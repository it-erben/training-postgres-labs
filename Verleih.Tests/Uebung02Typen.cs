using Npgsql;
using Verleih.Buchungen;
using Verleih.Tests.Infrastruktur;

namespace Verleih.Tests;

[Trait("Uebung", "02")]
public sealed class Uebung02Typen(DatenbankFixture db) : IClassFixture<DatenbankFixture>
{
    private IBuchungen Buchungen => new Buchungsablage(db.Anwendung);

    private static readonly DateTime Start = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private static Buchung Neue(int geraetId, DateTime von, DateTime? bis, string abholort = "Halle 1") =>
        Buchung.Neu(geraetId, 1, von, bis, Geraetezustand.Gebraucht, new Zusatzinfo { Abholort = abholort, Hinweise = ["Schlüssel an der Pforte"] });

    [Fact]
    public async Task Buchung_speichert_Zeitraum_als_tstzrange()
    {
        var id = await Buchungen.AnlegenAsync(Neue(1, Start, Start.AddHours(4)));
        await using var cmd = db.Diagnose.CreateCommand(
            "SELECT pg_typeof(zeitraum)::text, lower(zeitraum), upper(zeitraum), lower_inc(zeitraum), upper_inc(zeitraum) FROM verleih.buchung WHERE id = $1");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("tstzrange", reader.GetString(0));
        Assert.Equal(Start, reader.GetDateTime(1));
        Assert.Equal(Start.AddHours(4), reader.GetDateTime(2));
        Assert.True(reader.GetBoolean(3), "Die untere Grenze muss zum Zeitraum gehören.");
        Assert.False(reader.GetBoolean(4), "Die obere Grenze darf nicht zum Zeitraum gehören.");
    }

    [Fact]
    public async Task Zeitpunkte_kommen_als_Utc_zurueck()
    {
        var id = await Buchungen.AnlegenAsync(Neue(2, Start, Start.AddHours(2)));
        var geladen = await Buchungen.LadeAsync(id);
        Assert.NotNull(geladen);
        Assert.Equal(DateTimeKind.Utc, geladen.Von.Kind);
        Assert.Equal(Start, geladen.Von);
        Assert.Equal(Start.AddHours(2), geladen.Bis);
    }

    [Fact]
    public async Task Lokale_Zeitpunkte_werden_abgewiesen()
    {
        var lokal = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Local);
        await Assert.ThrowsAsync<ArgumentException>(() => Buchungen.AnlegenAsync(Neue(3, lokal, lokal.AddHours(1))));
        Assert.Equal(0L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.buchung WHERE geraet_id = 3"));
    }

    [Fact]
    public async Task Ueberlappende_Buchung_desselben_Geraets_scheitert_mit_23P01()
    {
        await Buchungen.AnlegenAsync(Neue(4, Start, Start.AddHours(4)));
        var fehler = await Assert.ThrowsAsync<PostgresException>(() =>
            Buchungen.AnlegenAsync(Neue(4, Start.AddHours(2), Start.AddHours(6))));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, fehler.SqlState);
        Assert.False(string.IsNullOrEmpty(fehler.ConstraintName), "Der EXCLUDE-Constraint braucht einen Namen.");
    }

    [Fact]
    public async Task Angrenzende_Buchungen_sind_erlaubt()
    {
        await Buchungen.AnlegenAsync(Neue(5, Start, Start.AddHours(2)));
        await Buchungen.AnlegenAsync(Neue(5, Start.AddHours(2), Start.AddHours(4)));
        Assert.Equal(2L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.buchung WHERE geraet_id = 5"));
    }

    [Fact]
    public async Task Ueberlappung_anderer_Geraete_ist_erlaubt()
    {
        await Buchungen.AnlegenAsync(Neue(6, Start, Start.AddHours(4)));
        await Buchungen.AnlegenAsync(Neue(1, Start.AddDays(1), Start.AddDays(1).AddHours(4)));
        await Buchungen.AnlegenAsync(Neue(2, Start.AddDays(1), Start.AddDays(1).AddHours(4)));
    }

    [Fact]
    public async Task Zusatzinfo_wird_als_jsonb_mit_camelCase_gespeichert()
    {
        var id = await Buchungen.AnlegenAsync(Neue(1, Start.AddDays(2), Start.AddDays(2).AddHours(1), "Halle 3"));
        Assert.Equal("jsonb", await db.SkalarAsync<string>("SELECT pg_typeof(zusatzinfo)::text FROM verleih.buchung WHERE id = $1", id));
        Assert.Equal("Halle 3", await db.SkalarAsync<string>("SELECT zusatzinfo ->> 'abholort' FROM verleih.buchung WHERE id = $1", id));
        Assert.Equal(1, await db.SkalarAsync<int>("SELECT jsonb_array_length(zusatzinfo -> 'hinweise') FROM verleih.buchung WHERE id = $1", id));
    }

    [Fact]
    public async Task Suche_nach_Zusatzinfo_verwendet_jsonb_Parameter()
    {
        await Buchungen.AnlegenAsync(Neue(2, Start.AddDays(3), Start.AddDays(3).AddHours(1), "Halle 7"));
        await Buchungen.AnlegenAsync(Neue(3, Start.AddDays(3), Start.AddDays(3).AddHours(1), "Halle 7"));
        var treffer = await Buchungen.SucheNachAbholortAsync("Halle 7");
        Assert.Equal(2, treffer.Count);
        Assert.All(treffer, b => Assert.Equal("Halle 7", b.Zusatz.Abholort));
        Assert.All(treffer, b => Assert.Equal(["Schlüssel an der Pforte"], b.Zusatz.Hinweise));
    }

    [Fact]
    public async Task Geraetezustand_ist_ein_Enum_auf_beiden_Seiten()
    {
        Assert.Equal('e', await db.SkalarAsync<char>(
            "SELECT typtype FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'verleih' AND t.typname = 'geraetezustand'"));
        var buchung = Neue(4, Start.AddDays(4), Start.AddDays(4).AddHours(1)) with { ZustandBeiAbholung = Geraetezustand.Defekt };
        var id = await Buchungen.AnlegenAsync(buchung);
        Assert.Equal("defekt", await db.SkalarAsync<string>("SELECT zustand_bei_abholung::text FROM verleih.buchung WHERE id = $1", id));
        Assert.Equal(Geraetezustand.Defekt, (await Buchungen.LadeAsync(id))!.ZustandBeiAbholung);
    }

    [Fact]
    [Trait("Stretch", "ja")]
    public async Task Zeitraum_ohne_Ende_bedeutet_offene_Buchung()
    {
        await Buchungen.AnlegenAsync(Neue(6, Start.AddDays(10), null));
        var fehler = await Assert.ThrowsAsync<PostgresException>(() =>
            Buchungen.AnlegenAsync(Neue(6, Start.AddDays(30), Start.AddDays(31))));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, fehler.SqlState);
        var geladen = await Buchungen.SucheNachAbholortAsync("Halle 1");
        Assert.Contains(geladen, b => b.GeraetId == 6 && b.Bis is null);
    }
}
