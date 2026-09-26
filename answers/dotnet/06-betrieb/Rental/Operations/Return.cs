using Npgsql;
using Rental.Database;

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

    public async Task ReturnAsync(long bookingId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            "UPDATE rental.booking SET returned_at = now() WHERE id = $1 AND returned_at IS NULL", conn, tx))
        {
            cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = bookingId });
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            {
                throw new InvalidOperationException($"Buchung {bookingId} ist unbekannt oder bereits zurückgegeben.");
            }
        }

        await using (var cmd = new NpgsqlCommand("SELECT pg_notify($1, $2)", conn, tx))
        {
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = NotifyChannels.Return });
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = bookingId.ToString() });
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (BeforeCommit is not null)
        {
            await BeforeCommit();
        }
        await tx.CommitAsync(ct);
    }
}

/// <summary>
/// Hört auf einer dedizierten Verbindung auf Rückgaben. Die Verbindung wandert
/// nicht durch den Pool und wird nach einem Abbruch neu aufgebaut.
/// </summary>
public sealed class ReturnListener(string connectionString)
{
    public const string AppName = "rental_listener";

    public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(connectionString)
    {
        ApplicationName = AppName,
        KeepAlive = 10,
        Pooling = false,
    }.ConnectionString;

    /// <summary>Läuft, bis das Token abgebrochen wird. Jede Rückgabe ruft den Handler mit der Buchungsnummer.</summary>
    public async Task RunAsync(Func<long, Task> onReturn, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConnectionString);
                await conn.OpenAsync(ct);
                conn.Notification += (_, e) =>
                {
                    if (long.TryParse(e.Payload, out var id))
                    {
                        _ = onReturn(id);
                    }
                };
                await using (var cmd = new NpgsqlCommand($"LISTEN {NotifyChannels.Return}", conn))
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                while (!ct.IsCancellationRequested)
                {
                    await conn.WaitAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException)
            {
                // Verbindung verloren: kurz warten und erneut LISTEN ausführen.
                await Task.Delay(200, ct);
            }
        }
    }
}

public sealed record ConnectionInfo(int Pid, string AppName, string State, string? WaitEventType);

/// <summary>Sicht der Anwendung auf ihre eigenen Serververbindungen.</summary>
public sealed class ConnectionDiagnostics(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<ConnectionInfo>> OwnConnectionsAsync(CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT pid, application_name, state, wait_event_type
            FROM pg_stat_activity
            WHERE datname = current_database() AND application_name = ANY($1)
            ORDER BY application_name, pid
            """);
        cmd.Parameters.Add(new NpgsqlParameter { Value = new[] { RentalDataSource.AppName, ReturnListener.AppName, ReadDataSource.AppName } });
        var result = new List<ConnectionInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ConnectionInfo(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return result;
    }
}

/// <summary>Lesende Zugriffe bevorzugen ein Replikat, wenn eines erreichbar ist.</summary>
public static class ReadDataSource
{
    public const string AppName = "rental_reader";

    public static NpgsqlDataSource Create(string connectionWithAllHosts)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionWithAllHosts)
        {
            ApplicationName = AppName,
            TargetSessionAttributes = "prefer-standby",
        };
        return NpgsqlDataSource.Create(settings.ConnectionString);
    }
}
