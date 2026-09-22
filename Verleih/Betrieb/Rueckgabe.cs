using Npgsql;

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

    public Task ZurueckgebenAsync(long buchungId, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 6: UPDATE und pg_notify in einer Transaktion.");
    }
}

/// <summary>
/// Hört auf einer dedizierten Verbindung auf Rückgaben. Die Verbindung wandert
/// nicht durch den Pool und wird nach einem Abbruch neu aufgebaut.
/// </summary>
public sealed class RueckgabeMelder(string verbindung)
{
    public const string AnwendungsName = "verleih_melder";

    /// <summary>Übung 6: eigener Application Name, Keepalive, kein Pool.</summary>
    public string Verbindungszeichenfolge { get; } = verbindung;

    /// <summary>Läuft, bis das Token abgebrochen wird. Jede Rückgabe ruft den Handler mit der Buchungsnummer.</summary>
    public Task LaufeAsync(Func<long, Task> beiRueckgabe, CancellationToken ct)
    {
        throw new NotImplementedException("Übung 6: LISTEN und WaitAsync auf einer dedizierten Verbindung.");
    }
}

public sealed record Verbindungsinfo(int Pid, string AnwendungsName, string Zustand, string? WartetAuf);

/// <summary>Sicht der Anwendung auf ihre eigenen Serververbindungen.</summary>
public sealed class Diagnose(NpgsqlDataSource quelle)
{
    public Task<IReadOnlyList<Verbindungsinfo>> EigeneVerbindungenAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 6: eigene Verbindungen aus pg_stat_activity.");
    }
}

/// <summary>Lesende Zugriffe bevorzugen ein Replikat, wenn eines erreichbar ist.</summary>
public static class Lesequelle
{
    public const string AnwendungsName = "verleih_lesend";

    public static NpgsqlDataSource Erzeuge(string verbindungMitAllenHosts)
    {
        throw new NotImplementedException("Übung 6: Target Session Attributes = prefer-standby.");
    }
}
