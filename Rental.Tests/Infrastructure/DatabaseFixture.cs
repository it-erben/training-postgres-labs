using Npgsql;
using Rental.Database;

namespace Rental.Tests.Infrastructure;

/// <summary>
/// Baut das Schema rental vor jeder Testklasse neu auf und stellt zwei
/// DataSources bereit: die der Anwendung und eine eigene für Serverabfragen.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public NpgsqlDataSource AdminSource { get; } = TestEnvironment.CreateAdminSource();

    private NpgsqlDataSource? appSource;

    /// <summary>DataSource der Anwendung; entsteht beim ersten Zugriff über RentalDataSource.Create.</summary>
    public NpgsqlDataSource AppSource => appSource ??= RentalDataSource.Create(TestEnvironment.ConnectionString);

    public string ConnectionString => TestEnvironment.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await Migrator.RebuildAsync(AdminSource);
    }

    public async ValueTask DisposeAsync()
    {
        if (appSource is not null)
        {
            await appSource.DisposeAsync();
        }
        await AdminSource.DisposeAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var cmd = AdminSource.CreateCommand(sql);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = p });
        }
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task ExecuteAsync(string sql, params object[] parameters)
    {
        await using var cmd = AdminSource.CreateCommand(sql);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = p });
        }
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Serververbindungen der Anwendung nach application_name, optional nach Zustand.</summary>
    public Task<long> CountConnectionsAsync(string appName = RentalDataSource.AppName, string? state = null) =>
        state is null
            ? ScalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = $1", appName)
            : ScalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = $1 AND state = $2", appName, state);

    public async Task<int> CreateCustomerAsync(string name) =>
        await ScalarAsync<int>("INSERT INTO rental.customer (name) VALUES ($1) RETURNING id", name);

    /// <summary>Wartet, bis eine Bedingung erfüllt ist, höchstens bis zum Timeout.</summary>
    public static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }
            await Task.Delay(50);
        }
        return false;
    }
}
