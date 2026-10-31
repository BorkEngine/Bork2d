using System.Collections.Concurrent;
using Bork2d.Tests.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Service;

public sealed class AssetServiceThreadingTests
{
    [Fact]
    public async Task AllRequestsRunOnTheSingleLoaderThread()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddRaw("data/a", [1]);
                builder.AddRaw("data/b", [2]);
                builder.AddRaw("data/c", [3]);
            });

        using var host = new AssetServiceHost(packPath);
        var threads = new ConcurrentQueue<int>();
        host.Service.RequestProcessingHook = _ => threads.Enqueue(Environment.CurrentManagedThreadId);
        host.Start();

        Thread loader = Assert.IsType<Thread>(host.Service.LoaderThread);

        await Task.WhenAll(
            host.Service.LoadData("data/a"),
            host.Service.LoadData("data/b"),
            host.Service.LoadData("data/c"));

        Assert.Equal(3, threads.Count);
        Assert.All(threads, threadId => Assert.Equal(loader.ManagedThreadId, threadId));
    }

    [Fact]
    public async Task RequestsAreProcessedInFifoOrder()
    {
        const int requestCount = 16;
        string[] paths = Enumerable.Range(0, requestCount)
            .Select(index => $"data/{index:00}")
            .ToArray();

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                foreach (string path in paths)
                {
                    builder.AddRaw(path, [(byte)path.Length]);
                }
            });

        using var host = new AssetServiceHost(packPath);
        var processed = new List<string>();
        host.Service.RequestProcessingHook = path =>
        {
            lock (processed)
            {
                processed.Add(path);
            }
        };
        host.Start();

        Task<byte[]>[] requests = paths
            .Select(path => host.Service.LoadData(path))
            .ToArray();
        await Task.WhenAll(requests);

        lock (processed)
        {
            Assert.Equal(paths, processed);
        }
    }

    [Fact]
    public async Task TasksCompleteOffTheCallerAndLoaderThreads()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1, 2, 3]));

        using var host = new AssetServiceHost(packPath);
        host.Start();
        Thread loader = Assert.IsType<Thread>(host.Service.LoaderThread);

        // A dedicated caller thread issues the request and then blocks, so it cannot be the thread
        // the asynchronous completion runs on.
        Task<byte[]>? request = null;
        using var callerGate = new ManualResetEventSlim(initialState: false);
        using var callerEntered = new ManualResetEventSlim(initialState: false);
        var caller = new Thread(() =>
        {
            request = host.Service.LoadData("data/raw");
            callerEntered.Set();
            callerGate.Wait(TimeSpan.FromSeconds(10));
        })
        {
            IsBackground = true,
        };
        caller.Start();

        Assert.True(callerEntered.Wait(TimeSpan.FromSeconds(10)));
        Task<byte[]> task = request!;

        var observed = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = task.ContinueWith(
            _ => observed.TrySetResult(Environment.CurrentManagedThreadId),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        int continuationThreadId = await observed.Task;
        await task;

        callerGate.Set();
        Assert.True(caller.Join(TimeSpan.FromSeconds(10)));

        Assert.NotEqual(caller.ManagedThreadId, continuationThreadId);
        Assert.NotEqual(loader.ManagedThreadId, continuationThreadId);
    }

    [Fact]
    public async Task StopCancelsQueuedAndInFlightRequestsAndJoinsTheLoader()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1, 2, 3]));

        using var host = new AssetServiceHost(packPath);
        using var gate = new ManualResetEventSlim(initialState: false);
        using var entered = new ManualResetEventSlim(initialState: false);
        host.Service.RequestProcessingHook = _ =>
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
        };
        host.Start();
        Thread loader = Assert.IsType<Thread>(host.Service.LoaderThread);

        Task<byte[]> inFlight = host.Service.LoadData("data/raw");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Task<byte[]> queued = host.Service.LoadData("data/raw");

        Task stop = Task.Run(host.Service.Stop);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inFlight);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(TaskStatus.Canceled, inFlight.Status);
        Assert.Equal(TaskStatus.Canceled, queued.Status);

        gate.Set();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(loader.IsAlive);
        Assert.Null(host.Service.LoaderThread);
    }
}
