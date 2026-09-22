using System.Reflection;
using Npgsql;

namespace Verleih.Datenbank;

/// <summary>
/// Spielt alle eingebetteten SQL-Migrationen in Namensreihenfolge ein.
/// Das Schema verleih wird vorher vollständig entfernt; die Tests rufen den
/// Migrator vor jeder Testklasse auf, damit jeder Lauf vom selben Stand ausgeht.
/// </summary>
public static class Migrator
{
    public const string Schema = "verleih";

    public static async Task NeuAufbauenAsync(NpgsqlDataSource quelle, CancellationToken ct = default)
    {
        await using (var cmd = quelle.CreateCommand($"DROP SCHEMA IF EXISTS {Schema} CASCADE; CREATE SCHEMA {Schema};"))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var assembly = typeof(Migrator).Assembly;
        var namen = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in namen)
        {
            await using var strom = assembly.GetManifestResourceStream(name)!;
            using var leser = new StreamReader(strom);
            var sql = await leser.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(sql))
            {
                continue;
            }
            await using var conn = await quelle.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
