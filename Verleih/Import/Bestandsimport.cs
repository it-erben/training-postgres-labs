using Npgsql;

namespace Verleih.Import;

/// <summary>Eine Zeile der CSV ist fachlich ungültig; nichts wurde gespeichert.</summary>
public sealed class ImportFehler(int zeile, string grund) : Exception($"Zeile {zeile}: {grund}")
{
    public int Zeile { get; } = zeile;
}

public interface IBestandsimport
{
    /// <summary>
    /// Liest Bestandsbewegungen als CSV (geraet_id;zeitpunkt;menge;bemerkung, ohne Kopfzeile)
    /// und schreibt sie vollständig oder gar nicht. Liefert die Zahl der Zeilen.
    /// </summary>
    Task<long> ImportiereAsync(Stream csv, CancellationToken ct = default);

    /// <summary>Bonus: setzt Bestände je Gerät in einem Roundtrip, vorhandene werden überschrieben.</summary>
    Task AktualisiereBestandAsync(IReadOnlyList<(int GeraetId, int Menge)> bestaende, CancellationToken ct = default);
}

public sealed class Bestandsimport(NpgsqlDataSource quelle) : IBestandsimport
{
    public Task<long> ImportiereAsync(Stream csv, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 4: zeilenweise lesen, prüfen, über COPY schreiben.");
    }

    public Task AktualisiereBestandAsync(IReadOnlyList<(int GeraetId, int Menge)> bestaende, CancellationToken ct = default)
    {
        throw new NotImplementedException("Bonus Übung 4: Upsert über unnest und ON CONFLICT.");
    }
}
