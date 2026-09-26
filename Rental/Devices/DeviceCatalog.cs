using Npgsql;

namespace Rental.Devices;

public sealed class DeviceCatalog(NpgsqlDataSource dataSource) : IDeviceCatalog
{
    public Task<IReadOnlyList<Device>> FindByCategoryAsync(string category, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 1: Suche nach Kategorie mit Parameter.");
    }

    public Task<IReadOnlyList<Device>> FindByNameAsync(string searchTerm, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 1: Suche nach Namensbestandteil mit Parameter.");
    }
}
