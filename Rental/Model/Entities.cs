using NpgsqlTypes;
using Rental.Bookings;

namespace Rental.Model;

/// <summary>Entitäten für EF Core über dem bestehenden Schema rental.</summary>
public sealed class CustomerEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int BonusPoints { get; set; }
    public List<BookingEntity> Bookings { get; set; } = [];
}

public sealed class DeviceEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
}

public sealed class BookingEntity
{
    public long Id { get; set; }
    public int DeviceId { get; set; }
    public DeviceEntity Device { get; set; } = null!;
    public int CustomerId { get; set; }
    public CustomerEntity Customer { get; set; } = null!;
    public NpgsqlRange<DateTime> TimeRange { get; set; }
    public DeviceCondition ConditionAtPickup { get; set; }
    public required ExtraInfo Extra { get; set; }
}

public sealed record CustomerWithBookings(int Id, string Name, int BonusPoints, IReadOnlyList<BookingEntity> Bookings);

public interface ICustomerOverview
{
    /// <summary>Alle Kunden mit ihren Buchungen, ohne Änderungsverfolgung, in höchstens zwei Anweisungen.</summary>
    Task<IReadOnlyList<CustomerWithBookings>> LoadAsync(CancellationToken ct = default);

    /// <summary>Schreibt allen Kunden mit mindestens einer Buchung Punkte gut, in einer Anweisung ohne Laden.</summary>
    Task<int> CreditBonusAsync(int points, CancellationToken ct = default);
}
