using Npgsql;

namespace Verleih.Geraete;

public sealed class GeraeteKatalog(NpgsqlDataSource quelle) : IGeraeteKatalog
{
    public Task<IReadOnlyList<Geraet>> SucheNachKategorieAsync(string kategorie, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 1: Suche nach Kategorie mit Parameter.");
    }

    public Task<IReadOnlyList<Geraet>> SucheNachNameAsync(string suchbegriff, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 1: Suche nach Namensbestandteil mit Parameter.");
    }
}
