using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RdtClient.Service.Services.Usenet.Concurrency;

namespace RdtClient.Service.Services.Usenet.Connections;

/// <summary>
/// Thread-safe, lazy connection pool.
/// </summary>
public sealed class ConnectionPool<T> : IDisposable, IAsyncDisposable
{
    /* -------------------------------- configuration -------------------------------- */

    public TimeSpan IdleTimeout { get; }
    public Int32 LiveConnections => _live;
    public Int32 IdleConnections => _idleConnections.Count;
    public Int32 ActiveConnections => _live - _idleConnections.Count;
    public Int32 AvailableConnections => _maxConnections - ActiveConnections;

    public event EventHandler<ConnectionPoolChangedEventArgs>? OnConnectionPoolChanged;

    private readonly Func<CancellationToken, ValueTask<T>> _factory;
    private readonly Int32 _maxConnections;

    /* --------------------------------- state --------------------------------------- */

    private readonly ConcurrentStack<Pooled> _idleConnections = new();
    private readonly PrioritizedSemaphore _gate;
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweeperTask; // keeps timer alive

    private Int32 _live; // number of connections currently alive
    private Int32 _disposed; // 0 == false, 1 == true

    /* ------------------------------------------------------------------------------ */

    public ConnectionPool(
        Int32 maxConnections,
        Func<CancellationToken, ValueTask<T>> connectionFactory,
        TimeSpan? idleTimeout = null)
    {
        if (maxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConnections));

        _factory = connectionFactory
                   ?? throw new ArgumentNullException(nameof(connectionFactory));
        IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);

        _maxConnections = maxConnections;
        _gate = new PrioritizedSemaphore(maxConnections, maxConnections);
        _sweeperTask = Task.Run(SweepLoop); // background idle-reaper
    }

    /* ============================== public API ==================================== */

    public async Task<ConnectionLock<T>> GetConnectionLockAsync
    (
        SemaphorePriority priority,
        CancellationToken cancellationToken = default
    )
    {
        // Make caller cancellation also cancel the wait on the gate.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sweepCts.Token);

        await _gate.WaitAsync(priority, linked.Token).ConfigureAwait(false);

        // Pool might have been disposed after wait returned:
        if (Volatile.Read(ref _disposed) == 1)
        {
            _gate.Release();
            ThrowDisposed();
        }

        // Try to reuse an existing idle connection.
        while (_idleConnections.TryPop(out var item))
        {
            if (!item.IsExpired(IdleTimeout))
            {
                TriggerConnectionPoolChangedEvent();
                return BuildLock(item.Connection);
            }

            // Stale – destroy and continue looking.
            DisposeConnection(item.Connection);
            Interlocked.Decrement(ref _live);
            TriggerConnectionPoolChangedEvent();
        }

        // Need a fresh connection.
        T conn;
        try
        {
            conn = await _factory(linked.Token).ConfigureAwait(false);
        }
        catch
        {
            _gate.Release(); // free the permit on failure
            throw;
        }

        Interlocked.Increment(ref _live);
        TriggerConnectionPoolChangedEvent();
        return BuildLock(conn);

        ConnectionLock<T> BuildLock(T c)
            => new(c, Return, Destroy);

        static void ThrowDisposed()
            => throw new ObjectDisposedException(nameof(ConnectionPool<T>));
    }

    /* ========================== core helpers ====================================== */

    private readonly record struct Pooled(T Connection, Int64 LastTouchedMillis)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Boolean IsExpired(TimeSpan idle, Int64 nowMillis = 0)
        {
            if (nowMillis == 0) nowMillis = Environment.TickCount64;
            return unchecked(nowMillis - LastTouchedMillis) >= idle.TotalMilliseconds;
        }
    }

    private void Return(T connection)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            DisposeConnection(connection);
            Interlocked.Decrement(ref _live);
            TriggerConnectionPoolChangedEvent();
            return;
        }

        _idleConnections.Push(new Pooled(connection, Environment.TickCount64));
        _gate.Release();
        TriggerConnectionPoolChangedEvent();
    }

    private void Destroy(T connection)
    {
        // When a lock requests replacement, we dispose the connection instead of reusing.
        DisposeConnection(connection);
        Interlocked.Decrement(ref _live);
        if (Volatile.Read(ref _disposed) == 0)
        {
            _gate.Release();
        }

        TriggerConnectionPoolChangedEvent();
    }

    private void TriggerConnectionPoolChangedEvent()
    {
        OnConnectionPoolChanged?.Invoke(this, new ConnectionPoolChangedEventArgs(
            _live,
            _idleConnections.Count,
            _maxConnections
        ));
    }

    /* =================== idle sweeper (background) ================================= */

    private async Task SweepLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(IdleTimeout / 2);
            while (await timer.WaitForNextTickAsync(_sweepCts.Token).ConfigureAwait(false))
                SweepOnce();
        }
        catch (OperationCanceledException)
        {
            /* normal on disposal */
        }
    }

    private void SweepOnce()
    {
        var now = Environment.TickCount64;
        var survivors = new List<Pooled>();
        var isAnyConnectionFreed = false;

        while (_idleConnections.TryPop(out var item))
        {
            if (item.IsExpired(IdleTimeout, now))
            {
                DisposeConnection(item.Connection);
                Interlocked.Decrement(ref _live);
                isAnyConnectionFreed = true;
            }
            else
            {
                survivors.Add(item);
            }
        }

        // Preserve original LIFO order.
        for (var i = survivors.Count - 1; i >= 0; i--)
            _idleConnections.Push(survivors[i]);

        if (isAnyConnectionFreed)
            TriggerConnectionPoolChangedEvent();
    }

    /* ------------------------- dispose helpers ------------------------------------ */

    private static void DisposeConnection(T conn)
    {
        if (conn is IDisposable d)
            d.Dispose();
    }

    /* -------------------------- IAsyncDisposable ---------------------------------- */

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await _sweepCts.CancelAsync();

        try
        {
            await _sweeperTask.ConfigureAwait(false); // await clean sweep exit
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }

        // Drain and dispose cached items.
        while (_idleConnections.TryPop(out var item))
            DisposeConnection(item.Connection);

        _sweepCts.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /* ----------------------------- IDisposable ------------------------------------ */

    public void Dispose()
    {
        _ = DisposeAsync().AsTask(); // fire-and-forget synchronous path
    }
}

public sealed class ConnectionPoolChangedEventArgs(Int32 live, Int32 idle, Int32 max) : EventArgs
{
    public Int32 Live { get; } = live;
    public Int32 Idle { get; } = idle;
    public Int32 Max { get; } = max;
    public Int32 Active => Live - Idle;
}
