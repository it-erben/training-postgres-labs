namespace Rental.Bookings;

/// <summary>Zustand des Geräts bei Abholung; auf dem Server der Enum-Typ rental.device_condition.</summary>
public enum DeviceCondition
{
    New,
    Used,
    Defective,
}

/// <summary>Wird als jsonb-Dokument in der Spalte extra_info abgelegt.</summary>
public sealed class ExtraInfo
{
    public required string PickupLocation { get; set; }
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Eine Buchung. From und To sind UTC-Zeitpunkte; To ist offen, wenn null.
/// Der Zeitraum ist halb offen: From gehört dazu, To nicht.
/// </summary>
public sealed record Booking(
    long Id,
    int DeviceId,
    int CustomerId,
    DateTime From,
    DateTime? To,
    DeviceCondition ConditionAtPickup,
    ExtraInfo Extra)
{
    public static Booking Create(int deviceId, int customerId, DateTime from, DateTime? to, DeviceCondition state, ExtraInfo extra) =>
        new(0, deviceId, customerId, from, to, state, extra);
}

public interface IBookingStore
{
    /// <summary>Legt die Buchung an und liefert die vergebene ID.</summary>
    Task<long> CreateAsync(Booking booking, CancellationToken ct = default);

    Task<Booking?> LoadAsync(long id, CancellationToken ct = default);

    /// <summary>Alle Buchungen mit dem angegebenen Abholort in der Zusatzinfo.</summary>
    Task<IReadOnlyList<Booking>> FindByPickupLocationAsync(string pickupLocation, CancellationToken ct = default);
}
