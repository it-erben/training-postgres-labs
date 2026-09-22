using Npgsql;
using Rental.Operations;
using Rental.Tests.Infrastructure;

namespace Rental.Tests;

[Trait("Exercise", "06")]
public sealed class Exercise06Operations(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static int nextDay;

    /// <summary>Jede Buchung liegt an einem eigenen Tag in der Vergangenheit, damit keine Überlappung entsteht.</summary>
    private async Task<long> CreateBookingAsync()
    {
        var customerId = await db.CreateCustomerAsync("Rueckgabe");
        var day = Interlocked.Increment(ref nextDay);
        return await db.ScalarAsync<long>("""
            INSERT INTO rental.booking (device_id, customer_id, time_range, condition_at_pickup)
            VALUES (1, $1, tstzrange(timestamptz '2026-01-01' + make_interval(days => $2), timestamptz '2026-01-01' + make_interval(days => $2 + 1), '[)'), 'used')
            RETURNING id
            """, customerId, day);
    }

    /// <summary>Eigener Empfänger der Tests, unabhängig vom Melder der Anwendung.</summary>
    private async Task<(NpgsqlConnection Connection, List<string> Received)> ListenerAsync()
    {
        var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.ConnectionString) { ApplicationName = "rental_test_listener", Pooling = false }.ConnectionString);
        await conn.OpenAsync();
        var received = new List<string>();
        conn.Notification += (_, e) => received.Add(e.Payload);
        await using var cmd = new NpgsqlCommand($"LISTEN {NotifyChannels.Return}", conn);
        await cmd.ExecuteNonQueryAsync();
        return (conn, received);
    }

    [Fact]
    public async Task Return_sends_notification_after_commit()
    {
        var id = await CreateBookingAsync();
        var (listener, received) = await ListenerAsync();
        await using (listener)
        {
            await new ReturnService(db.AppSource).ReturnAsync(id);
            Assert.True(await listener.WaitAsync(3000), "Innerhalb von 3 s kam keine Benachrichtigung an.");
            Assert.Equal([id.ToString()], received);
            Assert.NotNull(await db.ScalarAsync<object>("SELECT returned_at FROM rental.booking WHERE id = $1", id));
        }
    }

    [Fact]
    public async Task Notification_does_not_arrive_before_commit()
    {
        var id = await CreateBookingAsync();
        var (listener, received) = await ListenerAsync();
        await using (listener)
        {
            bool? beforeCommit = null;
            var service = new ReturnService(db.AppSource)
            {
                BeforeCommit = async () => beforeCommit = await listener.WaitAsync(500),
            };
            await service.ReturnAsync(id);
            Assert.False(beforeCommit, "Die Benachrichtigung kam vor dem COMMIT an; NOTIFY muss in derselben Transaktion wie das UPDATE laufen.");
            Assert.True(await listener.WaitAsync(3000));
            Assert.Equal([id.ToString()], received);
        }
    }

    [Fact]
    public async Task Rolled_back_return_sends_nothing()
    {
        var id = await CreateBookingAsync();
        var (listener, received) = await ListenerAsync();
        await using (listener)
        {
            var service = new ReturnService(db.AppSource) { BeforeCommit = () => throw new InvalidOperationException("Test bricht ab") };
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReturnAsync(id));
            Assert.False(await listener.WaitAsync(500));
            Assert.Empty(received);
            Assert.Null(await db.ScalarAsync<object>("SELECT returned_at FROM rental.booking WHERE id = $1", id) as DateTime?);
            Assert.Equal(0L, await db.CountConnectionsAsync(state: "idle in transaction"));
        }
    }

    [Fact]
    public async Task Listener_uses_a_dedicated_connection_with_keepalive()
    {
        var notifier = new ReturnListener(db.ConnectionString);
        var settings = new NpgsqlConnectionStringBuilder(notifier.ConnectionString);
        Assert.True(settings.KeepAlive > 0, "Keepalive ist nicht gesetzt.");
        Assert.Equal(ReturnListener.AppName, settings.ApplicationName);

        using var cts = new CancellationTokenSource();
        var received = new TaskCompletionSource<long>();
        var run = notifier.RunAsync(id => { received.TrySetResult(id); return Task.CompletedTask; }, cts.Token);
        Assert.True(await DatabaseFixture.WaitUntilAsync(async () => await db.CountConnectionsAsync(ReturnListener.AppName) == 1, TimeSpan.FromSeconds(5)),
            "Es gibt keine Serververbindung mit application_name = rental_listener.");

        var id = await CreateBookingAsync();
        await new ReturnService(db.AppSource).ReturnAsync(id);
        Assert.Equal(id, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task Diagnostics_lists_own_connections_with_state()
    {
        using var cts = new CancellationTokenSource();
        var run = new ReturnListener(db.ConnectionString).RunAsync(_ => Task.CompletedTask, cts.Token);
        await DatabaseFixture.WaitUntilAsync(async () => await db.CountConnectionsAsync(ReturnListener.AppName) == 1, TimeSpan.FromSeconds(5));

        var connections = await new ConnectionDiagnostics(db.AppSource).OwnConnectionsAsync();
        Assert.Contains(connections, v => v.AppName == ReturnListener.AppName && v.State == "idle");
        Assert.Contains(connections, v => v.AppName == "rental" && v.State == "active");
        Assert.DoesNotContain(connections, v => v.AppName == "rental_test");

        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task Read_queries_prefer_the_replica()
    {
        var connectionString = TestEnvironment.ReadOnlyConnectionString;
        Assert.SkipWhen(connectionString is null, $"{TestEnvironment.ReadOnlyConnectionVariable} ist nicht gesetzt; ohne Replikat wird dieser Test übersprungen.");
        await using var readSource = ReadDataSource.Create(connectionString!);
        await using var cmd = readSource.CreateCommand("SELECT pg_is_in_recovery()");
        Assert.True((bool)(await cmd.ExecuteScalarAsync())!, "Die Leseverbindung landet auf dem Primärserver; Target Session Attributes prüfen.");
    }

    [Fact]
    [Trait("Stretch", "true")]
    public async Task Listener_reconnects_after_connection_loss()
    {
        using var cts = new CancellationTokenSource();
        var received = new List<long>();
        var run = new ReturnListener(db.ConnectionString).RunAsync(id => { lock (received) received.Add(id); return Task.CompletedTask; }, cts.Token);
        await DatabaseFixture.WaitUntilAsync(async () => await db.CountConnectionsAsync(ReturnListener.AppName) == 1, TimeSpan.FromSeconds(5));

        var pid = await db.ScalarAsync<int>("SELECT pid FROM pg_stat_activity WHERE application_name = $1", ReturnListener.AppName);
        await db.ExecuteAsync("SELECT pg_terminate_backend($1)", pid);
        Assert.True(await DatabaseFixture.WaitUntilAsync(async () =>
            await db.ScalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE application_name = $1 AND pid <> $2", ReturnListener.AppName, pid) == 1,
            TimeSpan.FromSeconds(10)), "Der Melder hat nach dem Abbruch keine neue Verbindung aufgebaut.");

        var id = await CreateBookingAsync();
        await new ReturnService(db.AppSource).ReturnAsync(id);
        Assert.True(await DatabaseFixture.WaitUntilAsync(() => Task.FromResult(received.Contains(id)), TimeSpan.FromSeconds(5)),
            "Nach dem Neuaufbau kam die Rückgabe nicht an.");

        cts.Cancel();
        await run;
    }
}
