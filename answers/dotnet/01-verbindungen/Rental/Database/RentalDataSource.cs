using Npgsql;

namespace Rental.Database;

/// <summary>Der einzige Einstiegspunkt der Anwendung in die Datenbank.</summary>
public static class RentalDataSource
{
    public const string AppName = "rental";

    public static NpgsqlDataSource Create(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = AppName,
            MaxPoolSize = 4,
            MinPoolSize = 0,
            Timeout = 5,
            MaxAutoPrepare = 20,
            AutoPrepareMinUsages = 2,
            // Bonus aus Übung 1: keine Anweisung der Anwendung läuft länger als vier Sekunden.
            Options = "-c statement_timeout=4s",
        };
        return new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
    }
}
