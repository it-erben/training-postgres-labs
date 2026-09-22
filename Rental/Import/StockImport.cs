using Npgsql;

namespace Rental.Import;

/// <summary>Eine Zeile der CSV ist fachlich ungültig; nichts wurde gespeichert.</summary>
public sealed class ImportException(int line, string reason) : Exception($"Zeile {line}: {reason}")
{
    public int Line { get; } = line;
}

public interface IStockImport
{
    /// <summary>
    /// Liest Bestandsbewegungen als CSV (device_id;moved_at;quantity;remark, ohne Kopfzeile)
    /// und schreibt sie vollständig oder gar nicht. Liefert die Zahl der Zeilen.
    /// </summary>
    Task<long> ImportAsync(Stream csv, CancellationToken ct = default);

    /// <summary>Bonus: setzt Bestände je Gerät in einem Roundtrip, vorhandene werden überschrieben.</summary>
    Task UpdateStockAsync(IReadOnlyList<(int DeviceId, int Quantity)> stockLevels, CancellationToken ct = default);
}

public sealed class StockImport(NpgsqlDataSource dataSource) : IStockImport
{
    public Task<long> ImportAsync(Stream csv, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 4: zeilenweise lesen, prüfen, über COPY schreiben.");
    }

    public Task UpdateStockAsync(IReadOnlyList<(int DeviceId, int Quantity)> stockLevels, CancellationToken ct = default)
    {
        throw new NotImplementedException("Bonus Übung 4: Upsert über unnest und ON CONFLICT.");
    }
}
