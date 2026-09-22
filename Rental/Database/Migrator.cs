using System.Reflection;
using Npgsql;

namespace Rental.Database;

/// <summary>
/// Spielt alle eingebetteten SQL-Migrationen in Namensreihenfolge ein.
/// Das Schema rental wird vorher vollständig entfernt; die Tests rufen den
/// Migrator vor jeder Testklasse auf, damit jeder Lauf vom selben Stand ausgeht.
/// </summary>
public static class Migrator
{
    public const string Schema = "rental";

    public static async Task RebuildAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        await using (var cmd = dataSource.CreateCommand($"DROP SCHEMA IF EXISTS {Schema} CASCADE; CREATE SCHEMA {Schema};"))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var assembly = typeof(Migrator).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in names)
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(sql))
            {
                continue;
            }
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
