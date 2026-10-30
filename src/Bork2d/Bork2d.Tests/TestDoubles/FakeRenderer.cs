using Bork2d.Render;
using Bork2d.Render.Diagnostics;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic <see cref="IRenderer"/> used by host tests instead of Vulkan.
/// </summary>
/// <remarks>
/// Readiness, readiness failure, and a post-start fatal are explicit calls, so tests never sleep
/// or poll. Waits are bounded overloads; every stop path signals the join event.
/// </remarks>
internal sealed class FakeRenderer : IRenderer
{
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly ManualResetEventSlim _stopped = new(initialState: false);
    private Exception? _fatalException;
    private int _status = (int)RendererStatus.NotStarted;
    private int _stopReason = (int)RendererStopReason.None;
    private bool _isReady;
    private int _stopRequested;
    private int _joined;

    /// <summary>Gets or sets a value indicating whether <see cref="Start"/> becomes ready.</summary>
    internal bool AutoReady { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="RequestStop"/> completes.</summary>
    internal bool StopOnRequest { get; set; } = true;

    /// <summary>Gets a value indicating whether <see cref="Start"/> was called.</summary>
    internal bool Started { get; private set; }

    /// <summary>Gets a value indicating whether a stop was requested.</summary>
    internal bool StopRequested => Volatile.Read(ref _stopRequested) != 0;

    /// <summary>Gets a value indicating whether the renderer was joined.</summary>
    internal bool Joined => Volatile.Read(ref _joined) != 0;

    /// <summary>Gets the number of extent posts.</summary>
    internal int ExtentPostCount { get; private set; }

    /// <summary>Gets the number of fullscreen posts.</summary>
    internal int FullscreenPostCount { get; private set; }

    /// <summary>Gets the number of minimized posts.</summary>
    internal int MinimizedPostCount { get; private set; }

    /// <summary>Gets the latest posted extent.</summary>
    internal WindowExtent? LastExtent { get; private set; }

    /// <summary>Gets the latest posted fullscreen value.</summary>
    internal bool? LastFullscreen { get; private set; }

    /// <summary>Gets the latest posted minimized value.</summary>
    internal bool? LastMinimized { get; private set; }

    /// <summary>Gets or sets the counters reported to the host.</summary>
    internal RendererCounters Counters { get; set; } = RendererCounters.Empty;

    /// <inheritdoc/>
    public bool IsReady => _isReady;

    /// <inheritdoc/>
    public RendererStatus Status => (RendererStatus)Volatile.Read(ref _status);

    /// <inheritdoc/>
    public RendererStopReason StopReason => (RendererStopReason)Volatile.Read(ref _stopReason);

    /// <inheritdoc/>
    RendererCounters IRenderer.Counters => Counters;

    /// <inheritdoc/>
    public Exception? FatalException => Volatile.Read(ref _fatalException);

    /// <inheritdoc/>
    public void Start()
    {
        if (Started)
        {
            throw new InvalidOperationException("The fake renderer was already started.");
        }

        Started = true;
        SetStatus(RendererStatus.Starting);
        if (AutoReady)
        {
            SetReady();
        }
    }

    /// <summary>Signals readiness.</summary>
    internal void SetReady()
    {
        _isReady = true;
        SetStatus(RendererStatus.Ready);
        _ready.Set();
    }

    /// <summary>Signals a readiness failure and completes the renderer.</summary>
    /// <param name="exception">The failure the host observes.</param>
    internal void FailReadiness(Exception exception) => PublishFatal(exception);

    /// <summary>Publishes a post-start fatal error and completes the renderer.</summary>
    /// <param name="exception">The failure the host observes.</param>
    internal void PublishFatal(Exception exception)
    {
        Volatile.Write(ref _fatalException, exception);
        _isReady = false;
        SetStatus(RendererStatus.Faulted);
        Volatile.Write(ref _stopReason, (int)RendererStopReason.Fatal);
        _ready.Set();
        CompleteStop();
    }

    /// <summary>Completes the fake stop once the test's in-flight work is done.</summary>
    internal void CompleteStop()
    {
        if (Status != RendererStatus.Faulted)
        {
            SetStatus(RendererStatus.Stopped);
        }

        _stopped.Set();
    }

    /// <inheritdoc/>
    public void WaitForReady() => _ready.Wait();

    /// <inheritdoc/>
    public bool WaitForReady(TimeSpan timeout) => _ready.Wait(timeout);

    /// <inheritdoc/>
    public void PostWindowExtent(WindowExtent extent)
    {
        ExtentPostCount++;
        LastExtent = extent;
    }

    /// <inheritdoc/>
    public void PostFullscreen(bool fullscreen)
    {
        FullscreenPostCount++;
        LastFullscreen = fullscreen;
    }

    /// <inheritdoc/>
    public void PostMinimized(bool minimized)
    {
        MinimizedPostCount++;
        LastMinimized = minimized;
    }

    /// <inheritdoc/>
    public void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            return;
        }

        if (Status != RendererStatus.Faulted)
        {
            SetStatus(RendererStatus.Stopping);
            Volatile.Write(ref _stopReason, (int)RendererStopReason.Requested);
        }

        if (StopOnRequest)
        {
            CompleteStop();
        }
    }

    /// <inheritdoc/>
    public void Join()
    {
        _stopped.Wait();
        Volatile.Write(ref _joined, 1);
    }

    /// <inheritdoc/>
    public bool Join(TimeSpan timeout)
    {
        if (!_stopped.Wait(timeout))
        {
            return false;
        }

        Volatile.Write(ref _joined, 1);
        return true;
    }

    private void SetStatus(RendererStatus status) => Volatile.Write(ref _status, (int)status);
}
