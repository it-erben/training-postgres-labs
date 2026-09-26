using Npgsql;

namespace Rental.Devices;

public sealed class DeviceCatalog(NpgsqlDataSource dataSource) : IDeviceCatalog
{
    public async Task<IReadOnlyList<Device>> FindByCategoryAsync(string category, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT id, name, category FROM rental.device WHERE category = $1 ORDER BY name");
        cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = category });
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<Device>> FindByNameAsync(string searchTerm, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT id, name, category FROM rental.device WHERE name ILIKE '%' || $1 || '%' ORDER BY name");
        cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = searchTerm });
        return await ReadAllAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<Device>> ReadAllAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var result = new List<Device>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new Device(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }
}
