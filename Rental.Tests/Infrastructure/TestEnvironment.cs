using Npgsql;

namespace Rental.Tests.Infrastructure;

/// <summary>Verbindungsdaten aus der Umgebung; keine Zugangsdaten im Repository.</summary>
public static class TestEnvironment
{
    public const string ConnectionVariable = "RENTAL_CONNECTION";
    public const string ReadOnlyConnectionVariable = "RENTAL_CONNECTION_RO";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionVariable)
        ?? throw new InvalidOperationException(
            $"{ConnectionVariable} ist nicht gesetzt. Übung 0 beschreibt, wie die Verbindungszeichenfolge aus dem Secret entsteht.");

    public static string? ReadOnlyConnectionString => Environment.GetEnvironmentVariable(ReadOnlyConnectionVariable);

    /// <summary>Eigene DataSource der Tests, in pg_stat_activity als rental_test erkennbar.</summary>
    public static NpgsqlDataSource CreateAdminSource() =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            ApplicationName = "rental_test",
            MaxPoolSize = 8,
        }.ConnectionString);
}
