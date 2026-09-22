using System.Data;
using Npgsql;

namespace Verleih.Buchungen;

/// <summary>
/// Bucht ein Gerät für einen Kunden. Die Regel "höchstens drei Buchungen je
/// Kunde" wird innerhalb einer serialisierbaren Transaktion geprüft; ein
/// Serialisierungskonflikt wiederholt die gesamte Transaktion.
/// </summary>
public sealed class Buchungsdienst(NpgsqlDataSource quelle)
{
    public const int MaxBuchungenJeKunde = 3;

    public int MaxVersuche { get; set; } = 3;

    /// <summary>Nur für Tests: wird innerhalb der Transaktion vor der fachlichen Prüfung aufgerufen.</summary>
    public Func<NpgsqlConnection, CancellationToken, Task>? VorPruefung { get; set; }

    /// <summary>Bonus: serialisiert Buchungen desselben Kunden über einen Advisory Lock.</summary>
    public bool MitAdvisoryLock { get; set; }

    public async Task<long> BucheAsync(Buchung buchung, CancellationToken ct = default)
    {
        for (var versuch = 1; ; versuch++)
        {
            try
            {
                return await VersucheAsync(buchung, ct);
            }
            catch (PostgresException e) when (IstWiederholbar(e) && versuch < MaxVersuche)
            {
                // Die gesamte Transaktion einschließlich der fachlichen Prüfung wird erneut ausgeführt.
            }
            catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.ExclusionViolation
                                                or PostgresErrorCodes.UniqueViolation
                                                or PostgresErrorCodes.CheckViolation
                                                or PostgresErrorCodes.ForeignKeyViolation)
            {
                throw new BuchungsKonflikt(e.ConstraintName ?? e.SqlState, e);
            }
        }
    }

    public static bool IstWiederholbar(PostgresException e) =>
        e.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

    private async Task<long> VersucheAsync(Buchung buchung, CancellationToken ct)
    {
        await using var conn = await quelle.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (MitAdvisoryLock)
        {
            await using var sperre = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx);
            sperre.Parameters.Add(new NpgsqlParameter<long> { TypedValue = buchung.KundeId });
            await sperre.ExecuteNonQueryAsync(ct);
        }

        if (VorPruefung is not null)
        {
            await VorPruefung(conn, ct);
        }

        await using (var zaehl = new NpgsqlCommand("SELECT count(*) FROM verleih.buchung WHERE kunde_id = $1", conn, tx))
        {
            zaehl.Parameters.Add(new NpgsqlParameter<int> { TypedValue = buchung.KundeId });
            var vorhandene = (long)(await zaehl.ExecuteScalarAsync(ct))!;
            if (vorhandene >= MaxBuchungenJeKunde)
            {
                await tx.RollbackAsync(ct);
                throw new BuchungAbgelehnt($"Kunde {buchung.KundeId} hat bereits {vorhandene} Buchungen.");
            }
        }

        var id = await Buchungsablage.AnlegenAsync(conn, tx, buchung, ct);
        await tx.CommitAsync(ct);
        return id;
    }
}
