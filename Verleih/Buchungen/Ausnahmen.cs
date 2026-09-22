namespace Verleih.Buchungen;

/// <summary>Die fachliche Regel lehnt die Buchung ab, etwa weil der Kunde die Höchstzahl erreicht hat.</summary>
public sealed class BuchungAbgelehnt(string grund) : Exception(grund);

/// <summary>Ein Constraint des Servers hat die Buchung verhindert, etwa eine Überlappung.</summary>
public sealed class BuchungsKonflikt(string constraintName, Exception ursache)
    : Exception($"Buchung verletzt {constraintName}", ursache)
{
    public string ConstraintName { get; } = constraintName;
}
