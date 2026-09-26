using Npgsql;

namespace Rental.Bookings;

public sealed class BookingStore(NpgsqlDataSource dataSource) : IBookingStore
{
    public Task<long> CreateAsync(Booking booking, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 2: Buchung mit tstzrange, Enum und jsonb anlegen.");
    }

    /// <summary>Einfügen auf einer vorhandenen Verbindung; Übung 3 nutzt dieselbe Anweisung in ihrer Transaktion.</summary>
    public static Task<long> CreateAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Booking booking, CancellationToken ct)
    {
        throw new NotImplementedException("Übung 2: Einfügen auf vorhandener Verbindung.");
    }

    public Task<Booking?> LoadAsync(long id, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 2: Buchung laden, Zeitpunkte als Utc.");
    }

    public Task<IReadOnlyList<Booking>> FindByPickupLocationAsync(string pickupLocation, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 2: Suche über jsonb-Parameter mit @>.");
    }
}
