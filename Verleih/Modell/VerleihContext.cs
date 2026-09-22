using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Verleih.Modell;

public sealed class VerleihContext(DbContextOptions<VerleihContext> options) : DbContext(options)
{
    public DbSet<KundeEntitaet> Kunden => Set<KundeEntitaet>();
    public DbSet<GeraetEntitaet> Geraete => Set<GeraetEntitaet>();
    public DbSet<BuchungEntitaet> Buchungen => Set<BuchungEntitaet>();

    /// <summary>
    /// Provider-Einstellungen für UseNpgsql. Der Enum muss hier und auf der
    /// NpgsqlDataSource bekannt sein, weil die DataSource von außen kommt.
    /// </summary>
    public static void Konfiguriere(NpgsqlDbContextOptionsBuilder provider)
    {
        // Übung 5: Enum-Zuordnung für den Provider.
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Übung 5: Tabellen, Spalten, JSON-Dokument und xmin abbilden.
    }
}

public sealed class Kundenuebersicht(VerleihContext ctx) : IKundenuebersicht
{
    public Task<IReadOnlyList<KundeMitBuchungen>> LadeAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 5: Kunden mit Buchungen ohne Tracking in höchstens zwei Anweisungen.");
    }

    public Task<int> BonusGutschriftAsync(int punkte, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 5: ExecuteUpdate ohne Laden.");
    }
}
