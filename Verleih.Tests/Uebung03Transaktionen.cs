using Npgsql;
using Verleih.Buchungen;
using Verleih.Tests.Infrastruktur;

namespace Verleih.Tests;

[Trait("Uebung", "03")]
public sealed class Uebung03Transaktionen(DatenbankFixture db) : IClassFixture<DatenbankFixture>
{
    private static readonly DateTime Start = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
    private static int naechsterTag;

    private Buchungsdienst Dienst => new(db.Anwendung);

    /// <summary>Jede Buchung bekommt einen eigenen Tag, damit nur beabsichtigte Überlappungen entstehen.</summary>
    private static Buchung Neue(int kundeId, int geraetId, int? tag = null)
    {
        var t = tag ?? Interlocked.Increment(ref naechsterTag);
        return Buchung.Neu(geraetId, kundeId, Start.AddDays(t), Start.AddDays(t).AddHours(2), Geraetezustand.Gebraucht,
            new Zusatzinfo { Abholort = "Halle 1" });
    }

    private static Task Konflikt(NpgsqlConnection conn, CancellationToken ct)
    {
        var cmd = new NpgsqlCommand("DO $$ BEGIN RAISE EXCEPTION 'Testkonflikt' USING ERRCODE = '40001'; END $$", conn);
        return cmd.ExecuteNonQueryAsync(ct);
    }

    [Fact]
    public async Task Buchung_laeuft_in_einer_serialisierbaren_Transaktion()
    {
        var kunde = await db.KundeAnlegenAsync("Isolation");
        string? stufe = null;
        var dienst = Dienst;
        dienst.VorPruefung = async (conn, ct) =>
        {
            await using var cmd = new NpgsqlCommand("SELECT current_setting('transaction_isolation')", conn);
            stufe = (string)(await cmd.ExecuteScalarAsync(ct))!;
        };
        await dienst.BucheAsync(Neue(kunde, 1));
        Assert.Equal("serializable", stufe);
    }

