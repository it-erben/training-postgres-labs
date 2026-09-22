using Npgsql;

namespace Verleih.Buchungen;

/// <summary>
/// Bucht ein Gerät für einen Kunden. Die Regel "höchstens drei Buchungen je
/// Kunde" wird innerhalb einer serialisierbaren Transaktion geprüft; ein
/// Serialisierungskonflikt wiederholt die gesamte Transaktion.
/// </summary>
public sealed class Buchungsdienst(NpgsqlDataSource quelle)
{
    public const int MaxBuchungenJeKunde = 3;

    public int MaxVersuche { get; set; } = 3;

    /// <summary>Nur für Tests: wird innerhalb der Transaktion vor der fachlichen Prüfung aufgerufen.</summary>
    public Func<NpgsqlConnection, CancellationToken, Task>? VorPruefung { get; set; }

    /// <summary>Bonus: serialisiert Buchungen desselben Kunden über einen Advisory Lock.</summary>
    public bool MitAdvisoryLock { get; set; }

    public Task<long> BucheAsync(Buchung buchung, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 3: serialisierbare Transaktion mit Prüfung, Wiederholung und Fehlerzuordnung.");
    }

    public static bool IstWiederholbar(PostgresException e)
    {
        throw new NotImplementedException("Übung 3: 40001 und 40P01 sind wiederholbar.");
    }
}
