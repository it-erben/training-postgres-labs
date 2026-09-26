using System.Globalization;
using Npgsql;
using NpgsqlTypes;

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
    public async Task<long> ImportAsync(Stream csv, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Ohne CompleteAsync verwirft der Server alle bereits übertragenen Zeilen.
        await using var importer = await conn.BeginBinaryImportAsync(
            "COPY rental.stock_movement (device_id, moved_at, quantity, remark) FROM STDIN (FORMAT BINARY)", ct);

        using var reader = new StreamReader(csv);
        long lines = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lines++;
            var fields = line.Split(';');
            if (fields.Length != 4)
            {
                throw new ImportException((int)lines, "vier Felder erwartet");
            }
            if (!int.TryParse(fields[0], CultureInfo.InvariantCulture, out var deviceId))
            {
                throw new ImportException((int)lines, "device_id ist keine Zahl");
            }
            if (!DateTime.TryParse(fields[1], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var movedAt))
            {
                throw new ImportException((int)lines, "moved_at ist kein Datum");
            }
            if (!int.TryParse(fields[2], CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
            {
                throw new ImportException((int)lines, "quantity muss größer als 0 sein");
            }

            await importer.StartRowAsync(ct);
            await importer.WriteAsync(deviceId, NpgsqlDbType.Integer, ct);
            await importer.WriteAsync(movedAt, NpgsqlDbType.TimestampTz, ct);
            await importer.WriteAsync(quantity, NpgsqlDbType.Integer, ct);
            if (fields[3].Length == 0)
            {
                await importer.WriteNullAsync(ct);
            }
            else
            {
                await importer.WriteAsync(fields[3], NpgsqlDbType.Text, ct);
            }
        }

        await importer.CompleteAsync(ct);
        await importer.DisposeAsync();
        await tx.CommitAsync(ct);
        return lines;
    }

    public async Task UpdateStockAsync(IReadOnlyList<(int DeviceId, int Quantity)> stockLevels, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO rental.stock (device_id, quantity)
            SELECT * FROM unnest($1::int[], $2::int[])
            ON CONFLICT (device_id) DO UPDATE SET quantity = EXCLUDED.quantity
            """);
        cmd.Parameters.Add(new NpgsqlParameter { Value = stockLevels.Select(b => b.DeviceId).ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter { Value = stockLevels.Select(b => b.Quantity).ToArray() });
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
