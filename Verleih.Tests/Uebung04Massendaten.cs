using System.Diagnostics;
using System.Text;
using Verleih.Import;
using Verleih.Tests.Infrastruktur;

namespace Verleih.Tests;

[Trait("Uebung", "04")]
public sealed class Uebung04Massendaten(DatenbankFixture db) : IClassFixture<DatenbankFixture>
{
    private const int Zeilen = 200_000;

    private IBestandsimport Import => new Bestandsimport(db.Anwendung);

    private static MemoryStream Csv(int fehlerhafteZeile = 0)
    {
        var sb = new StringBuilder(Zeilen * 48);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= Zeilen; i++)
        {
            var menge = i == fehlerhafteZeile ? -1 : 1 + i % 5;
            sb.Append(1 + i % 6).Append(';')
              .Append(start.AddMinutes(i).ToString("O")).Append(';')
              .Append(menge).Append(';')
              .Append(i % 100 == 0 ? "Inventur" : "").Append('\n');
        }
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private Task LeerenAsync() => db.AusfuehrenAsync("TRUNCATE verleih.bestandsbewegung");

    [Fact]
    public async Task Import_schreibt_200000_Zeilen()
    {
        await LeerenAsync();
        var anzahl = await Import.ImportiereAsync(Csv());
        Assert.Equal(Zeilen, anzahl);
        Assert.Equal((long)Zeilen, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.bestandsbewegung"));
        Assert.Equal(2000L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.bestandsbewegung WHERE bemerkung = 'Inventur'"));
        Assert.Equal(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc),
            await db.SkalarAsync<DateTime>("SELECT min(zeitpunkt) FROM verleih.bestandsbewegung"));
    }

    [Fact]
    public async Task Import_braucht_hoechstens_fuenf_Sekunden()
    {
        await LeerenAsync();
        var uhr = Stopwatch.StartNew();
        await Import.ImportiereAsync(Csv());
        Assert.True(uhr.Elapsed < TimeSpan.FromSeconds(5),
            $"Der Import hat {uhr.Elapsed.TotalSeconds:F1} s gebraucht. Einzel-INSERTs erreichen das Budget nicht; COPY unterschreitet es deutlich.");
    }

    [Fact(Timeout = 30_000)]
    public async Task Import_nutzt_COPY_und_streamt_die_Eingabe()
    {
        await LeerenAsync();
        // Der Stream gibt nach 64 KB nichts mehr her, bis der Server ein laufendes COPY der Anwendung zeigt.
        var freigabe = new TaskCompletionSource();
        var strom = new BlockierenderStream(Csv(), 64 * 1024, freigabe.Task);
        var import = Import.ImportiereAsync(strom, TestContext.Current.CancellationToken);

        var gesehen = await DatenbankFixture.WarteBisAsync(async () =>
            await db.SkalarAsync<long>(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = 'verleih' AND query LIKE 'COPY%'") > 0,
            TimeSpan.FromSeconds(15));
        freigabe.TrySetResult();
        Assert.True(gesehen, "Innerhalb von 15 s lief auf dem Server kein COPY der Anwendung. Der Import muss COPY verwenden und die CSV während der Übertragung lesen.");
        Assert.Equal(Zeilen, await import);
    }

    [Fact]
    public async Task Fehlerhafte_Zeile_verwirft_den_gesamten_Import()
    {
        await LeerenAsync();
        var fehler = await Assert.ThrowsAsync<ImportFehler>(() => Import.ImportiereAsync(Csv(fehlerhafteZeile: 150_000)));
        Assert.Equal(150_000, fehler.Zeile);
        Assert.Equal(0L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.bestandsbewegung"));
    }

    [Fact]
    public async Task Import_laesst_keine_Verbindung_offen()
    {
        await LeerenAsync();
        await Import.ImportiereAsync(Csv());
        await Assert.ThrowsAsync<ImportFehler>(() => Import.ImportiereAsync(Csv(fehlerhafteZeile: 7)));
        Assert.Equal(0L, await db.VerbindungenAsync(zustand: "active"));
        Assert.Equal(0L, await db.VerbindungenAsync(zustand: "idle in transaction"));
        Assert.Equal(0L, await db.VerbindungenAsync(zustand: "idle in transaction (aborted)"));
    }

    [Fact]
    [Trait("Stretch", "ja")]
    public async Task Upsert_per_unnest_aktualisiert_bestehende_Bestaende()
    {
        await db.AusfuehrenAsync("TRUNCATE verleih.bestand");
        await Import.AktualisiereBestandAsync([(1, 10), (2, 20), (3, 30)]);
        await Import.AktualisiereBestandAsync([(2, 25), (4, 40)]);
        Assert.Equal(4L, await db.SkalarAsync<long>("SELECT count(*) FROM verleih.bestand"));
        Assert.Equal(25, await db.SkalarAsync<int>("SELECT menge FROM verleih.bestand WHERE geraet_id = 2"));
        Assert.Equal(10, await db.SkalarAsync<int>("SELECT menge FROM verleih.bestand WHERE geraet_id = 1"));
    }

    /// <summary>Liefert die ersten Bytes sofort und blockiert danach, bis die Freigabe eintrifft.</summary>
    private sealed class BlockierenderStream(Stream innen, long freieBytes, Task freigabe) : Stream
    {
        private long gelesen;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (gelesen >= freieBytes)
            {
                await freigabe.WaitAsync(ct);
            }
            var n = await innen.ReadAsync(buffer, ct);
            gelesen += n;
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => innen.Length;
        public override long Position { get => innen.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
