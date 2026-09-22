using Npgsql;
using NpgsqlTypes;

namespace Rental.Bookings;

public sealed class BookingStore(NpgsqlDataSource dataSource) : IBookingStore
{
    public const string InsertSql = """
        INSERT INTO rental.booking (device_id, customer_id, time_range, condition_at_pickup, extra_info)
        VALUES ($1, $2, tstzrange($3, $4, '[)'), $5, $6)
        RETURNING id
        """;

    private const string SelectSql = """
        SELECT id, device_id, customer_id, lower(time_range), upper(time_range), condition_at_pickup, extra_info
        FROM rental.booking
        """;

    public async Task<long> CreateAsync(Booking booking, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await CreateAsync(conn, null, booking, ct);
    }

    /// <summary>Einfügen auf einer vorhandenen Verbindung, damit Übung 3 dieselbe Anweisung in ihrer Transaktion nutzt.</summary>
    public static async Task<long> CreateAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Booking booking, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(InsertSql, conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter<int> { TypedValue = booking.DeviceId });
        cmd.Parameters.Add(new NpgsqlParameter<int> { TypedValue = booking.CustomerId });
        // Expliziter Typ: ein DateTime mit Kind = Local wird von Npgsql abgewiesen,
        // statt vom Server in der Sitzungszeitzone umgedeutet zu werden.
        cmd.Parameters.Add(new NpgsqlParameter { Value = booking.From, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)booking.To ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        cmd.Parameters.Add(new NpgsqlParameter { Value = booking.ConditionAtPickup });
        cmd.Parameters.Add(new NpgsqlParameter { Value = booking.Extra, NpgsqlDbType = NpgsqlDbType.Jsonb });
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<Booking?> LoadAsync(long id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(SelectSql + " WHERE id = $1");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        var matches = await ReadAllAsync(cmd, ct);
        return matches.Count == 0 ? null : matches[0];
    }

    public async Task<IReadOnlyList<Booking>> FindByPickupLocationAsync(string pickupLocation, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(SelectSql + " WHERE extra_info @> $1 ORDER BY id");
        cmd.Parameters.Add(new NpgsqlParameter { Value = new { pickupLocation }, NpgsqlDbType = NpgsqlDbType.Jsonb });
        return await ReadAllAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<Booking>> ReadAllAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var result = new List<Booking>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new Booking(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.GetFieldValue<DeviceCondition>(5),
                reader.GetFieldValue<ExtraInfo>(6)));
        }
        return result;
    }
}
