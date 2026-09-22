using Npgsql;

namespace Verleih.Buchungen;

public sealed class Buchungsablage(NpgsqlDataSource quelle) : IBuchungen
{
    public Task<long> AnlegenAsync(Buchung buchung, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 2: Buchung mit tstzrange, Enum und jsonb anlegen.");
    }

    /// <summary>Einfügen auf einer vorhandenen Verbindung; Übung 3 nutzt dieselbe Anweisung in ihrer Transaktion.</summary>
    public static Task<long> AnlegenAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Buchung buchung, CancellationToken ct)
    {
        throw new NotImplementedException("Übung 2: Einfügen auf vorhandener Verbindung.");
    }

    public Task<Buchung?> LadeAsync(long id, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 2: Buchung laden, Zeitpunkte als Utc.");
    }

    public Task<IReadOnlyList<Buchung>> SucheNachAbholortAsync(string abholort, CancellationToken ct = default)
    {
        throw new NotImplementedException("Übung 2: Suche über jsonb-Parameter mit @>.");
    }
}
