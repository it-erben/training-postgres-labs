using Npgsql;
using Verleih.Datenbank;

namespace Verleih.Betrieb;

public static class Kanaele
{
    public const string Rueckgabe = "verleih_rueckgabe";
}

/// <summary>Meldet die Rückgabe eines Geräts und benachrichtigt Empfänger im selben COMMIT.</summary>
public sealed class RueckgabeDienst(NpgsqlDataSource quelle)
{
    /// <summary>Nur für Tests: wird nach NOTIFY und vor COMMIT aufgerufen.</summary>
    public Func<Task>? VorCommit { get; set; }

    public async Task ZurueckgebenAsync(long buchungId, CancellationToken ct = default)
    {
        await using var conn = await quelle.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            "UPDATE verleih.buchung SET zurueckgegeben_am = now() WHERE id = $1 AND zurueckgegeben_am IS NULL", conn, tx))
        {
            cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = buchungId });
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            {
                throw new InvalidOperationException($"Buchung {buchungId} ist unbekannt oder bereits zurückgegeben.");
            }
        }

        await using (var cmd = new NpgsqlCommand("SELECT pg_notify($1, $2)", conn, tx))
        {
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = Kanaele.Rueckgabe });
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = buchungId.ToString() });
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (VorCommit is not null)
        {
            await VorCommit();
        }
        await tx.CommitAsync(ct);
    }
}

/// <summary>
/// Hört auf einer dedizierten Verbindung auf Rückgaben. Die Verbindung wandert
/// nicht durch den Pool und wird nach einem Abbruch neu aufgebaut.
/// </summary>
public sealed class RueckgabeMelder(string verbindung)
{
    public const string AnwendungsName = "verleih_melder";

    public string Verbindungszeichenfolge { get; } = new NpgsqlConnectionStringBuilder(verbindung)
    {
        ApplicationName = AnwendungsName,
        KeepAlive = 10,
        Pooling = false,
    }.ConnectionString;

    /// <summary>Läuft, bis das Token abgebrochen wird. Jede Rückgabe ruft den Handler mit der Buchungsnummer.</summary>
    public async Task LaufeAsync(Func<long, Task> beiRueckgabe, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(Verbindungszeichenfolge);
                await conn.OpenAsync(ct);
                conn.Notification += (_, e) =>
                {
                    if (long.TryParse(e.Payload, out var id))
                    {
                        _ = beiRueckgabe(id);
                    }
                };
                await using (var cmd = new NpgsqlCommand($"LISTEN {Kanaele.Rueckgabe}", conn))
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

public sealed record Verbindungsinfo(int Pid, string AnwendungsName, string Zustand, string? WartetAuf);

/// <summary>Sicht der Anwendung auf ihre eigenen Serververbindungen.</summary>
public sealed class Diagnose(NpgsqlDataSource quelle)
{
    public async Task<IReadOnlyList<Verbindungsinfo>> EigeneVerbindungenAsync(CancellationToken ct = default)
    {
        await using var cmd = quelle.CreateCommand("""
            SELECT pid, application_name, state, wait_event_type
            FROM pg_stat_activity
            WHERE datname = current_database() AND application_name = ANY($1)
            ORDER BY application_name, pid
            """);
        cmd.Parameters.Add(new NpgsqlParameter { Value = new[] { Datenquelle.AnwendungsName, RueckgabeMelder.AnwendungsName, Lesequelle.AnwendungsName } });
        var ergebnis = new List<Verbindungsinfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ergebnis.Add(new Verbindungsinfo(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return ergebnis;
    }
}

/// <summary>Lesende Zugriffe bevorzugen ein Replikat, wenn eines erreichbar ist.</summary>
public static class Lesequelle
{
    public const string AnwendungsName = "verleih_lesend";

    public static NpgsqlDataSource Erzeuge(string verbindungMitAllenHosts)
    {
        var einstellungen = new NpgsqlConnectionStringBuilder(verbindungMitAllenHosts)
        {
            ApplicationName = AnwendungsName,
            TargetSessionAttributes = "prefer-standby",
        };
        return NpgsqlDataSource.Create(einstellungen.ConnectionString);
    }
}
