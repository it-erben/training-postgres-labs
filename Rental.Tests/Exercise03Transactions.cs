using Npgsql;
using Rental.Bookings;
using Rental.Tests.Infrastructure;

namespace Rental.Tests;

[Trait("Exercise", "03")]
public sealed class Exercise03Transactions(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Start = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
    private static int nextDay;

    private BookingService Service => new(db.AppSource);

    /// <summary>Jede Buchung bekommt einen eigenen Tag, damit nur beabsichtigte Überlappungen entstehen.</summary>
    private static Booking NewBooking(int customerId, int deviceId, int? day = null)
    {
        var t = day ?? Interlocked.Increment(ref nextDay);
        return Booking.Create(deviceId, customerId, Start.AddDays(t), Start.AddDays(t).AddHours(2), DeviceCondition.Used,
            new ExtraInfo { PickupLocation = "Halle 1" });
    }

    private static Task ConflictAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var cmd = new NpgsqlCommand("DO $$ BEGIN RAISE EXCEPTION 'Testkonflikt' USING ERRCODE = '40001'; END $$", conn);
        return cmd.ExecuteNonQueryAsync(ct);
    }

    [Fact]
    public async Task Booking_runs_in_a_serializable_transaction()
    {
        var customerId = await db.CreateCustomerAsync("Isolation");
        string? level = null;
        var service = Service;
        service.BeforeCheck = async (conn, ct) =>
        {
            await using var cmd = new NpgsqlCommand("SELECT current_setting('transaction_isolation')", conn);
            level = (string)(await cmd.ExecuteScalarAsync(ct))!;
        };
        await service.BookAsync(NewBooking(customerId, 1));
        Assert.Equal("serializable", level);
    }

    [Fact]
    public async Task Fourth_booking_is_rejected_by_business_rule()
    {
        var customerId = await db.CreateCustomerAsync("Vielbucher");
        for (var i = 0; i < 3; i++)
        {
            await Service.BookAsync(NewBooking(customerId, 1));
        }
        await Assert.ThrowsAsync<BookingRejectedException>(() => Service.BookAsync(NewBooking(customerId, 1)));
        Assert.Equal(3L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.booking WHERE customer_id = $1", customerId));
    }

    [Fact]
    public async Task Two_concurrent_third_bookings_let_exactly_one_through()
    {
        var customerId = await db.CreateCustomerAsync("Gleichzeitig");
        await Service.BookAsync(NewBooking(customerId, 1));
        await Service.BookAsync(NewBooking(customerId, 2));

        // Beide Transaktionen sollen die Prüfung gesehen haben, bevor eine von beiden bestätigt.
        var arrived = 0;
        var barrier = new TaskCompletionSource();
        var service = Service;
        service.BeforeCheck = async (_, ct) =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                barrier.TrySetResult();
            }
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };

        var a = Task.Run(() => service.BookAsync(NewBooking(customerId, 3)));
        var b = Task.Run(() => service.BookAsync(NewBooking(customerId, 4)));
        var results = await Task.WhenAll(CaptureAsync(a), CaptureAsync(b));

        Assert.Equal(1, results.Count(e => e is null));
        Assert.Equal(1, results.Count(e => e is BookingRejectedException));
        Assert.Equal(3L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.booking WHERE customer_id = $1", customerId));
    }

    private static async Task<Exception?> CaptureAsync(Task<long> task)
    {
        try { await task; return null; }
        catch (Exception e) { return e; }
    }

    [Fact]
    public async Task Conflict_is_attempted_at_most_three_times()
    {
        var customerId = await db.CreateCustomerAsync("Dauerkonflikt");
        var calls = 0;
        var service = Service;
        service.BeforeCheck = (conn, ct) => { calls++; return ConflictAsync(conn, ct); };

        var error = await Assert.ThrowsAsync<PostgresException>(() => service.BookAsync(NewBooking(customerId, 2)));
        Assert.Equal(PostgresErrorCodes.SerializationFailure, error.SqlState);
        Assert.Equal(3, calls);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.booking WHERE customer_id = $1", customerId));
    }

    [Fact]
    public async Task Retry_repeats_the_business_check()
    {
        var customerId = await db.CreateCustomerAsync("Einmalkonflikt");
        var calls = 0;
        var service = Service;
        service.BeforeCheck = (conn, ct) => ++calls == 1 ? ConflictAsync(conn, ct) : Task.CompletedTask;

        var id = await service.BookAsync(NewBooking(customerId, 3));
        Assert.Equal(2, calls);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.booking WHERE id = $1", id));
    }

    [Fact]
    public async Task Constraint_violation_is_not_retried()
    {
        var customerId = await db.CreateCustomerAsync("Ueberlappung");
        var calls = 0;
        var service = Service;
        service.BeforeCheck = (_, _) => { calls++; return Task.CompletedTask; };

        await service.BookAsync(NewBooking(customerId, 5, day: 500));
        var error = await Assert.ThrowsAsync<BookingConflictException>(() => service.BookAsync(NewBooking(customerId, 5, day: 500)));
        Assert.Equal("booking_no_overlap", error.ConstraintName);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task No_open_transaction_remains_after_an_error()
    {
        var customerId = await db.CreateCustomerAsync("Aufraeumen");
        var service = Service;
        service.BeforeCheck = ConflictAsync;
        await Assert.ThrowsAsync<PostgresException>(() => service.BookAsync(NewBooking(customerId, 4)));

        Assert.Equal(0L, await db.CountConnectionsAsync(state: "idle in transaction"));
        Assert.Equal(0L, await db.CountConnectionsAsync(state: "idle in transaction (aborted)"));
    }

    [Fact]
    [Trait("Stretch", "true")]
    public async Task Cancellation_token_ends_the_server_query()
    {
        var customerId = await db.CreateCustomerAsync("Abbruch");
        var calls = 0;
        var service = Service;
        service.BeforeCheck = async (conn, ct) =>
        {
            calls++;
            await using var cmd = new NpgsqlCommand("SELECT pg_sleep(10)", conn);
            await cmd.ExecuteNonQueryAsync(ct);
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BookAsync(NewBooking(customerId, 5), cts.Token));
        Assert.Equal(1, calls);
        Assert.True(await DatabaseFixture.WaitUntilAsync(async () => await db.CountConnectionsAsync(state: "active") == 0, TimeSpan.FromSeconds(5)),
            "Nach dem Abbruch ist weiterhin eine Anwendungsverbindung aktiv.");
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.booking WHERE customer_id = $1", customerId));
    }

    [Fact]
    [Trait("Stretch", "true")]
    public async Task Advisory_lock_per_customer_avoids_the_conflict()
    {
        var customerId = await db.CreateCustomerAsync("Advisory");
        long? held = null;
        var service = Service;
        service.UseAdvisoryLock = true;
        service.BeforeCheck = async (conn, ct) =>
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid() AND granted", conn);
            held = (long)(await cmd.ExecuteScalarAsync(ct))!;
        };
        await service.BookAsync(NewBooking(customerId, 6));
        Assert.Equal(1L, held);
    }
}
