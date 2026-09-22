using NpgsqlTypes;
using Verleih.Buchungen;

namespace Verleih.Modell;

/// <summary>Entitäten für EF Core über dem bestehenden Schema verleih.</summary>
public sealed class KundeEntitaet
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Bonuspunkte { get; set; }
    public List<BuchungEntitaet> Buchungen { get; set; } = [];
}

public sealed class GeraetEntitaet
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Kategorie { get; set; }
}

public sealed class BuchungEntitaet
{
    public long Id { get; set; }
    public int GeraetId { get; set; }
    public GeraetEntitaet Geraet { get; set; } = null!;
    public int KundeId { get; set; }
    public KundeEntitaet Kunde { get; set; } = null!;
    public NpgsqlRange<DateTime> Zeitraum { get; set; }
    public Geraetezustand ZustandBeiAbholung { get; set; }
    public required Zusatzinfo Zusatz { get; set; }
}

public sealed record KundeMitBuchungen(int Id, string Name, int Bonuspunkte, IReadOnlyList<BuchungEntitaet> Buchungen);

public interface IKundenuebersicht
{
    /// <summary>Alle Kunden mit ihren Buchungen, ohne Änderungsverfolgung, in höchstens zwei Anweisungen.</summary>
    Task<IReadOnlyList<KundeMitBuchungen>> LadeAsync(CancellationToken ct = default);

    /// <summary>Schreibt allen Kunden mit mindestens einer Buchung Punkte gut, in einer Anweisung ohne Laden.</summary>
    Task<int> BonusGutschriftAsync(int punkte, CancellationToken ct = default);
}
