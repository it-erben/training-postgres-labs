namespace Verleih.Geraete;

public sealed record Geraet(int Id, string Name, string Kategorie);

public interface IGeraeteKatalog
{
    /// <summary>Alle Geräte einer Kategorie, nach Name sortiert.</summary>
    Task<IReadOnlyList<Geraet>> SucheNachKategorieAsync(string kategorie, CancellationToken ct = default);

    /// <summary>Geräte, deren Name den Suchbegriff enthält, nach Name sortiert.</summary>
    Task<IReadOnlyList<Geraet>> SucheNachNameAsync(string suchbegriff, CancellationToken ct = default);
}
