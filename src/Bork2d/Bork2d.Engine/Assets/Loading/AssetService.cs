using System.Diagnostics.CodeAnalysis;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Vfs;
using Microsoft.Extensions.Logging;
using PolyType;

namespace Bork2d.Engine.Assets.Loading;

/// <summary>
/// The app-scoped asset service: one dedicated loader thread, one unbounded FIFO request queue, and
/// an internal base/overlay filesystem.
/// </summary>
/// <remarks>
/// <para>
/// The constructor allocates the in-memory filesystem only: it does not resolve a path, open a
/// file, or start a thread, so <c>Build()</c> can stay filesystem-free. <see cref="Start"/> opens
/// and validates the base pack (unless the service was created for an empty filesystem) and then
/// starts exactly one loader thread. <see cref="Stop"/> stops accepting requests, cancels queued
/// and in-flight load tasks, joins the loader, and closes the mounted packs. Both are idempotent
/// at the <see cref="Dispose"/> level.
/// </para>
/// <para>
/// A load request resolves its winning pack entry at enqueue time, so a later overlay mount never
/// changes an in-flight request. The loader thread only reads, decompresses, and deserializes; it
/// never touches SDL, Vulkan, or game entities. Tasks complete asynchronously, never inline on the
/// caller or loader thread.
/// </para>
/// <para>
/// The service treats a mounted pack as trusted content. It does not validate asset fields or
/// follow <see cref="AssetRef{TAsset}"/>/<see cref="DataRef"/> links; pack creation owns reference
/// integrity and the bake tooling owns field validation.
/// </para>
/// <para>
/// After <see cref="Stop"/> or <see cref="Dispose"/> every <see cref="IAssets"/> member throws
/// <see cref="ObjectDisposedException"/>. The owning <c>IEngine.Assets</c> getter never throws by
/// itself (D-M3-14).
/// </para>
/// </remarks>
internal sealed class AssetService : IAssets, IDisposable
{
    private const string Category = "Bork2d.Engine.Assets";
    private const string DataKind = "data";
    private const string MessagePackKind = "messagepack";

    private readonly Lock _lock = new();
    private readonly AssetFileSystem _fileSystem = new();
    private readonly Queue<AssetRequest> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ILogger _logger;
    private readonly string? _basePackPath;

    private Thread? _loaderThread;
    private AssetRequest? _inFlight;
    private bool _started;

    // Read on every public member without taking the lock, so shutdown is observed promptly.
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetService"/> class without touching the
    /// filesystem or starting a thread.
    /// </summary>
    /// <param name="basePackPath">
    /// The configured base pack path, or <see langword="null"/> for an empty filesystem
    /// (<c>WithoutAssets</c>). Relative paths resolve against the process current directory when
    /// <see cref="Start"/> opens the pack.
    /// </param>
    /// <param name="loggerFactory">The app-owned logger factory.</param>
    internal AssetService(string? basePackPath, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _basePackPath = basePackPath;
        _logger = loggerFactory.CreateLogger(Category);
    }

    /// <summary>
    /// Gets or sets a deterministic test hook invoked on the loader thread after a request is
    /// dequeued and before it executes.
    /// </summary>
    /// <remarks>
    /// Production code never sets this property. Tests use it to observe FIFO processing order and
    /// to block the loader deterministically without sleeps.
    /// </remarks>
    internal Action<string>? RequestProcessingHook { get; set; }

    /// <summary>
    /// Gets or sets a deterministic test hook invoked on the caller of <see cref="Stop"/> after the
    /// service marks itself disposed. Tests use it to wait for shutdown ordering without sleeps.
    /// </summary>
    /// <remarks>Production code never sets this property.</remarks>
    internal Action? ServiceDisposedHook { get; set; }

    /// <summary>
    /// Gets the dedicated loader thread, or <see langword="null"/> before
    /// <see cref="Start"/> or after it has been joined.
    /// </summary>
    internal Thread? LoaderThread => Volatile.Read(ref _loaderThread);

