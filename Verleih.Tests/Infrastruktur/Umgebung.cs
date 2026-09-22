using Npgsql;

namespace Verleih.Tests.Infrastruktur;

/// <summary>Verbindungsdaten aus der Umgebung; keine Zugangsdaten im Repository.</summary>
public static class Umgebung
{
    public const string Variable = "VERLEIH_CONNECTION";
    public const string VariableLesend = "VERLEIH_CONNECTION_RO";

    public static string Verbindung =>
        Environment.GetEnvironmentVariable(Variable)
        ?? throw new InvalidOperationException(
            $"{Variable} ist nicht gesetzt. Übung 0 beschreibt, wie die Verbindungszeichenfolge aus dem Secret entsteht.");

    public static string? VerbindungLesend => Environment.GetEnvironmentVariable(VariableLesend);

    /// <summary>Eigene DataSource der Tests, in pg_stat_activity als verleih_test erkennbar.</summary>
    public static NpgsqlDataSource Diagnose() =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(Verbindung)
        {
            ApplicationName = "verleih_test",
            MaxPoolSize = 8,
        }.ConnectionString);
}
