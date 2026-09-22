using Npgsql;

namespace Verleih.Datenbank;

/// <summary>Der einzige Einstiegspunkt der Anwendung in die Datenbank.</summary>
public static class Datenquelle
{
    public const string AnwendungsName = "verleih";

    public static NpgsqlDataSource Erzeuge(string verbindung)
    {
        throw new NotImplementedException("Übung 1: Datenquelle.Erzeuge baut die NpgsqlDataSource mit Application Name, Poolgrenze, Timeout und Auto-Prepare.");
    }
}
