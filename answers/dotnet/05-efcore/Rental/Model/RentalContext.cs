using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Rental.Bookings;

namespace Rental.Model;

public sealed class RentalContext(DbContextOptions<RentalContext> options) : DbContext(options)
{
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();

    /// <summary>
    /// Provider-Einstellungen für UseNpgsql. Der Enum muss hier und auf der
    /// NpgsqlDataSource bekannt sein, weil die DataSource von außen kommt.
    /// </summary>
    public static void ConfigureProvider(NpgsqlDbContextOptionsBuilder provider)
    {
        provider.MapEnum<DeviceCondition>("device_condition", "rental");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("rental");

        modelBuilder.Entity<CustomerEntity>(k =>
        {
            k.ToTable("customer");
            k.Property(x => x.Id).HasColumnName("id");
            k.Property(x => x.Name).HasColumnName("name");
            k.Property(x => x.BonusPoints).HasColumnName("bonus_points");
            // xmin der Zeile als Versionsmerkmal: kein zusätzliches Feld in der Tabelle.
            k.Property<uint>("xmin").IsRowVersion();
            k.HasMany(x => x.Bookings).WithOne(b => b.Customer).HasForeignKey(b => b.CustomerId);
        });

        modelBuilder.Entity<DeviceEntity>(g =>
        {
            g.ToTable("device");
            g.Property(x => x.Id).HasColumnName("id");
            g.Property(x => x.Name).HasColumnName("name");
            g.Property(x => x.Category).HasColumnName("category");
        });

        modelBuilder.Entity<BookingEntity>(b =>
        {
            b.ToTable("booking");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.DeviceId).HasColumnName("device_id");
            b.Property(x => x.CustomerId).HasColumnName("customer_id");
            b.Property(x => x.TimeRange).HasColumnName("time_range").HasColumnType("tstzrange");
            b.Property(x => x.ConditionAtPickup).HasColumnName("condition_at_pickup");
            // Dasselbe Dokument liest auch Npgsql mit camelCase-Schlüsseln; die Namen müssen übereinstimmen.
            b.OwnsOne(x => x.Extra, z =>
            {
                z.ToJson("extra_info");
                z.Property(x => x.PickupLocation).HasJsonPropertyName("pickupLocation");
                z.Property(x => x.Notes).HasJsonPropertyName("notes");
            });
            b.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId);
        });
    }
}

public sealed class CustomerOverview(RentalContext ctx) : ICustomerOverview
{
    public async Task<IReadOnlyList<CustomerWithBookings>> LoadAsync(CancellationToken ct = default)
    {
        var customers = await ctx.Customers
            .AsNoTracking()
            .Include(k => k.Bookings)
            .OrderBy(k => k.Id)
            .ToListAsync(ct);
        return customers.Select(k => new CustomerWithBookings(k.Id, k.Name, k.BonusPoints, k.Bookings)).ToList();
    }

    public Task<int> CreditBonusAsync(int points, CancellationToken ct = default) =>
        ctx.Customers
            .Where(k => k.Bookings.Any())
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.BonusPoints, k => k.BonusPoints + points), ct);
}
