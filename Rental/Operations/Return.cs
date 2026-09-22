using Npgsql;

namespace Rental.Operations;

public static class NotifyChannels
{
    public const string Return = "rental_return";
}

/// <summary>Meldet die Rückgabe eines Geräts und benachrichtigt Empfänger im selben COMMIT.</summary>
public sealed class ReturnService(NpgsqlDataSource dataSource)
{
    /// <summary>Nur für Tests: wird nach NOTIFY und vor COMMIT aufgerufen.</summary>
    public Func<Task>? BeforeCommit { get; set; }

    public Task ReturnAsync(long bookingId, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 6: UPDATE und pg_notify in einer Transaktion.");
    }
}

/// <summary>
/// Hört auf einer dedizierten Verbindung auf Rückgaben. Die Verbindung wandert
/// nicht durch den Pool und wird nach einem Abbruch neu aufgebaut.
/// </summary>
public sealed class ReturnListener(string connectionString)
{
    public const string AppName = "rental_listener";

    /// <summary>Übung 6: eigener Application Name, Keepalive, kein Pool.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <summary>Läuft, bis das Token abgebrochen wird. Jede Rückgabe ruft den Handler mit der Buchungsnummer.</summary>
    public Task RunAsync(Func<long, Task> onReturn, CancellationToken ct)
    {
        throw new NotImplementedException("Übung 6: LISTEN und WaitAsync auf einer dedizierten Verbindung.");
    }
}

public sealed record ConnectionInfo(int Pid, string AppName, string State, string? WaitEventType);

/// <summary>Sicht der Anwendung auf ihre eigenen Serververbindungen.</summary>
public sealed class ConnectionDiagnostics(NpgsqlDataSource dataSource)
{
    public Task<IReadOnlyList<ConnectionInfo>> OwnConnectionsAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 6: eigene Verbindungen aus pg_stat_activity.");
    }
}

/// <summary>Lesende Zugriffe bevorzugen ein Replikat, wenn eines erreichbar ist.</summary>
public static class ReadDataSource
{
    public const string AppName = "rental_reader";

    public static NpgsqlDataSource Create(string connectionWithAllHosts)
    {
        throw new NotImplementedException("Übung 6: Target Session Attributes = prefer-standby.");
    }
}
