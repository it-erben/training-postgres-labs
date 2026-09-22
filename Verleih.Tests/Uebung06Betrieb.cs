using Npgsql;
using Verleih.Betrieb;
using Verleih.Tests.Infrastruktur;

namespace Verleih.Tests;

[Trait("Uebung", "06")]
public sealed class Uebung06Betrieb(DatenbankFixture db) : IClassFixture<DatenbankFixture>
{
    private static int naechsterTag;

    /// <summary>Jede Buchung liegt an einem eigenen Tag in der Vergangenheit, damit keine Überlappung entsteht.</summary>
    private async Task<long> BuchungAsync()
    {
        var kunde = await db.KundeAnlegenAsync("Rueckgabe");
        var tag = Interlocked.Increment(ref naechsterTag);
        return await db.SkalarAsync<long>("""
            INSERT INTO verleih.buchung (geraet_id, kunde_id, zeitraum, zustand_bei_abholung)
            VALUES (1, $1, tstzrange(timestamptz '2026-01-01' + make_interval(days => $2), timestamptz '2026-01-01' + make_interval(days => $2 + 1), '[)'), 'gebraucht')
            RETURNING id
            """, kunde, tag);
    }

    /// <summary>Eigener Empfänger der Tests, unabhängig vom Melder der Anwendung.</summary>
    private async Task<(NpgsqlConnection Verbindung, List<string> Empfangen)> LauscherAsync()
    {
        var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Verbindung) { ApplicationName = "verleih_test_lauscher", Pooling = false }.ConnectionString);
        await conn.OpenAsync();
        var empfangen = new List<string>();
        conn.Notification += (_, e) => empfangen.Add(e.Payload);
        await using var cmd = new NpgsqlCommand($"LISTEN {Kanaele.Rueckgabe}", conn);
        await cmd.ExecuteNonQueryAsync();
        return (conn, empfangen);
    }

    [Fact]
    public async Task Rueckgabe_sendet_Benachrichtigung_nach_Commit()
    {
        var id = await BuchungAsync();
        var (lauscher, empfangen) = await LauscherAsync();
        await using (lauscher)
        {
            await new RueckgabeDienst(db.Anwendung).ZurueckgebenAsync(id);
            Assert.True(await lauscher.WaitAsync(3000), "Innerhalb von 3 s kam keine Benachrichtigung an.");
            Assert.Equal([id.ToString()], empfangen);
            Assert.NotNull(await db.SkalarAsync<object>("SELECT zurueckgegeben_am FROM verleih.buchung WHERE id = $1", id));
        }
    }

    [Fact]
    public async Task Benachrichtigung_kommt_nicht_vor_dem_Commit()
    {
        var id = await BuchungAsync();
        var (lauscher, empfangen) = await LauscherAsync();
        await using (lauscher)
        {
            bool? vorCommit = null;
            var dienst = new RueckgabeDienst(db.Anwendung)
            {
                VorCommit = async () => vorCommit = await lauscher.WaitAsync(500),
            };
            await dienst.ZurueckgebenAsync(id);
            Assert.False(vorCommit, "Die Benachrichtigung kam vor dem COMMIT an; NOTIFY muss in derselben Transaktion wie das UPDATE laufen.");
            Assert.True(await lauscher.WaitAsync(3000));
            Assert.Equal([id.ToString()], empfangen);
        }
    }

    [Fact]
    public async Task Zurueckgerollte_Rueckgabe_sendet_nichts()
    {
        var id = await BuchungAsync();
        var (lauscher, empfangen) = await LauscherAsync();
        await using (lauscher)
        {
            var dienst = new RueckgabeDienst(db.Anwendung) { VorCommit = () => throw new InvalidOperationException("Test bricht ab") };
            await Assert.ThrowsAsync<InvalidOperationException>(() => dienst.ZurueckgebenAsync(id));
            Assert.False(await lauscher.WaitAsync(500));
            Assert.Empty(empfangen);
            Assert.Null(await db.SkalarAsync<object>("SELECT zurueckgegeben_am FROM verleih.buchung WHERE id = $1", id) as DateTime?);
            Assert.Equal(0L, await db.VerbindungenAsync(zustand: "idle in transaction"));
        }
    }

    [Fact]
    public async Task Empfaenger_verwendet_eine_dedizierte_Verbindung_mit_Keepalive()
    {
        var melder = new RueckgabeMelder(db.Verbindung);
        var einstellungen = new NpgsqlConnectionStringBuilder(melder.Verbindungszeichenfolge);
        Assert.True(einstellungen.KeepAlive > 0, "Keepalive ist nicht gesetzt.");
        Assert.Equal(RueckgabeMelder.AnwendungsName, einstellungen.ApplicationName);

        using var cts = new CancellationTokenSource();
        var empfangen = new TaskCompletionSource<long>();
        var lauf = melder.LaufeAsync(id => { empfangen.TrySetResult(id); return Task.CompletedTask; }, cts.Token);
        Assert.True(await DatenbankFixture.WarteBisAsync(async () => await db.VerbindungenAsync(RueckgabeMelder.AnwendungsName) == 1, TimeSpan.FromSeconds(5)),
            "Es gibt keine Serververbindung mit application_name = verleih_melder.");

        var id = await BuchungAsync();
        await new RueckgabeDienst(db.Anwendung).ZurueckgebenAsync(id);
        Assert.Equal(id, await empfangen.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        cts.Cancel();
        await lauf;
    }

    [Fact]
    public async Task Diagnose_listet_eigene_Verbindungen_mit_Zustand()
    {
        using var cts = new CancellationTokenSource();
        var lauf = new RueckgabeMelder(db.Verbindung).LaufeAsync(_ => Task.CompletedTask, cts.Token);
        await DatenbankFixture.WarteBisAsync(async () => await db.VerbindungenAsync(RueckgabeMelder.AnwendungsName) == 1, TimeSpan.FromSeconds(5));

        var verbindungen = await new Diagnose(db.Anwendung).EigeneVerbindungenAsync();
        Assert.Contains(verbindungen, v => v.AnwendungsName == RueckgabeMelder.AnwendungsName && v.Zustand == "idle");
        Assert.Contains(verbindungen, v => v.AnwendungsName == "verleih" && v.Zustand == "active");
        Assert.DoesNotContain(verbindungen, v => v.AnwendungsName == "verleih_test");

        cts.Cancel();
        await lauf;
    }

    [Fact]
    public async Task Leseabfragen_bevorzugen_das_Replikat()
    {
        var verbindung = Umgebung.VerbindungLesend;
        Assert.SkipWhen(verbindung is null, $"{Umgebung.VariableLesend} ist nicht gesetzt; ohne Replikat wird dieser Test übersprungen.");
        await using var lesend = Lesequelle.Erzeuge(verbindung!);
        await using var cmd = lesend.CreateCommand("SELECT pg_is_in_recovery()");
        Assert.True((bool)(await cmd.ExecuteScalarAsync())!, "Die Leseverbindung landet auf dem Primärserver; Target Session Attributes prüfen.");
    }

    [Fact]
    [Trait("Stretch", "ja")]
    public async Task Melder_verbindet_nach_Verbindungsabbruch_neu()
    {
        using var cts = new CancellationTokenSource();
        var empfangen = new List<long>();
        var lauf = new RueckgabeMelder(db.Verbindung).LaufeAsync(id => { lock (empfangen) empfangen.Add(id); return Task.CompletedTask; }, cts.Token);
        await DatenbankFixture.WarteBisAsync(async () => await db.VerbindungenAsync(RueckgabeMelder.AnwendungsName) == 1, TimeSpan.FromSeconds(5));

        var pid = await db.SkalarAsync<int>("SELECT pid FROM pg_stat_activity WHERE application_name = $1", RueckgabeMelder.AnwendungsName);
        await db.AusfuehrenAsync("SELECT pg_terminate_backend($1)", pid);
        Assert.True(await DatenbankFixture.WarteBisAsync(async () =>
            await db.SkalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE application_name = $1 AND pid <> $2", RueckgabeMelder.AnwendungsName, pid) == 1,
            TimeSpan.FromSeconds(10)), "Der Melder hat nach dem Abbruch keine neue Verbindung aufgebaut.");

        var id = await BuchungAsync();
        await new RueckgabeDienst(db.Anwendung).ZurueckgebenAsync(id);
        Assert.True(await DatenbankFixture.WarteBisAsync(() => Task.FromResult(empfangen.Contains(id)), TimeSpan.FromSeconds(5)),
            "Nach dem Neuaufbau kam die Rückgabe nicht an.");

        cts.Cancel();
        await lauf;
    }
}
