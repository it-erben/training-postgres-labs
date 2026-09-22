namespace Verleih.Buchungen;

/// <summary>Zustand des Geräts bei Abholung; auf dem Server der Enum-Typ verleih.geraetezustand.</summary>
public enum Geraetezustand
{
    Neu,
    Gebraucht,
    Defekt,
}

/// <summary>Wird als jsonb-Dokument in der Spalte zusatzinfo abgelegt.</summary>
public sealed class Zusatzinfo
{
    public required string Abholort { get; set; }
    public List<string> Hinweise { get; set; } = [];
}

/// <summary>
/// Eine Buchung. Von und Bis sind UTC-Zeitpunkte; Bis ist offen, wenn null.
/// Der Zeitraum ist halb offen: Von gehört dazu, Bis nicht.
/// </summary>
public sealed record Buchung(
    long Id,
    int GeraetId,
    int KundeId,
    DateTime Von,
    DateTime? Bis,
    Geraetezustand ZustandBeiAbholung,
    Zusatzinfo Zusatz)
{
    public static Buchung Neu(int geraetId, int kundeId, DateTime von, DateTime? bis, Geraetezustand zustand, Zusatzinfo zusatz) =>
        new(0, geraetId, kundeId, von, bis, zustand, zusatz);
}

public interface IBuchungen
{
    /// <summary>Legt die Buchung an und liefert die vergebene ID.</summary>
    Task<long> AnlegenAsync(Buchung buchung, CancellationToken ct = default);

    Task<Buchung?> LadeAsync(long id, CancellationToken ct = default);

    /// <summary>Alle Buchungen mit dem angegebenen Abholort in der Zusatzinfo.</summary>
    Task<IReadOnlyList<Buchung>> SucheNachAbholortAsync(string abholort, CancellationToken ct = default);
}
