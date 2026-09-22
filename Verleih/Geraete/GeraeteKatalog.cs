using Npgsql;

namespace Verleih.Geraete;

public sealed class GeraeteKatalog(NpgsqlDataSource quelle) : IGeraeteKatalog
{
    public async Task<IReadOnlyList<Geraet>> SucheNachKategorieAsync(string kategorie, CancellationToken ct = default)
    {
        await using var cmd = quelle.CreateCommand(
            "SELECT id, name, kategorie FROM verleih.geraet WHERE kategorie = $1 ORDER BY name");
        cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = kategorie });
        return await LeseAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<Geraet>> SucheNachNameAsync(string suchbegriff, CancellationToken ct = default)
    {
        await using var cmd = quelle.CreateCommand(
            "SELECT id, name, kategorie FROM verleih.geraet WHERE name ILIKE '%' || $1 || '%' ORDER BY name");
        cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = suchbegriff });
        return await LeseAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<Geraet>> LeseAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var ergebnis = new List<Geraet>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ergebnis.Add(new Geraet(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        return ergebnis;
    }
}
