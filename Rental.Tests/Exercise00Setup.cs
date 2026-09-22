using Npgsql;
using Rental.Tests.Infrastructure;

namespace Rental.Tests;

[Trait("Exercise", "00")]
public sealed class Exercise00Setup
{
    [Fact]
    public async Task Connection_reaches_own_database()
    {
        var expected = new NpgsqlConnectionStringBuilder(TestEnvironment.ConnectionString).Database;
        await using var dataSource = TestEnvironment.CreateAdminSource();
        await using var cmd = dataSource.CreateCommand("SELECT current_database()");
        Assert.Equal(expected, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Role_is_owner_without_superuser()
    {
        await using var dataSource = TestEnvironment.CreateAdminSource();
        await using var cmd = dataSource.CreateCommand("""
            SELECT pg_get_userbyid(d.datdba) = current_user, r.rolsuper
            FROM pg_database d, pg_roles r
            WHERE d.datname = current_database() AND r.rolname = current_user
            """);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0), "Die Rolle ist nicht Eigentümerin der Datenbank.");
        Assert.False(reader.GetBoolean(1), "Die Übungen sind für eine Rolle ohne Superuser gedacht.");
    }

    [Fact]
    public async Task Server_version_is_17_or_18()
    {
        await using var dataSource = TestEnvironment.CreateAdminSource();
        await using var cmd = dataSource.CreateCommand("SELECT current_setting('server_version_num')::int");
        var version = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.InRange(version, 170000, 189999);
    }

    [Fact]
    public async Task Access_goes_to_rw_service()
    {
        await using var dataSource = TestEnvironment.CreateAdminSource();
        await using var cmd = dataSource.CreateCommand("SELECT pg_is_in_recovery()");
        Assert.False((bool)(await cmd.ExecuteScalarAsync())!, "Die Verbindung zeigt auf ein Replikat; RENTAL_CONNECTION muss den rw-Dienst nennen.");
    }
}
