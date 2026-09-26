using Npgsql;

namespace Rental.Bookings;

/// <summary>
/// Bucht ein Gerät für einen Kunden. Die Regel "höchstens drei Buchungen je
/// Kunde" wird innerhalb einer serialisierbaren Transaktion geprüft; ein
/// Serialisierungskonflikt wiederholt die gesamte Transaktion.
/// </summary>
public sealed class BookingService(NpgsqlDataSource dataSource)
{
    public const int MaxBookingsPerCustomer = 3;

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Nur für Tests: wird innerhalb der Transaktion vor der fachlichen Prüfung aufgerufen.</summary>
    public Func<NpgsqlConnection, CancellationToken, Task>? BeforeCheck { get; set; }

    /// <summary>Bonus: serialisiert Buchungen desselben Kunden über einen Advisory Lock.</summary>
    public bool UseAdvisoryLock { get; set; }

    public Task<long> BookAsync(Booking booking, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 3: serialisierbare Transaktion mit Prüfung, Wiederholung und Fehlerzuordnung.");
    }

    public static bool IsRetryable(PostgresException e)
    {
        throw new NotImplementedException("Übung 3: 40001 und 40P01 sind wiederholbar.");
    }
}
