using Npgsql;
using Verleih.Datenbank;

namespace Verleih.Tests.Infrastruktur;

/// <summary>
/// Baut das Schema verleih vor jeder Testklasse neu auf und stellt zwei
/// DataSources bereit: die der Anwendung und eine eigene für Serverabfragen.
/// </summary>
public sealed class DatenbankFixture : IAsyncLifetime
{
    public NpgsqlDataSource Diagnose { get; } = Umgebung.Diagnose();

    private NpgsqlDataSource? anwendung;

    /// <summary>DataSource der Anwendung; entsteht beim ersten Zugriff über Datenquelle.Erzeuge.</summary>
    public NpgsqlDataSource Anwendung => anwendung ??= Datenquelle.Erzeuge(Umgebung.Verbindung);

    public string Verbindung => Umgebung.Verbindung;

    public async ValueTask InitializeAsync()
    {
        await Migrator.NeuAufbauenAsync(Diagnose);
    }

    public async ValueTask DisposeAsync()
    {
        if (anwendung is not null)
        {
            await anwendung.DisposeAsync();
        }
        await Diagnose.DisposeAsync();
    }

    public async Task<T> SkalarAsync<T>(string sql, params object[] parameter)
    {
        await using var cmd = Diagnose.CreateCommand(sql);
        foreach (var p in parameter)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = p });
        }
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task AusfuehrenAsync(string sql, params object[] parameter)
    {
        await using var cmd = Diagnose.CreateCommand(sql);
        foreach (var p in parameter)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = p });
        }
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Serververbindungen der Anwendung nach application_name, optional nach Zustand.</summary>
    public Task<long> VerbindungenAsync(string anwendungsName = Datenquelle.AnwendungsName, string? zustand = null) =>
        zustand is null
            ? SkalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = $1", anwendungsName)
            : SkalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = $1 AND state = $2", anwendungsName, zustand);

    public async Task<int> KundeAnlegenAsync(string name) =>
        await SkalarAsync<int>("INSERT INTO verleih.kunde (name) VALUES ($1) RETURNING id", name);

    /// <summary>Wartet, bis eine Bedingung erfüllt ist, höchstens bis zum Timeout.</summary>
    public static async Task<bool> WarteBisAsync(Func<Task<bool>> bedingung, TimeSpan timeout)
    {
        var ende = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < ende)
        {
            if (await bedingung())
            {
                return true;
            }
            await Task.Delay(50);
        }
        return false;
    }
}
