namespace Rental.Bookings;

/// <summary>Die fachliche Regel lehnt die Buchung ab, etwa weil der Kunde die Höchstzahl erreicht hat.</summary>
public sealed class BookingRejectedException(string reason) : Exception(reason);

/// <summary>Ein Constraint des Servers hat die Buchung verhindert, etwa eine Überlappung.</summary>
public sealed class BookingConflictException(string constraintName, Exception inner)
    : Exception($"Buchung verletzt {constraintName}", inner)
{
    public string ConstraintName { get; } = constraintName;
}
