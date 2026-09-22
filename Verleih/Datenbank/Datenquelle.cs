using Npgsql;

namespace Verleih.Datenbank;

/// <summary>Der einzige Einstiegspunkt der Anwendung in die Datenbank.</summary>
public static class Datenquelle
{
    public const string AnwendungsName = "verleih";

    public static NpgsqlDataSource Erzeuge(string verbindung)
    {
        var einstellungen = new NpgsqlConnectionStringBuilder(verbindung)
        {
            ApplicationName = AnwendungsName,
            MaxPoolSize = 4,
            MinPoolSize = 0,
            Timeout = 5,
            MaxAutoPrepare = 20,
            AutoPrepareMinUsages = 2,
            // Bonus aus Übung 1: keine Anweisung der Anwendung läuft länger als vier Sekunden.
            Options = "-c statement_timeout=4s",
        };
        return new NpgsqlDataSourceBuilder(einstellungen.ConnectionString).Build();
    }
}
