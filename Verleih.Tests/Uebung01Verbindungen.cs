using Npgsql;
using Verleih.Datenbank;
using Verleih.Geraete;
using Verleih.Tests.Infrastruktur;

namespace Verleih.Tests;

[Trait("Uebung", "01")]
public sealed class Uebung01Verbindungen(DatenbankFixture db) : IClassFixture<DatenbankFixture>
{
    private IGeraeteKatalog Katalog => new GeraeteKatalog(db.Anwendung);

    [Fact]
    public async Task Suche_nach_Kategorie_liefert_erwartete_Geraete()
    {
        var geraete = await Katalog.SucheNachKategorieAsync("Bohrer");
        Assert.Equal(["Akkuschrauber", "Bohrhammer"], geraete.Select(g => g.Name));
        Assert.All(geraete, g => Assert.Equal("Bohrer", g.Kategorie));
    }

    [Fact]
    public async Task Suche_verwendet_Parameter_statt_Verkettung()
    {
        var treffer = await Katalog.SucheNachNameAsync("'; DROP TABLE verleih.geraet; --");
        Assert.Empty(treffer);
        Assert.Equal(6L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.geraet"));

        var saegen = await Katalog.SucheNachNameAsync("säge");
        Assert.Equal(["Kreissäge", "Stichsäge"], saegen.Select(g => g.Name));
    }

    [Fact]
    public async Task DataSource_traegt_Application_Name_verleih()
    {
        await Katalog.SucheNachKategorieAsync("Zugang");
        Assert.True(await db.VerbindungenAsync() >= 1,
            "Keine Serververbindung mit application_name = verleih gefunden. Die DataSource muss den Application Name setzen.");
    }

    [Fact]
    public async Task Verbindung_wird_nach_Gebrauch_an_den_Pool_zurueckgegeben()
    {
        await Katalog.SucheNachKategorieAsync("Sägen");
        Assert.Equal(0L, await db.VerbindungenAsync(zustand: "active"));
        Assert.Equal(0L, await db.VerbindungenAsync(zustand: "idle in transaction"));
    }

    [Fact]
    public async Task Pool_haelt_hoechstens_vier_Serververbindungen()
    {
        var gehalten = new List<NpgsqlConnection>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                gehalten.Add(await db.Anwendung.OpenConnectionAsync());
            }
            Assert.Equal(4L, await db.VerbindungenAsync());

            var uhr = System.Diagnostics.Stopwatch.StartNew();
            var fehler = await Assert.ThrowsAsync<NpgsqlException>(async () =>
            {
                await using var fuenfte = await db.Anwendung.OpenConnectionAsync();
            });
            Assert.True(uhr.Elapsed < TimeSpan.FromSeconds(8),
                $"Die fünfte Verbindung hat {uhr.Elapsed.TotalSeconds:F0} s gewartet; Timeout in der Verbindungszeichenfolge auf höchstens 5 s setzen.");
            Assert.Contains("pool", fehler.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var c in gehalten)
            {
                await c.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task DataSource_bereitet_wiederholte_Anweisungen_automatisch_vor()
    {
        var einstellungen = new NpgsqlConnectionStringBuilder(db.Anwendung.ConnectionString);
        Assert.True(einstellungen.MaxAutoPrepare >= 5, "Max Auto Prepare ist nicht gesetzt.");

        await using var conn = await db.Anwendung.OpenConnectionAsync();
        for (var i = 0; i < 3; i++)
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM verleih.geraet WHERE kategorie = $1", conn);
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Bohrer" });
            await cmd.ExecuteScalarAsync();
        }
        await using var zaehl = new NpgsqlCommand("SELECT count(*) FROM pg_prepared_statements", conn);
        Assert.True((long)(await zaehl.ExecuteScalarAsync())! >= 1,
            "Nach drei Ausführungen derselben Anweisung ist auf dieser Verbindung kein Prepared Statement vorhanden.");
    }

    [Fact]
    [Trait("Stretch", "ja")]
    public async Task Statement_Timeout_gilt_je_Verbindung()
    {
        await using var cmd = db.Anwendung.CreateCommand("SELECT pg_sleep(5)");
        var fehler = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.QueryCanceled, fehler.SqlState);
    }
}
