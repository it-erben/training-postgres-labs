using Npgsql;

namespace Rental.Database;

/// <summary>Der einzige Einstiegspunkt der Anwendung in die Datenbank.</summary>
public static class RentalDataSource
{
    public const string AppName = "rental";

    public static NpgsqlDataSource Create(string connectionString)
    {
        throw new NotImplementedException("Übung 1: RentalDataSource.Create baut die NpgsqlDataSource mit Application Name, Poolgrenze, Timeout und Auto-Prepare.");
    }
}
