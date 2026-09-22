using Npgsql;
using Rental.Bookings;
using Rental.Tests.Infrastructure;

namespace Rental.Tests;

[Trait("Exercise", "02")]
public sealed class Exercise02Types(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private IBookingStore Bookings => new BookingStore(db.AppSource);

    private static readonly DateTime Start = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private static Booking NewBooking(int deviceId, DateTime from, DateTime? to, string pickupLocation = "Halle 1") =>
        Booking.Create(deviceId, 1, from, to, DeviceCondition.Used, new ExtraInfo { PickupLocation = pickupLocation, Notes = ["Schlüssel an der Pforte"] });

    [Fact]
    public async Task Booking_stores_time_range_as_tstzrange()
    {
        var id = await Bookings.CreateAsync(NewBooking(1, Start, Start.AddHours(4)));
        await using var cmd = db.AdminSource.CreateCommand(
            "SELECT pg_typeof(time_range)::text, lower(time_range), upper(time_range), lower_inc(time_range), upper_inc(time_range) FROM rental.booking WHERE id = $1");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("tstzrange", reader.GetString(0));
        Assert.Equal(Start, reader.GetDateTime(1));
        Assert.Equal(Start.AddHours(4), reader.GetDateTime(2));
        Assert.True(reader.GetBoolean(3), "Die untere Grenze muss zum Zeitraum gehören.");
        Assert.False(reader.GetBoolean(4), "Die obere Grenze darf nicht zum Zeitraum gehören.");
    }

    [Fact]
    public async Task Timestamps_come_back_as_utc()
    {
        var id = await Bookings.CreateAsync(NewBooking(2, Start, Start.AddHours(2)));
        var loaded = await Bookings.LoadAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal(DateTimeKind.Utc, loaded.From.Kind);
        Assert.Equal(Start, loaded.From);
        Assert.Equal(Start.AddHours(2), loaded.To);
    }

    [Fact]
    public async Task Local_timestamps_are_rejected()
    {
        // Der Abholort kennzeichnet die abgewiesene Buchung; andere Tests buchen Gerät 3 ebenfalls.
        var local = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Local);
        await Assert.ThrowsAsync<ArgumentException>(() => Bookings.CreateAsync(NewBooking(3, local, local.AddHours(1), "Halle 9")));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.booking WHERE extra_info ->> 'pickupLocation' = 'Halle 9'"));
    }

    [Fact]
    public async Task Overlapping_booking_of_same_device_fails_with_23P01()
    {
        await Bookings.CreateAsync(NewBooking(4, Start, Start.AddHours(4)));
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Bookings.CreateAsync(NewBooking(4, Start.AddHours(2), Start.AddHours(6))));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, error.SqlState);
        Assert.False(string.IsNullOrEmpty(error.ConstraintName), "Der EXCLUDE-Constraint braucht einen Namen.");
    }

    [Fact]
    public async Task Adjacent_bookings_are_allowed()
    {
        await Bookings.CreateAsync(NewBooking(5, Start, Start.AddHours(2)));
        await Bookings.CreateAsync(NewBooking(5, Start.AddHours(2), Start.AddHours(4)));
        Assert.Equal(2L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.booking WHERE device_id = 5"));
    }

    [Fact]
    public async Task Overlap_of_different_devices_is_allowed()
    {
        await Bookings.CreateAsync(NewBooking(6, Start, Start.AddHours(4)));
        await Bookings.CreateAsync(NewBooking(1, Start.AddDays(1), Start.AddDays(1).AddHours(4)));
        await Bookings.CreateAsync(NewBooking(2, Start.AddDays(1), Start.AddDays(1).AddHours(4)));
    }

    [Fact]
    public async Task Extra_info_is_stored_as_jsonb_in_camelCase()
    {
        var id = await Bookings.CreateAsync(NewBooking(1, Start.AddDays(2), Start.AddDays(2).AddHours(1), "Halle 3"));
        Assert.Equal("jsonb", await db.ScalarAsync<string>("SELECT pg_typeof(extra_info)::text FROM rental.booking WHERE id = $1", id));
        Assert.Equal("Halle 3", await db.ScalarAsync<string>("SELECT extra_info ->> 'pickupLocation' FROM rental.booking WHERE id = $1", id));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT jsonb_array_length(extra_info -> 'notes') FROM rental.booking WHERE id = $1", id));
    }

    [Fact]
    public async Task Find_by_extra_info_uses_jsonb_parameter()
    {
        await Bookings.CreateAsync(NewBooking(2, Start.AddDays(3), Start.AddDays(3).AddHours(1), "Halle 7"));
        await Bookings.CreateAsync(NewBooking(3, Start.AddDays(3), Start.AddDays(3).AddHours(1), "Halle 7"));
        var matches = await Bookings.FindByPickupLocationAsync("Halle 7");
        Assert.Equal(2, matches.Count);
        Assert.All(matches, b => Assert.Equal("Halle 7", b.Extra.PickupLocation));
        Assert.All(matches, b => Assert.Equal(["Schlüssel an der Pforte"], b.Extra.Notes));
    }

    [Fact]
    public async Task Device_condition_is_an_enum_on_both_sides()
    {
        Assert.Equal('e', await db.ScalarAsync<char>(
            "SELECT typtype FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'rental' AND t.typname = 'device_condition'"));
        var booking = NewBooking(4, Start.AddDays(4), Start.AddDays(4).AddHours(1)) with { ConditionAtPickup = DeviceCondition.Defective };
        var id = await Bookings.CreateAsync(booking);
        Assert.Equal("defective", await db.ScalarAsync<string>("SELECT condition_at_pickup::text FROM rental.booking WHERE id = $1", id));
        Assert.Equal(DeviceCondition.Defective, (await Bookings.LoadAsync(id))!.ConditionAtPickup);
    }

    [Fact]
    [Trait("Stretch", "true")]
    public async Task Time_range_without_end_means_open_booking()
    {
        await Bookings.CreateAsync(NewBooking(6, Start.AddDays(10), null));
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Bookings.CreateAsync(NewBooking(6, Start.AddDays(30), Start.AddDays(31))));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, error.SqlState);
        var loaded = await Bookings.FindByPickupLocationAsync("Halle 1");
        Assert.Contains(loaded, b => b.DeviceId == 6 && b.To is null);
    }
}
