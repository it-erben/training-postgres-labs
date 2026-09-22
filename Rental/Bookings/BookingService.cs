using System.Data;
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

    public async Task<long> BookAsync(Booking booking, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AttemptAsync(booking, ct);
            }
            catch (PostgresException e) when (IsRetryable(e) && attempt < MaxAttempts)
            {
                // Die gesamte Transaktion einschließlich der fachlichen Prüfung wird erneut ausgeführt.
            }
            catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.ExclusionViolation
                                                or PostgresErrorCodes.UniqueViolation
                                                or PostgresErrorCodes.CheckViolation
                                                or PostgresErrorCodes.ForeignKeyViolation)
            {
                throw new BookingConflictException(e.ConstraintName ?? e.SqlState, e);
            }
        }
    }

    public static bool IsRetryable(PostgresException e) =>
        e.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

    private async Task<long> AttemptAsync(Booking booking, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (UseAdvisoryLock)
        {
            await using var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx);
            lockCmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = booking.CustomerId });
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        if (BeforeCheck is not null)
        {
            await BeforeCheck(conn, ct);
        }

        await using (var countCmd = new NpgsqlCommand("SELECT count(*) FROM rental.booking WHERE customer_id = $1", conn, tx))
        {
            countCmd.Parameters.Add(new NpgsqlParameter<int> { TypedValue = booking.CustomerId });
            var existing = (long)(await countCmd.ExecuteScalarAsync(ct))!;
            if (existing >= MaxBookingsPerCustomer)
            {
                await tx.RollbackAsync(ct);
                throw new BookingRejectedException($"Kunde {booking.CustomerId} hat bereits {existing} Buchungen.");
            }
        }

        var id = await BookingStore.CreateAsync(conn, tx, booking, ct);
        await tx.CommitAsync(ct);
        return id;
    }
}
