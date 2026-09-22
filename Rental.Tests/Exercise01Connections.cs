using Npgsql;
using Rental.Database;
using Rental.Devices;
using Rental.Tests.Infrastructure;

namespace Rental.Tests;

[Trait("Exercise", "01")]
public sealed class Exercise01Connections(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private IDeviceCatalog Catalog => new DeviceCatalog(db.AppSource);

    [Fact]
    public async Task Find_by_category_returns_expected_devices()
    {
        var devices = await Catalog.FindByCategoryAsync("drills");
        Assert.Equal(["Akkuschrauber", "Bohrhammer"], devices.Select(g => g.Name));
        Assert.All(devices, g => Assert.Equal("drills", g.Category));
    }

    [Fact]
    public async Task Search_uses_parameters_not_concatenation()
    {
        var matches = await Catalog.FindByNameAsync("'; DROP TABLE rental.device; --");
        Assert.Empty(matches);
        Assert.Equal(6L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.device"));

        var saws = await Catalog.FindByNameAsync("säge");
        Assert.Equal(["Kreissäge", "Stichsäge"], saws.Select(g => g.Name));
    }

    [Fact]
    public async Task DataSource_sets_application_name_rental()
    {
        await Catalog.FindByCategoryAsync("access");
        Assert.True(await db.CountConnectionsAsync() >= 1,
            "Keine Serververbindung mit application_name = rental gefunden. Die DataSource muss den Application Name setzen.");
    }

    [Fact]
    public async Task Connection_returns_to_pool_after_use()
    {
        await Catalog.FindByCategoryAsync("saws");
        Assert.Equal(0L, await db.CountConnectionsAsync(state: "active"));
        Assert.Equal(0L, await db.CountConnectionsAsync(state: "idle in transaction"));
    }

    [Fact]
    public async Task Pool_holds_at_most_four_server_connections()
    {
        var held = new List<NpgsqlConnection>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                held.Add(await db.AppSource.OpenConnectionAsync());
            }
            Assert.Equal(4L, await db.CountConnectionsAsync());

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<NpgsqlException>(async () =>
            {
                await using var fifth = await db.AppSource.OpenConnectionAsync();
            });
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8),
                $"Die fünfte Verbindung hat {stopwatch.Elapsed.TotalSeconds:F0} s gewartet; Timeout in der Verbindungszeichenfolge auf höchstens 5 s setzen.");
            Assert.Contains("pool", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var c in held)
            {
                await c.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task DataSource_auto_prepares_repeated_statements()
    {
        var settings = new NpgsqlConnectionStringBuilder(db.AppSource.ConnectionString);
        Assert.True(settings.MaxAutoPrepare >= 5, "Max Auto Prepare ist nicht gesetzt.");

        await using var conn = await db.AppSource.OpenConnectionAsync();
        for (var i = 0; i < 3; i++)
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM rental.device WHERE category = $1", conn);
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "drills" });
            await cmd.ExecuteScalarAsync();
        }
        await using var countCmd = new NpgsqlCommand("SELECT count(*) FROM pg_prepared_statements", conn);
        Assert.True((long)(await countCmd.ExecuteScalarAsync())! >= 1,
            "Nach drei Ausführungen derselben Anweisung ist auf dieser Verbindung kein Prepared Statement vorhanden.");
    }

    [Fact]
    [Trait("Stretch", "true")]
    public async Task Statement_timeout_applies_per_connection()
    {
        await using var cmd = db.AppSource.CreateCommand("SELECT pg_sleep(5)");
        var error = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.QueryCanceled, error.SqlState);
    }
}
