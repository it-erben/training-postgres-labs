using System.Globalization;
using Npgsql;
using NpgsqlTypes;

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
    public async Task<long> ImportiereAsync(Stream csv, CancellationToken ct = default)
    {
        await using var conn = await quelle.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Ohne CompleteAsync verwirft der Server alle bereits übertragenen Zeilen.
        await using var importer = await conn.BeginBinaryImportAsync(
            "COPY verleih.bestandsbewegung (geraet_id, zeitpunkt, menge, bemerkung) FROM STDIN (FORMAT BINARY)", ct);

        using var leser = new StreamReader(csv);
        long zeilen = 0;
        string? zeile;
        while ((zeile = await leser.ReadLineAsync(ct)) is not null)
        {
            zeilen++;
            var felder = zeile.Split(';');
            if (felder.Length != 4)
            {
                throw new ImportFehler((int)zeilen, "vier Felder erwartet");
            }
            if (!int.TryParse(felder[0], CultureInfo.InvariantCulture, out var geraetId))
            {
                throw new ImportFehler((int)zeilen, "geraet_id ist keine Zahl");
            }
            if (!DateTime.TryParse(felder[1], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var zeitpunkt))
            {
                throw new ImportFehler((int)zeilen, "zeitpunkt ist kein Datum");
            }
            if (!int.TryParse(felder[2], CultureInfo.InvariantCulture, out var menge) || menge <= 0)
            {
                throw new ImportFehler((int)zeilen, "menge muss größer als 0 sein");
            }

            await importer.StartRowAsync(ct);
            await importer.WriteAsync(geraetId, NpgsqlDbType.Integer, ct);
            await importer.WriteAsync(zeitpunkt, NpgsqlDbType.TimestampTz, ct);
            await importer.WriteAsync(menge, NpgsqlDbType.Integer, ct);
            if (felder[3].Length == 0)
            {
                await importer.WriteNullAsync(ct);
            }
            else
            {
                await importer.WriteAsync(felder[3], NpgsqlDbType.Text, ct);
            }
        }

        await importer.CompleteAsync(ct);
        await importer.DisposeAsync();
        await tx.CommitAsync(ct);
        return zeilen;
    }

    public async Task AktualisiereBestandAsync(IReadOnlyList<(int GeraetId, int Menge)> bestaende, CancellationToken ct = default)
    {
        await using var cmd = quelle.CreateCommand("""
            INSERT INTO verleih.bestand (geraet_id, menge)
            SELECT * FROM unnest($1::int[], $2::int[])
            ON CONFLICT (geraet_id) DO UPDATE SET menge = EXCLUDED.menge
            """);
        cmd.Parameters.Add(new NpgsqlParameter { Value = bestaende.Select(b => b.GeraetId).ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter { Value = bestaende.Select(b => b.Menge).ToArray() });
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
