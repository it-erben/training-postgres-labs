using System.Diagnostics;
using System.Text;
using Rental.Import;
using Rental.Tests.Infrastructure;

namespace Rental.Tests;

[Trait("Exercise", "04")]
public sealed class Exercise04BulkData(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private const int RowCount = 200_000;

    private IStockImport Import => new StockImport(db.AppSource);

    private static MemoryStream Csv(int badLine = 0)
    {
        var sb = new StringBuilder(RowCount * 48);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= RowCount; i++)
        {
            var quantity = i == badLine ? -1 : 1 + i % 5;
            sb.Append(1 + i % 6).Append(';')
              .Append(start.AddMinutes(i).ToString("O")).Append(';')
              .Append(quantity).Append(';')
              .Append(i % 100 == 0 ? "stocktake" : "").Append('\n');
        }
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private Task TruncateAsync() => db.ExecuteAsync("TRUNCATE rental.stock_movement");

    [Fact]
    public async Task Import_writes_200000_rows()
    {
        await TruncateAsync();
        var count = await Import.ImportAsync(Csv());
        Assert.Equal(RowCount, count);
        Assert.Equal((long)RowCount, await db.ScalarAsync<long>("SELECT count(*) FROM rental.stock_movement"));
        Assert.Equal(2000L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.stock_movement WHERE remark = 'stocktake'"));
        Assert.Equal(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc),
            await db.ScalarAsync<DateTime>("SELECT min(moved_at) FROM rental.stock_movement"));
    }

    [Fact]
    public async Task Import_takes_at_most_five_seconds()
    {
        await TruncateAsync();
        var stopwatch = Stopwatch.StartNew();
        await Import.ImportAsync(Csv());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Der Import hat {stopwatch.Elapsed.TotalSeconds:F1} s gebraucht. Einzel-INSERTs erreichen das Budget nicht; COPY unterschreitet es deutlich.");
    }

    [Fact(Timeout = 30_000)]
    public async Task Import_uses_COPY_and_streams_the_input()
    {
        await TruncateAsync();
        // Der Stream gibt nach 64 KB nichts mehr her, bis der Server ein laufendes COPY der Anwendung zeigt.
        var release = new TaskCompletionSource();
        var stream = new BlockingStream(Csv(), 64 * 1024, release.Task);
        var import = Import.ImportAsync(stream, TestContext.Current.CancellationToken);

        var seen = await DatabaseFixture.WaitUntilAsync(async () =>
            await db.ScalarAsync<long>(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = 'rental' AND query LIKE 'COPY%'") > 0,
            TimeSpan.FromSeconds(15));
        release.TrySetResult();
        Assert.True(seen, "Innerhalb von 15 s lief auf dem Server kein COPY der Anwendung. Der Import muss COPY verwenden und die CSV während der Übertragung lesen.");
        Assert.Equal(RowCount, await import);
    }

    [Fact]
    public async Task Invalid_line_discards_the_whole_import()
    {
        await TruncateAsync();
        var error = await Assert.ThrowsAsync<ImportException>(() => Import.ImportAsync(Csv(badLine: 150_000)));
        Assert.Equal(150_000, error.Line);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.stock_movement"));
    }

    [Fact]
    public async Task Import_leaves_no_connection_open()
    {
        await TruncateAsync();
        await Import.ImportAsync(Csv());
        await Assert.ThrowsAsync<ImportException>(() => Import.ImportAsync(Csv(badLine: 7)));
        Assert.Equal(0L, await db.CountConnectionsAsync(state: "active"));
        Assert.Equal(0L, await db.CountConnectionsAsync(state: "idle in transaction"));
        Assert.Equal(0L, await db.CountConnectionsAsync(state: "idle in transaction (aborted)"));
    }

    [Fact]
    [Trait("Stretch", "true")]
    public async Task Upsert_via_unnest_updates_existing_stock()
    {
        await db.ExecuteAsync("TRUNCATE rental.stock");
        await Import.UpdateStockAsync([(1, 10), (2, 20), (3, 30)]);
        await Import.UpdateStockAsync([(2, 25), (4, 40)]);
        Assert.Equal(4L, await db.ScalarAsync<long>("SELECT count(*) FROM rental.stock"));
        Assert.Equal(25, await db.ScalarAsync<int>("SELECT quantity FROM rental.stock WHERE device_id = 2"));
        Assert.Equal(10, await db.ScalarAsync<int>("SELECT quantity FROM rental.stock WHERE device_id = 1"));
    }

    /// <summary>Liefert die ersten Bytes sofort und blockiert danach, bis die Freigabe eintrifft.</summary>
    private sealed class BlockingStream(Stream inner, long freeBytes, Task release) : Stream
    {
        private long bytesRead;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (bytesRead >= freeBytes)
            {
                await release.WaitAsync(ct);
            }
            var n = await inner.ReadAsync(buffer, ct);
            bytesRead += n;
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
