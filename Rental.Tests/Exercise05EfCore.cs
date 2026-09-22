using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rental.Model;
using Rental.Tests.Infrastructure;

namespace Rental.Tests;

[Trait("Exercise", "05")]
public sealed class Exercise05EfCore(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private readonly CommandCounter counter = new();
    private static int nextDay;

    private RentalContext CreateContext() => new(new DbContextOptionsBuilder<RentalContext>()
        .UseNpgsql(db.AppSource, RentalContext.ConfigureProvider)
        .AddInterceptors(counter)
        .Options);

    /// <summary>Legt Kunden mit Buchungen an und liefert ihre IDs.</summary>
    private async Task<List<int>> CustomersWithBookingsAsync(int customers, int bookingsPerCustomer)
    {
        var ids = new List<int>();
        for (var k = 0; k < customers; k++)
        {
            var customerId = await db.CreateCustomerAsync($"EF-Kunde {k:00}");
            ids.Add(customerId);
            for (var b = 0; b < bookingsPerCustomer; b++)
            {
                await db.ExecuteAsync("""
                    INSERT INTO rental.booking (device_id, customer_id, time_range, condition_at_pickup, extra_info)
                    VALUES ($1, $2, tstzrange($3, $3 + interval '2 hours', '[)'), 'used', $4::jsonb)
                    """, 1 + b % 6, customerId, new DateTime(2027, 1, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(Interlocked.Increment(ref nextDay)),
                    $$$"""{"pickupLocation": "Halle {{{b % 2 + 1}}}", "notes": []}""");
            }
        }
        return ids;
    }

    [Fact]
    public async Task Model_matches_existing_schema()
    {
        await CustomersWithBookingsAsync(2, 2);
        await using var ctx = CreateContext();
        Assert.Equal(6, await ctx.Devices.CountAsync());
        Assert.True(await ctx.Customers.CountAsync() >= 2);
        var booking = await ctx.Bookings.OrderBy(b => b.Id).FirstAsync();
        Assert.Equal(DateTimeKind.Utc, booking.TimeRange.LowerBound.Kind);
        Assert.True(booking.TimeRange.LowerBoundIsInclusive);
        Assert.False(booking.TimeRange.UpperBoundIsInclusive);
        Assert.Equal(Bookings.DeviceCondition.Used, booking.ConditionAtPickup);
        Assert.StartsWith("Halle", booking.Extra.PickupLocation);
    }

    [Fact]
    public async Task Extra_info_is_mapped_as_jsonb()
    {
        await CustomersWithBookingsAsync(1, 4);
        await using var ctx = CreateContext();
        var query = ctx.Bookings.Where(b => b.Extra.PickupLocation == "Halle 2");
        Assert.Contains("->>", query.ToQueryString());
        Assert.True(await query.CountAsync() >= 2);
    }

    [Fact]
    public async Task Customer_overview_needs_at_most_two_statements()
    {
        var ids = await CustomersWithBookingsAsync(50, 3);
        await using var ctx = CreateContext();
        counter.Reset();
        var overview = await new CustomerOverview(ctx).LoadAsync();
        Assert.True(overview.Count >= 50);
        Assert.Equal(150, overview.Where(k => ids.Contains(k.Id)).Sum(k => k.Bookings.Count));
        Assert.True(counter.Commands <= 2,
            $"Die Übersicht hat {counter.Commands} Anweisungen gesendet. Buchungen je Kunde nachzuladen ist N+1; Include oder eine Aufteilung in zwei Abfragen lösen das.");
    }

    [Fact]
    public async Task Overview_loads_without_tracking()
    {
        await CustomersWithBookingsAsync(3, 1);
        await using var ctx = CreateContext();
        await new CustomerOverview(ctx).LoadAsync();
        Assert.Empty(ctx.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Bonus_credit_uses_ExecuteUpdate()
    {
        var withBooking = (await CustomersWithBookingsAsync(5, 1))[0];
        var withoutBooking = await db.CreateCustomerAsync("Ohne Buchung");
        await using var ctx = CreateContext();
        counter.Reset();
        var affected = await new CustomerOverview(ctx).CreditBonusAsync(10);
        Assert.True(affected >= 5);
        Assert.Equal(1, counter.Commands);
        Assert.Contains("UPDATE", counter.Last, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, await db.ScalarAsync<int>("SELECT bonus_points FROM rental.customer WHERE id = $1", withBooking));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT bonus_points FROM rental.customer WHERE id = $1", withoutBooking));
    }

    [Fact]
    public async Task Concurrent_change_is_detected_via_xmin()
    {
        var id = await db.CreateCustomerAsync("Versioniert");
        await using var first = CreateContext();
        await using var second = CreateContext();
        var k1 = await first.Customers.SingleAsync(k => k.Id == id);
        var k2 = await second.Customers.SingleAsync(k => k.Id == id);

        k2.Name = "Versioniert (zweite Sitzung)";
        await second.SaveChangesAsync();

        k1.Name = "Versioniert (erste Sitzung)";
        counter.Reset();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
        Assert.Contains("xmin", counter.Last, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Versioniert (zweite Sitzung)", await db.ScalarAsync<string>("SELECT name FROM rental.customer WHERE id = $1", id));
    }

    [Fact]
    [Trait("Stretch", "true")]
    public async Task Booking_duration_is_computed_on_the_server()
    {
        await CustomersWithBookingsAsync(1, 2);
        await using var ctx = CreateContext();
        counter.Reset();
        var hours = await ctx.Bookings
            .Select(b => (b.TimeRange.UpperBound - b.TimeRange.LowerBound).TotalHours)
            .ToListAsync();
        Assert.All(hours, h => Assert.Equal(2, h));
        Assert.Equal(1, counter.Commands);
        Assert.DoesNotContain("extra_info", counter.Last, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands { get; private set; }
        public string Last { get; private set; } = "";

        public void Reset() { Commands = 0; Last = ""; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Commands++; Last = command.CommandText; return base.ReaderExecutingAsync(command, eventData, result, ct); }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Commands++; Last = command.CommandText; return base.NonQueryExecutingAsync(command, eventData, result, ct); }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
        { Commands++; Last = command.CommandText; return base.ScalarExecutingAsync(command, eventData, result, ct); }
    }
}
