using Npgsql;
using Verleih.Tests.Infrastruktur;

namespace Verleih.Tests;

[Trait("Uebung", "00")]
public sealed class Uebung00Einrichtung
{
    [Fact]
    public async Task Verbindung_erreicht_eigene_Datenbank()
    {
        var erwartet = new NpgsqlConnectionStringBuilder(Umgebung.Verbindung).Database;
        await using var quelle = Umgebung.Diagnose();
        await using var cmd = quelle.CreateCommand("SELECT current_database()");
        Assert.Equal(erwartet, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rolle_ist_Eigentuemerin_ohne_Superuser()
    {
        await using var quelle = Umgebung.Diagnose();
        await using var cmd = quelle.CreateCommand("""
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
    public async Task Serverversion_ist_17_oder_18()
    {
        await using var quelle = Umgebung.Diagnose();
        await using var cmd = quelle.CreateCommand("SELECT current_setting('server_version_num')::int");
        var version = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.InRange(version, 170000, 189999);
    }

    [Fact]
    public async Task Zugriff_geht_auf_den_rw_Dienst()
    {
        await using var quelle = Umgebung.Diagnose();
        await using var cmd = quelle.CreateCommand("SELECT pg_is_in_recovery()");
        Assert.False((bool)(await cmd.ExecuteScalarAsync())!, "Die Verbindung zeigt auf ein Replikat; VERLEIH_CONNECTION muss den rw-Dienst nennen.");
    }
}
