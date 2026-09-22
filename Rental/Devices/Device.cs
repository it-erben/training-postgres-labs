namespace Rental.Devices;

public sealed record Device(int Id, string Name, string Category);

public interface IDeviceCatalog
{
    /// <summary>Alle Geräte einer Kategorie, nach Name sortiert.</summary>
    Task<IReadOnlyList<Device>> FindByCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>Geräte, deren Name den Suchbegriff enthält, nach Name sortiert.</summary>
    Task<IReadOnlyList<Device>> FindByNameAsync(string searchTerm, CancellationToken ct = default);
}