    [Fact]
    public async Task Vierte_Buchung_wird_fachlich_abgelehnt()
    {
        var kunde = await db.KundeAnlegenAsync("Vielbucher");
        for (var i = 0; i < 3; i++)
        {
            await Dienst.BucheAsync(Neue(kunde, 1));
        }
        await Assert.ThrowsAsync<BuchungAbgelehnt>(() => Dienst.BucheAsync(Neue(kunde, 1)));
        Assert.Equal(3L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.buchung WHERE kunde_id = $1", kunde));
    }

    [Fact]
    public async Task Zwei_gleichzeitige_dritte_Buchungen_lassen_genau_eine_durch()
    {
        var kunde = await db.KundeAnlegenAsync("Gleichzeitig");
        await Dienst.BucheAsync(Neue(kunde, 1));
        await Dienst.BucheAsync(Neue(kunde, 2));

        // Beide Transaktionen sollen die Prüfung gesehen haben, bevor eine von beiden bestätigt.
        var angekommen = 0;
        var schranke = new TaskCompletionSource();
        var dienst = Dienst;
        dienst.VorPruefung = async (_, ct) =>
        {
            if (Interlocked.Increment(ref angekommen) == 2)
            {
                schranke.TrySetResult();
            }
            await schranke.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };

        var a = Task.Run(() => dienst.BucheAsync(Neue(kunde, 3)));
        var b = Task.Run(() => dienst.BucheAsync(Neue(kunde, 4)));
        var ergebnisse = await Task.WhenAll(Sicher(a), Sicher(b));

        Assert.Equal(1, ergebnisse.Count(e => e is null));
        Assert.Equal(1, ergebnisse.Count(e => e is BuchungAbgelehnt));
        Assert.Equal(3L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.buchung WHERE kunde_id = $1", kunde));
    }

    private static async Task<Exception?> Sicher(Task<long> aufgabe)
    {
        try { await aufgabe; return null; }
        catch (Exception e) { return e; }
    }

    [Fact]
    public async Task Konflikt_wird_hoechstens_dreimal_versucht()
    {
        var kunde = await db.KundeAnlegenAsync("Dauerkonflikt");
        var aufrufe = 0;
        var dienst = Dienst;
        dienst.VorPruefung = (conn, ct) => { aufrufe++; return Konflikt(conn, ct); };

        var fehler = await Assert.ThrowsAsync<PostgresException>(() => dienst.BucheAsync(Neue(kunde, 2)));
        Assert.Equal(PostgresErrorCodes.SerializationFailure, fehler.SqlState);
        Assert.Equal(3, aufrufe);
        Assert.Equal(0L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.buchung WHERE kunde_id = $1", kunde));
    }

    [Fact]
    public async Task Wiederholung_fuehrt_die_fachliche_Pruefung_erneut_aus()
    {
        var kunde = await db.KundeAnlegenAsync("Einmalkonflikt");
        var aufrufe = 0;
        var dienst = Dienst;
        dienst.VorPruefung = (conn, ct) => ++aufrufe == 1 ? Konflikt(conn, ct) : Task.CompletedTask;

        var id = await dienst.BucheAsync(Neue(kunde, 3));
        Assert.Equal(2, aufrufe);
        Assert.Equal(1L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.buchung WHERE id = $1", id));
    }

    [Fact]
    public async Task Constraint_Verletzung_wird_nicht_wiederholt()
    {
        var kunde = await db.KundeAnlegenAsync("Ueberlappung");
        var aufrufe = 0;
        var dienst = Dienst;
        dienst.VorPruefung = (_, _) => { aufrufe++; return Task.CompletedTask; };

        await dienst.BucheAsync(Neue(kunde, 5, tag: 500));
        var fehler = await Assert.ThrowsAsync<BuchungsKonflikt>(() => dienst.BucheAsync(Neue(kunde, 5, tag: 500)));
        Assert.Equal("buchung_keine_ueberlappung", fehler.ConstraintName);
        Assert.Equal(2, aufrufe);
    }

    [Fact]
    public async Task Nach_einem_Fehler_bleibt_keine_offene_Transaktion()
    {
        var kunde = await db.KundeAnlegenAsync("Aufraeumen");
        var dienst = Dienst;
        dienst.VorPruefung = Konflikt;
        await Assert.ThrowsAsync<PostgresException>(() => dienst.BucheAsync(Neue(kunde, 4)));

        Assert.Equal(0L, await db.VerbindungenAsync(zustand: "idle in transaction"));
        Assert.Equal(0L, await db.VerbindungenAsync(zustand: "idle in transaction (aborted)"));
    }

    [Fact]
    [Trait("Stretch", "ja")]
    public async Task Abbruch_ueber_Token_beendet_die_Serverabfrage()
    {
        var kunde = await db.KundeAnlegenAsync("Abbruch");
        var aufrufe = 0;
        var dienst = Dienst;
        dienst.VorPruefung = async (conn, ct) =>
        {
            aufrufe++;
            await using var cmd = new NpgsqlCommand("SELECT pg_sleep(10)", conn);
            await cmd.ExecuteNonQueryAsync(ct);
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dienst.BucheAsync(Neue(kunde, 5), cts.Token));
        Assert.Equal(1, aufrufe);
        Assert.True(await DatenbankFixture.WarteBisAsync(async () => await db.VerbindungenAsync(zustand: "active") == 0, TimeSpan.FromSeconds(5)),
            "Nach dem Abbruch ist weiterhin eine Anwendungsverbindung aktiv.");
        Assert.Equal(0L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.buchung WHERE kunde_id = $1", kunde));
    }

    [Fact]
    [Trait("Stretch", "ja")]
    public async Task Advisory_Lock_je_Kunde_vermeidet_den_Konflikt()
    {
        var kunde = await db.KundeAnlegenAsync("Advisory");
        long? gehalten = null;
        var dienst = Dienst;
        dienst.MitAdvisoryLock = true;
        dienst.VorPruefung = async (conn, ct) =>
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid() AND granted", conn);
            gehalten = (long)(await cmd.ExecuteScalarAsync(ct))!;
        };
        await dienst.BucheAsync(Neue(kunde, 6));
        Assert.Equal(1L, gehalten);
    }
}
