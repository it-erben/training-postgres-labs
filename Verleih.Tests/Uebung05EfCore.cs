using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Verleih.Modell;
using Verleih.Tests.Infrastruktur;

namespace Verleih.Tests;

[Trait("Uebung", "05")]
public sealed class Uebung05EfCore(DatenbankFixture db) : IClassFixture<DatenbankFixture>
{
    private readonly Anweisungszaehler zaehler = new();
    private static int naechsterTag;

    private VerleihContext Kontext() => new(new DbContextOptionsBuilder<VerleihContext>()
        .UseNpgsql(db.Anwendung, VerleihContext.Konfiguriere)
        .AddInterceptors(zaehler)
        .Options);

    /// <summary>Legt Kunden mit Buchungen an und liefert ihre IDs.</summary>
    private async Task<List<int>> KundenMitBuchungenAsync(int kunden, int buchungenJeKunde)
    {
        var ids = new List<int>();
        for (var k = 0; k < kunden; k++)
        {
            var kunde = await db.KundeAnlegenAsync($"EF-Kunde {k:00}");
            ids.Add(kunde);
            for (var b = 0; b < buchungenJeKunde; b++)
            {
                await db.AusfuehrenAsync("""
                    INSERT INTO verleih.buchung (geraet_id, kunde_id, zeitraum, zustand_bei_abholung, zusatzinfo)
                    VALUES ($1, $2, tstzrange($3, $3 + interval '2 hours', '[)'), 'gebraucht', $4::jsonb)
                    """, 1 + b % 6, kunde, new DateTime(2027, 1, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(Interlocked.Increment(ref naechsterTag)),
                    $$$"""{"abholort": "Halle {{{b % 2 + 1}}}", "hinweise": []}""");
            }
        }
        return ids;
    }

    [Fact]
    public async Task Modell_passt_zum_bestehenden_Schema()
    {
        await KundenMitBuchungenAsync(2, 2);
        await using var ctx = Kontext();
        Assert.Equal(6, await ctx.Geraete.CountAsync());
        Assert.True(await ctx.Kunden.CountAsync() >= 2);
        var buchung = await ctx.Buchungen.OrderBy(b => b.Id).FirstAsync();
        Assert.Equal(DateTimeKind.Utc, buchung.Zeitraum.LowerBound.Kind);
        Assert.True(buchung.Zeitraum.LowerBoundIsInclusive);
        Assert.False(buchung.Zeitraum.UpperBoundIsInclusive);
        Assert.Equal(Buchungen.Geraetezustand.Gebraucht, buchung.ZustandBeiAbholung);
        Assert.StartsWith("Halle", buchung.Zusatz.Abholort);
    }

    [Fact]
    public async Task Zusatzinfo_ist_als_jsonb_abgebildet()
    {
        await KundenMitBuchungenAsync(1, 4);
        await using var ctx = Kontext();
        var abfrage = ctx.Buchungen.Where(b => b.Zusatz.Abholort == "Halle 2");
        Assert.Contains("->>", abfrage.ToQueryString());
        Assert.True(await abfrage.CountAsync() >= 2);
    }

    [Fact]
    public async Task Kundenuebersicht_braucht_hoechstens_zwei_Anweisungen()
    {
        var ids = await KundenMitBuchungenAsync(50, 3);
        await using var ctx = Kontext();
        zaehler.Zuruecksetzen();
        var uebersicht = await new Kundenuebersicht(ctx).LadeAsync();
        Assert.True(uebersicht.Count >= 50);
        Assert.Equal(150, uebersicht.Where(k => ids.Contains(k.Id)).Sum(k => k.Buchungen.Count));
        Assert.True(zaehler.Anweisungen <= 2,
            $"Die Übersicht hat {zaehler.Anweisungen} Anweisungen gesendet. Buchungen je Kunde nachzuladen ist N+1; Include oder eine Aufteilung in zwei Abfragen lösen das.");
    }

    [Fact]
    public async Task Uebersicht_laedt_ohne_Tracking()
    {
        await KundenMitBuchungenAsync(3, 1);
        await using var ctx = Kontext();
        await new Kundenuebersicht(ctx).LadeAsync();
        Assert.Empty(ctx.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Bonusgutschrift_nutzt_ExecuteUpdate()
    {
        var mit = (await KundenMitBuchungenAsync(5, 1))[0];
        var ohne = await db.KundeAnlegenAsync("Ohne Buchung");
        await using var ctx = Kontext();
        zaehler.Zuruecksetzen();
        var betroffen = await new Kundenuebersicht(ctx).BonusGutschriftAsync(10);
        Assert.True(betroffen >= 5);
        Assert.Equal(1, zaehler.Anweisungen);
        Assert.Contains("UPDATE", zaehler.Letzte, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, await db.SkalarAsync<int>("SELECT bonuspunkte FROM verleih.kunde WHERE id = $1", mit));
        Assert.Equal(0, await db.SkalarAsync<int>("SELECT bonuspunkte FROM verleih.kunde WHERE id = $1", ohne));
    }

    [Fact]
    public async Task Gleichzeitige_Aenderung_wird_ueber_xmin_erkannt()
    {
        var id = await db.KundeAnlegenAsync("Versioniert");
        await using var erste = Kontext();
        await using var zweite = Kontext();
        var k1 = await erste.Kunden.SingleAsync(k => k.Id == id);
        var k2 = await zweite.Kunden.SingleAsync(k => k.Id == id);

        k2.Name = "Versioniert (zweite Sitzung)";
        await zweite.SaveChangesAsync();

        k1.Name = "Versioniert (erste Sitzung)";
        zaehler.Zuruecksetzen();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => erste.SaveChangesAsync());
        Assert.Contains("xmin", zaehler.Letzte, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Versioniert (zweite Sitzung)", await db.SkalarAsync<string>("SELECT name FROM verleih.kunde WHERE id = $1", id));
    }

    [Fact]
    [Trait("Stretch", "ja")]
    public async Task Buchungsdauer_wird_auf_dem_Server_berechnet()
    {
        await KundenMitBuchungenAsync(1, 2);
        await using var ctx = Kontext();
        zaehler.Zuruecksetzen();
        var stunden = await ctx.Buchungen
            .Select(b => (b.Zeitraum.UpperBound - b.Zeitraum.LowerBound).TotalHours)
            .ToListAsync();
        Assert.All(stunden, h => Assert.Equal(2, h));
        Assert.Equal(1, zaehler.Anweisungen);
        Assert.DoesNotContain("zusatzinfo", zaehler.Letzte, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Anweisungszaehler : DbCommandInterceptor
    {
        public int Anweisungen { get; private set; }
        public string Letzte { get; private set; } = "";

        public void Zuruecksetzen() { Anweisungen = 0; Letzte = ""; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Anweisungen++; Letzte = command.CommandText; return base.ReaderExecutingAsync(command, eventData, result, ct); }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Anweisungen++; Letzte = command.CommandText; return base.NonQueryExecutingAsync(command, eventData, result, ct); }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
        { Anweisungen++; Letzte = command.CommandText; return base.ScalarExecutingAsync(command, eventData, result, ct); }
    }
}
