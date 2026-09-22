using Npgsql;
using NpgsqlTypes;

namespace Verleih.Buchungen;

public sealed class Buchungsablage(NpgsqlDataSource quelle) : IBuchungen
{
    public const string EinfuegenSql = """
        INSERT INTO verleih.buchung (geraet_id, kunde_id, zeitraum, zustand_bei_abholung, zusatzinfo)
        VALUES ($1, $2, tstzrange($3, $4, '[)'), $5, $6)
        RETURNING id
        """;

    private const string LesenSql = """
        SELECT id, geraet_id, kunde_id, lower(zeitraum), upper(zeitraum), zustand_bei_abholung, zusatzinfo
        FROM verleih.buchung
        """;

    public async Task<long> AnlegenAsync(Buchung buchung, CancellationToken ct = default)
    {
        await using var conn = await quelle.OpenConnectionAsync(ct);
        return await AnlegenAsync(conn, null, buchung, ct);
    }

    /// <summary>Einfügen auf einer vorhandenen Verbindung, damit Übung 3 dieselbe Anweisung in ihrer Transaktion nutzt.</summary>
    public static async Task<long> AnlegenAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Buchung buchung, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(EinfuegenSql, conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter<int> { TypedValue = buchung.GeraetId });
        cmd.Parameters.Add(new NpgsqlParameter<int> { TypedValue = buchung.KundeId });
        // Expliziter Typ: ein DateTime mit Kind = Local wird von Npgsql abgewiesen,
        // statt vom Server in der Sitzungszeitzone umgedeutet zu werden.
        cmd.Parameters.Add(new NpgsqlParameter { Value = buchung.Von, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)buchung.Bis ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        cmd.Parameters.Add(new NpgsqlParameter { Value = buchung.ZustandBeiAbholung });
        cmd.Parameters.Add(new NpgsqlParameter { Value = buchung.Zusatz, NpgsqlDbType = NpgsqlDbType.Jsonb });
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<Buchung?> LadeAsync(long id, CancellationToken ct = default)
    {
        await using var cmd = quelle.CreateCommand(LesenSql + " WHERE id = $1");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        var treffer = await LeseAsync(cmd, ct);
        return treffer.Count == 0 ? null : treffer[0];
    }

    public async Task<IReadOnlyList<Buchung>> SucheNachAbholortAsync(string abholort, CancellationToken ct = default)
    {
        await using var cmd = quelle.CreateCommand(LesenSql + " WHERE zusatzinfo @> $1 ORDER BY id");
        cmd.Parameters.Add(new NpgsqlParameter { Value = new { abholort }, NpgsqlDbType = NpgsqlDbType.Jsonb });
        return await LeseAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<Buchung>> LeseAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var ergebnis = new List<Buchung>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ergebnis.Add(new Buchung(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.GetFieldValue<Geraetezustand>(5),
                reader.GetFieldValue<Zusatzinfo>(6)));
        }
        return ergebnis;
    }
}
