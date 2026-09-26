using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

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
        // Übung 5: Enum-Zuordnung für den Provider.
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Übung 5: Tabellen, Spalten, JSON-Dokument und xmin abbilden.
    }
}

public sealed class CustomerOverview(RentalContext ctx) : ICustomerOverview
{
    public Task<IReadOnlyList<CustomerWithBookings>> LoadAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 5: Kunden mit Buchungen ohne Tracking in höchstens zwei Anweisungen.");
    }

    public Task<int> CreditBonusAsync(int points, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 5: ExecuteUpdate ohne Laden.");
    }
}