    /// <summary>
    /// Opens and validates the base pack, starts the loader thread, and logs the start event.
    /// </summary>
    /// <exception cref="Bork2dException">
    /// The configured base pack is missing or malformed. The partial service is cleaned up and no
    /// loader thread is left running.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The service has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The service has already been started.</exception>
    internal void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The asset service has already been started.");
            }

            _started = true;
        }

        PackMountInfo? mount = null;
        try
        {
            if (_basePackPath is not null)
            {
                try
                {
                    mount = _fileSystem.LoadPack(_basePackPath);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    _logger.LogError(
                        exception,
                        "Failed to open the base asset pack '{PackPath}': {Reason}.",
                        _basePackPath,
                        exception.Message);
                    throw;
                }
            }

            var loader = new Thread(Run)
            {
                IsBackground = true,
                Name = "Bork2d.Engine.Assets.Loader",
            };

            Volatile.Write(ref _loaderThread, loader);
            loader.Start();
        }
        catch
        {
            CleanupAfterStartFailure();
            throw;
        }

        LogStarted(mount);
    }

    /// <summary>
    /// Stops accepting requests, cancels queued and in-flight load tasks, joins the loader thread,
    /// and closes the mounted packs. Idempotent; secondary failures are logged, not thrown.
    /// </summary>
    internal void Stop()
    {
        AssetRequest[] queued;
        AssetRequest? inFlight;
        Thread? loader;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            queued = _queue.ToArray();
            _queue.Clear();
            inFlight = _inFlight;
            _inFlight = null;
            loader = _loaderThread;
        }

        ServiceDisposedHook?.Invoke();

        int canceled = CancelRequests(queued, inFlight, _stopping.Token);

        try
        {
            _stopping.Cancel();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogError(exception, "Failed to signal asset-loader shutdown.");
        }

        if (loader is not null && loader != Thread.CurrentThread)
        {
            try
            {
                loader.Join();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogError(exception, "Failed to join the asset loader thread.");
            }

            Volatile.Write(ref _loaderThread, null);
        }

        int packCount = 0;
        try
        {
            packCount = _fileSystem.PackCount;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogError(exception, "Failed to read the mounted pack count during shutdown.");
        }

        try
        {
            _fileSystem.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogError(exception, "Failed to close the mounted asset packs.");
        }

        _stopping.Dispose();
        _signal.Dispose();

        _logger.LogInformation(
            "Asset service stopped: pending {PendingCount}, canceled {CanceledCount}, mounted packs {PackCount}.",
            queued.Length + (inFlight is null ? 0 : 1),
            canceled,
            packCount);
    }

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(string assetPath)
        where T : IShapeable<T>
    {
        string canonicalPath = AssetPath.Normalize(assetPath);
        ThrowIfDisposed();

        AssetSnapshot snapshot = _fileSystem.CaptureSnapshot();
        if (!snapshot.TryGetEntry(canonicalPath, out ResolvedAsset? entry))
        {
            return Faulted<T>(AssetFailures.NotFound(canonicalPath));
        }

        if (entry.IsRaw)
        {
            return Faulted<T>(AssetFailures.RawAsset(canonicalPath));
        }

        var request = new LoadRequest<T>(
            canonicalPath,
            entry.ResolvedPackPath,
            MessagePackKind,
            () => AssetDeserializer.Deserialize<T>(entry));
        Enqueue(request);
        return request.TypedCompletion;
    }

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(AssetRef<T> reference)
        where T : IShapeable<T>
    {
        if (reference.Path is null)
        {
            throw new ArgumentException(
                "The asset reference has no path.",
                nameof(reference));
        }

        return LoadAsset<T>(reference.Path);
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(string assetPath)
    {
        string canonicalPath = AssetPath.Normalize(assetPath);
        ThrowIfDisposed();

        AssetSnapshot snapshot = _fileSystem.CaptureSnapshot();
        if (!snapshot.TryGetEntry(canonicalPath, out ResolvedAsset? entry))
        {
            return Faulted<byte[]>(AssetFailures.NotFound(canonicalPath));
        }

        if (entry.Size > Array.MaxLength)
        {
            return Faulted<byte[]>(AssetFailures.InvalidData(
                canonicalPath,
                $"the declared size {entry.Size} bytes exceeds Array.MaxLength"));
        }

        var request = new LoadRequest<byte[]>(
            canonicalPath,
            entry.ResolvedPackPath,
            DataKind,
            entry.ReadDecompressedBytes);
        Enqueue(request);
        return request.TypedCompletion;
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(DataRef reference)
    {
        if (reference.Path is null)
        {
            throw new ArgumentException(
                "The data reference has no path.",
                nameof(reference));
        }

        return LoadData(reference.Path);
    }

    /// <inheritdoc/>
    public void LoadPack(string packPath)
    {
        ThrowIfDisposed();

        PackMountInfo mount;
        try
        {
            mount = _fileSystem.LoadPack(packPath);
        }
        catch (Bork2dException exception)
        {
            _logger.LogError(
                exception,
                "Failed to mount asset pack '{PackPath}': {Reason}.",
                packPath,
                exception.Message);
            throw;
        }

        _logger.LogInformation(
            "Mounted asset pack '{PackPath}' above the current stack: {EntryCount} entries.",
            mount.SuppliedPath,
            mount.EntryCount);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
    {
        ThrowIfDisposed();
        return _fileSystem.Browse(prefix);
    }

    /// <inheritdoc/>
    public bool Exists(string assetPath)
    {
        ThrowIfDisposed();
        return _fileSystem.Exists(assetPath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        bool started;
        lock (_lock)
        {
            started = _started;
        }

        if (started)
        {
            Stop();
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        DisposeResources();
    }

    private static int CancelRequests(
        AssetRequest[] queued,
        AssetRequest? inFlight,
        CancellationToken cancellationToken)
    {
        int canceled = 0;
        foreach (AssetRequest request in queued)
        {
            if (request.Cancel(cancellationToken))
            {
                canceled++;
            }
        }

        if (inFlight is not null && inFlight.Cancel(cancellationToken))
        {
            canceled++;
        }

        return canceled;
    }

    private void Enqueue(AssetRequest request)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _queue.Enqueue(request);
            _signal.Release();
        }
    }

    private void Run()
    {
        try
        {
            while (TryTakeNext(out AssetRequest? request))
            {
                ExecuteRequest(request);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The asset loader thread stopped unexpectedly.");
        }
        finally
        {
            try
            {
                CancelRemaining();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogError(exception, "Failed to cancel the remaining asset requests.");
            }
        }
    }

    private bool TryTakeNext([NotNullWhen(true)] out AssetRequest? request)
    {
        while (true)
        {
            try
            {
                _signal.Wait(_stopping.Token);
            }
            catch (OperationCanceledException)
            {
                request = null;
                return false;
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    request = null;
                    return false;
                }

                if (_queue.Count == 0)
                {
                    continue;
                }

                request = _queue.Dequeue();
                _inFlight = request;
            }

            return true;
        }
    }

    private void ExecuteRequest(AssetRequest request)
    {
        try
        {
            RequestProcessingHook?.Invoke(request.CanonicalPath);
            if (request.Completion.IsCanceled || request.IsDropped)
            {
                return;
            }

            request.Execute();

            if (request.Completion.IsCanceled || request.IsDropped)
            {
                return;
            }

            // Log before completing so a caller that observes the task also observes the event.
            _logger.LogDebug(
                "Asset loaded: path {AssetPath}, kind {AssetKind}.",
                request.CanonicalPath,
                request.Kind);
            request.Complete();
        }
        catch (Exception exception)
        {
            Exception failure = request.RecordFault(exception);
            LogLoadFailure(failure);
            request.CompleteFault(failure);
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_inFlight, request))
                {
                    _inFlight = null;
                }
            }
        }
    }

    private void CancelRemaining()
    {
        AssetRequest[] remaining;
        lock (_lock)
        {
            remaining = _queue.ToArray();
            _queue.Clear();
        }

        if (remaining.Length == 0)
        {
            return;
        }

        CancelRequests(remaining, inFlight: null, cancellationToken: _stopping.Token);
    }

    private Task<T> Faulted<T>(AssetException exception)
    {
        LogLoadFailure(exception);
        return Task.FromException<T>(exception);
    }

    private void LogStarted(PackMountInfo? mount)
    {
        if (mount is { } info)
        {
            _logger.LogInformation(
                "Asset service started: mode {Mode}, configured pack {ConfiguredPath}, resolved "
                    + "path {ResolvedPath}, version {Version}, entries {EntryCount}, stored bytes "
                    + "{StoredBytes}.",
                "Pack",
                _basePackPath,
                info.ResolvedPath,
                info.Version,
                info.EntryCount,
                info.StoredBytes);
            return;
        }

        _logger.LogInformation(
            "Asset service started: mode {Mode}, configured pack {ConfiguredPath}, resolved path "
                + "{ResolvedPath}, version {Version}, entries {EntryCount}, stored bytes "
                + "{StoredBytes}.",
            "Empty",
            "<none>",
            "<none>",
            0U,
            0,
            0L);
    }

    private void LogLoadFailure(Exception failure)
    {
        if (failure is AssetException assetException)
        {
            _logger.LogError(
                assetException,
                "Asset load failed: path {AssetPath}, kind {AssetErrorKind}, reason {Reason}.",
                assetException.AssetPath ?? "<unknown>",
                assetException.Kind,
                assetException.Message);
            return;
        }

        _logger.LogError(failure, "Asset load failed: {Reason}.", failure.Message);
    }

    private void CleanupAfterStartFailure()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        Volatile.Write(ref _loaderThread, null);
        DisposeResources();
    }

    private void DisposeResources()
    {
        try
        {
            _fileSystem.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogError(exception, "Failed to close the asset filesystem.");
        }

        _stopping.Dispose();
        _signal.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
