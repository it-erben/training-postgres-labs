using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Verleih.Buchungen;

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
        provider.MapEnum<Geraetezustand>("geraetezustand", "verleih");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("verleih");

        modelBuilder.Entity<KundeEntitaet>(k =>
        {
            k.ToTable("kunde");
            k.Property(x => x.Id).HasColumnName("id");
            k.Property(x => x.Name).HasColumnName("name");
            k.Property(x => x.Bonuspunkte).HasColumnName("bonuspunkte");
            // xmin der Zeile als Versionsmerkmal: kein zusätzliches Feld in der Tabelle.
            k.Property<uint>("xmin").IsRowVersion();
            k.HasMany(x => x.Buchungen).WithOne(b => b.Kunde).HasForeignKey(b => b.KundeId);
        });

        modelBuilder.Entity<GeraetEntitaet>(g =>
        {
            g.ToTable("geraet");
            g.Property(x => x.Id).HasColumnName("id");
            g.Property(x => x.Name).HasColumnName("name");
            g.Property(x => x.Kategorie).HasColumnName("kategorie");
        });

        modelBuilder.Entity<BuchungEntitaet>(b =>
        {
            b.ToTable("buchung");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.GeraetId).HasColumnName("geraet_id");
            b.Property(x => x.KundeId).HasColumnName("kunde_id");
            b.Property(x => x.Zeitraum).HasColumnName("zeitraum").HasColumnType("tstzrange");
            b.Property(x => x.ZustandBeiAbholung).HasColumnName("zustand_bei_abholung");
            // Dasselbe Dokument liest auch Npgsql mit camelCase-Schlüsseln; die Namen müssen übereinstimmen.
            b.OwnsOne(x => x.Zusatz, z =>
            {
                z.ToJson("zusatzinfo");
                z.Property(x => x.Abholort).HasJsonPropertyName("abholort");
                z.Property(x => x.Hinweise).HasJsonPropertyName("hinweise");
            });
            b.HasOne(x => x.Geraet).WithMany().HasForeignKey(x => x.GeraetId);
        });
    }
}

public sealed class Kundenuebersicht(VerleihContext ctx) : IKundenuebersicht
{
    public async Task<IReadOnlyList<KundeMitBuchungen>> LadeAsync(CancellationToken ct = default)
    {
        var kunden = await ctx.Kunden
            .AsNoTracking()
            .Include(k => k.Buchungen)
            .OrderBy(k => k.Id)
            .ToListAsync(ct);
        return kunden.Select(k => new KundeMitBuchungen(k.Id, k.Name, k.Bonuspunkte, k.Buchungen)).ToList();
    }

    public Task<int> BonusGutschriftAsync(int punkte, CancellationToken ct = default) =>
        ctx.Kunden
            .Where(k => k.Buchungen.Any())
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.Bonuspunkte, k => k.Bonuspunkte + punkte), ct);
}
