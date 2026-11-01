using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Sprites;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The production offscreen Vulkan renderer: one render thread owns the instance (when the host did
/// not supply one), the device, two permanent sRGB targets, the snapshot ring, and the
/// dynamic-rendering clear, and records exactly one discarded frame per consumed snapshot until the
/// host stops it (Q7=G1, D-M6-07, D-M6-11).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> creates one background thread and returns immediately (D-M6-06). The thread
/// creates or adopts the instance, selects a device without surface/present requirements, chooses an
/// sRGB format, allocates two color images, transitions them once, creates two frames in flight, and
/// marks the renderer ready. The host observes readiness, status, counters, and the one-shot fatal
/// error through <see cref="IRenderer"/>; it drives the snapshot handoff through the slot API in
/// M7 WP-4.
/// </para>
/// <para>
/// With no <c>Ready</c> snapshot the render thread blocks on the combined snapshot/control wait and
/// never records a frame; a snapshot slot is held until the consuming GPU frame's fence is observed
/// and released when that frame is reused or during the stop/fatal/idle drain. There is no minimized
/// path in offscreen mode. There is no surface, swapchain, present, acquire, semaphore, or readback
/// (D-M6-23).
/// </para>
/// <para>
/// Steady-state frames allocate nothing, log nothing, and never call <c>vkDeviceWaitIdle</c>;
/// shutdown is the only place it is used. The renderer never calls SDL.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanOffscreenRenderer : IRenderer, IBindlessPageWriter
{
    private const int FramesInFlight = 2;
    private const int ColorImages = 2;
    private const string ThreadName = "Bork2d.Render";
    private const string PassLabelText = "Bork2d offscreen passes";
    private const ulong CleanupFenceTimeoutNanoseconds = 1_000_000_000;

    private readonly RendererStartInfo _startInfo;
    private readonly IVulkanInstanceFactory _instanceFactory;
    private readonly ILogger _logger;
    private readonly RendererControlChannel _control = new();
    private readonly SnapshotConsumer _consumer = new(new SnapshotRing(), FramesInFlight);
    private readonly SnapshotControlWait _wait;
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly ManualResetEventSlim _progress = new(initialState: false);
    private readonly RenderStatsBoard _stats = new();
    private readonly CaptureRequestQueue _captures = new();

    private OffscreenFrame?[] _frames = [];
    private OffscreenTarget? _target;
    private SpritePipeline? _spritePipeline;
    private TexturePageStore? _pages;
    private SpriteFramePlanner? _planner;
    private Thread? _thread;
    private VulkanDevice? _device;
    private IVulkanInstance? _instance;
    private bool _ownsInstance;
    private nint _passLabel;
    private int _frameIndex;
    private int _started;
    private int _status = (int)RendererStatus.NotStarted;
    private int _stopReason = (int)RendererStopReason.None;
    private int _readyFlag;
    private long _framesDiscarded;
    private long _lastFrameNumber;
    private int _validationErrorCount;
    private int _validationWarningCount;
    private int _loggedPageCount;
    private int _loggedPageCapacity;
    private int _loggedPageGrowths;
    private int _loggedFailedPaths;
    private int _loggedInstanceCapacity;
    private int _loggedInstanceGrowths;
    private volatile bool _validationEnabled;

    /// <summary>Initializes a new instance of the <see cref="VulkanOffscreenRenderer"/> class.</summary>
    /// <param name="startInfo">The validated offscreen start request.</param>
    /// <param name="instanceFactory">Creates the owned instance when the host supplies none.</param>
    /// <exception cref="Bork2dException">The start request has no asset seam.</exception>
    internal VulkanOffscreenRenderer(
        RendererStartInfo startInfo,
        IVulkanInstanceFactory instanceFactory)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(instanceFactory);
        if (startInfo.Assets is null)
        {
            throw new Bork2dException(
                "Offscreen Vulkan rendering requires the app's IAssets seam so the renderer can "
                    + "prefetch texture descriptors and payloads (M8 §6.5).");
        }

        _startInfo = startInfo;
        _instanceFactory = instanceFactory;
        _logger = startInfo.LoggerFactory.CreateLogger(RenderLog.Category);
        _wait = new SnapshotControlWait(_consumer.Ring, _control);
    }

    /// <inheritdoc/>
    public bool IsReady => Volatile.Read(ref _readyFlag) != 0;

    /// <inheritdoc/>
    public RendererStatus Status => (RendererStatus)Volatile.Read(ref _status);

    /// <inheritdoc/>
    public RendererStopReason StopReason => (RendererStopReason)Volatile.Read(ref _stopReason);

    /// <inheritdoc/>
    public RendererCounters Counters => new(
        FramesPresented: 0,
        FramesDiscarded: Volatile.Read(ref _framesDiscarded),
        SwapchainRecreations: 0,
        ValidationErrorCount: Volatile.Read(ref _validationErrorCount),
        ValidationWarningCount: Volatile.Read(ref _validationWarningCount),
        LastFrameNumber: Volatile.Read(ref _lastFrameNumber),
        LastPresentResult: 0);

    /// <inheritdoc/>
    public RenderStats RenderStats => _stats.Current;

    /// <inheritdoc/>
    public Exception? FatalException => _control.FatalException;

    /// <inheritdoc/>
    public SnapshotRingCounters SnapshotCounters => _consumer.Counters;

    /// <inheritdoc/>
    public long SuspendedConsumptions => _consumer.SuspendedConsumptions;

    /// <inheritdoc/>
    public Task<FrameCaptureResult> CaptureFrameAsync(CancellationToken cancellationToken = default)
        => _captures.Request(cancellationToken);

    /// <summary>Gets the renderer's snapshot ring (GPU tests observe slot state through it).</summary>
    internal SnapshotRing SnapshotRing => _consumer.Ring;

    /// <summary>Gets a value indicating whether every GPU frame owns its empty instance resources.</summary>
    internal bool HasInstanceResources
    {
        get
        {
            if (_frames.Length != FramesInFlight)
            {
                return false;
            }

            for (var index = 0; index < FramesInFlight; index++)
            {
                if (_frames[index]?.InstanceResources is null)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Gets a value indicating whether the sprite pipeline was created at startup.</summary>
    internal bool HasSpritePipeline => _spritePipeline is not null;

    /// <summary>Gets the texture-page cache (GPU tests observe the placeholder and counts).</summary>
    internal TexturePageStore? PageStore => _pages;

    /// <summary>Gets the largest instance-buffer capacity among the frames (GPU tests/diagnostics).</summary>
    internal int InstanceCapacityHighWater
    {
        get
        {
            var capacity = 0;
            for (var index = 0; index < _frames.Length; index++)
            {
                FrameInstanceResources? resources = _frames[index]?.InstanceResources;
                if (resources is not null)
                {
                    capacity = Math.Max(capacity, resources.InstanceCapacity);
                }
            }

            return capacity;
        }
    }

    /// <summary>Gets the total number of instance-buffer growths across the frames.</summary>
    internal int InstanceGrowthCount
    {
        get
        {
            var growths = 0;
            for (var index = 0; index < _frames.Length; index++)
            {
                growths += _frames[index]?.InstanceResources.GrowthCount ?? 0;
            }

            return growths;
        }
    }

    /// <inheritdoc/>
    void IBindlessPageWriter.EnsureCapacity(int capacity)
    {
        // Growth happens between frames: the device is idle and every in-flight frame's fence has
        // signaled before the per-frame descriptor pools/sets are recreated.
        WaitForDeviceIdle("texture page capacity growth");

        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.InstanceResources.MarkFenceSignaled();
            _frames[index]?.InstanceResources.RecreateBindless(capacity);
        }
    }

    /// <inheritdoc/>
    void IBindlessPageWriter.WritePage(int pageIndex, ImageView imageView, Sampler sampler)
    {
        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.InstanceResources.WritePage(pageIndex, imageView, sampler);
        }
    }

    /// <inheritdoc/>
    public SnapshotSlot? AcquireSnapshotSlot() => _consumer.Ring.Acquire();

    /// <inheritdoc/>
    public void PublishSnapshot(SnapshotSlot slot) => _consumer.Ring.Publish(slot);

    /// <inheritdoc/>
    public void AbandonSnapshotSlot(SnapshotSlot slot) => _consumer.Ring.Abandon(slot);

    /// <inheritdoc/>
    public void WakeSnapshotWait() => _consumer.Ring.SignalExit();

    /// <summary>Gets the number of discarded frames (diagnostic alias for the counter).</summary>
    internal long FramesDiscarded => Volatile.Read(ref _framesDiscarded);

    /// <summary>Gets a value indicating whether the instance actually enabled validation.</summary>
    /// <remarks>GPU tests use it to require validation evidence (D-M6-21).</remarks>
    internal bool ValidationEnabled => _validationEnabled;

    /// <inheritdoc/>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The Vulkan renderer was already started.");
        }

        SetStatus(RendererStatus.Starting);
        var thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = ThreadName,
        };
        _thread = thread;
        thread.Start();
    }

    /// <inheritdoc/>
    public void WaitForReady()
    {
        if (IsReady)
        {
            return;
        }

        _ready.Wait();
    }

    /// <inheritdoc/>
    public bool WaitForReady(TimeSpan timeout) => IsReady || _ready.Wait(timeout);

    /// <inheritdoc/>
    /// <remarks>Offscreen rendering has no window state; the post is ignored.</remarks>
    public void PostWindowExtent(WindowExtent extent)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Offscreen rendering has no window state; the post is ignored.</remarks>
    public void PostFullscreen(bool fullscreen)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Offscreen rendering has no window state; the post is ignored.</remarks>
    public void PostMinimized(bool minimized)
    {
    }

    /// <inheritdoc/>
    public void RequestStop()
    {
        Volatile.Write(ref _readyFlag, 0);
        _consumer.Ring.Stop();
        _captures.Close(CreateCaptureClosedException());
        _control.RequestStop();
        if (Volatile.Read(ref _stopReason) != (int)RendererStopReason.Fatal)
        {
            SetStopReason(RendererStopReason.Requested);
            RendererStatus status = Status;
            if (status is RendererStatus.Starting or RendererStatus.Ready)
            {
                SetStatus(RendererStatus.Stopping);
            }
        }

        _ready.Set();
        SignalProgress();
    }

    /// <inheritdoc/>
    public void Join()
    {
        _thread?.Join();
        DrainSnapshotsAfterJoin();
    }

    /// <inheritdoc/>
    public bool Join(TimeSpan timeout)
    {
        Thread? thread = _thread;
        if (thread is null)
        {
            DrainSnapshotsAfterJoin();
            return true;
        }

        if (!thread.Join(timeout))
        {
            return false;
        }

        DrainSnapshotsAfterJoin();
        return true;
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for a renderer predicate (GPU tests only;
    /// event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForProgress(Func<bool> predicate, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (predicate())
            {
                return true;
            }

            _progress.Reset();
            if (predicate())
            {
                return true;
            }

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            if (!_progress.Wait(remaining))
            {
                return predicate();
            }
        }
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for the counters to satisfy a predicate
    /// (GPU tests only; event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The counter condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForProgress(Func<RendererCounters, bool> predicate, TimeSpan timeout)
        => WaitForProgress(() => predicate(Counters), timeout);

    /// <summary>
    /// Frees every non-<c>Writing</c> slot once the render thread has joined (Q9=I1).
    /// </summary>
    /// <remarks>
    /// A fatal mid-tick may publish after the renderer's cleanup, so the ring cannot be fully
    /// drained until the render thread is gone. A slot still <c>Writing</c> belongs to a producer
    /// and is never touched.
    /// </remarks>
    private void DrainSnapshotsAfterJoin()
    {
        try
        {
            _consumer.Ring.DrainAfterRenderThreadJoined();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan snapshot-ring drain after join failed.");
        }
    }

    private void ThreadMain()
    {
        var faulted = false;
        try
        {
            _instance = _startInfo.Instance;
            if (_instance is null)
            {
                _instance = _instanceFactory.Create(
                    new VulkanInstanceRequest
                    {
                        RequiredExtensions = [],
                        ValidationRequested = _startInfo.ValidationRequested,
                        EngineName = "Bork2d",
                        LoggerFactory = _startInfo.LoggerFactory,
                    });
                _ownsInstance = true;
            }

            if (_instance.Handle == 0)
            {
                throw new Bork2dException(
                    "Offscreen Vulkan rendering requires a non-zero Vulkan instance handle.");
            }

            _validationEnabled = _instance.ValidationEnabled;

            if (_control.IsStopRequested)
            {
                SetStopReason(RendererStopReason.Requested);
                return;
            }

            _device = VulkanDeviceSelector.Select(
                _instance,
                requirePresent: false,
                surfaceHandle: 0,
                _logger);
            if (_control.IsStopRequested)
            {
                SetStopReason(RendererStopReason.Requested);
                return;
            }

            WindowExtent extent = ValidateExtent(_device);
            Format format = SelectFormat(_device);
            _target = OffscreenTarget.Create(
                _device,
                extent,
                format,
                ColorImages,
                _instance,
                _logger);
            _spritePipeline = SpritePipeline.Create(_device, _instance, _logger);
            _spritePipeline.GetOrCreatePipeline(format);
            if (_control.IsStopRequested)
            {
                SetStopReason(RendererStopReason.Requested);
                return;
            }

            CreateFrames();
            CreatePageStore();
            _planner = new SpriteFramePlanner(_pages!, _logger);
            TransitionTargets();
            _passLabel = _instance.CreateDebugLabel(PassLabelText);
            UpdateValidationCounters();
            if (_control.IsStopRequested)
            {
                SetStopReason(RendererStopReason.Requested);
                return;
            }

            Volatile.Write(ref _readyFlag, 1);
            SetStatus(RendererStatus.Ready);
            _ready.Set();
            LogStarted();

            RunFrames();
            SetStopReason(RendererStopReason.Requested);
        }
        catch (Exception exception)
        {
            faulted = true;
            PublishFatal(exception);
        }
        finally
        {
            Cleanup();
            Volatile.Write(ref _readyFlag, 0);
            if (faulted)
            {
                SetStopReason(RendererStopReason.Fatal);
                SetStatus(RendererStatus.Faulted);
            }
            else
            {
                SetStatus(RendererStatus.Stopped);
            }

            LogStopped();
            _ready.Set();
            SignalProgress();
        }
    }

    private void RunFrames()
    {
        VulkanDevice device = _device!;
        while (true)
        {
            ObserveValidation();
            if (IsStopping())
            {
                return;
            }

            // Complete captures from their own frame fences before waiting for a new snapshot: a
            // submitted capture must not depend on the game publishing another snapshot.
            RetireCompletedCaptures();

            // Q7=G1: one GPU frame per consumed snapshot; with none, wait without repeating.
            if (!_consumer.HasReadySnapshot)
            {
                if (_wait.Wait() == SnapshotWaitOutcome.Stopped)
                {
                    return;
                }

                continue;
            }

            OffscreenFrame frame = _frames[_frameIndex]!;
            Fence fence = frame.Fence;
            Result wait = device.Api.WaitForFences(
                device.Device,
                1,
                &fence,
                true,
                ulong.MaxValue);
            if (wait != Result.Success)
            {
                throw new Bork2dException($"vkWaitForFences failed with VkResult {wait}.");
            }

            if (IsStopping())
            {
                return;
            }

            // The frame fence has signaled: the frame's instance buffer is free to grow, its
            // recorded capture (if any) can complete from the staging bytes, and the slot it held is
            // retired before the newest Ready is taken for this frame. A null result means another
            // consumer took it; no frame.
            frame.InstanceResources.MarkFenceSignaled();
            frame.Capture.Complete();
            SnapshotSlot? slot = _consumer.TakeForFreeFrame(_frameIndex);
            if (slot is null)
            {
                continue;
            }

            // Advance the texture-page state machine once per frame, before recording.
            _pages!.Pump();

            Result resetFences = device.Api.ResetFences(device.Device, 1, &fence);
            if (resetFences != Result.Success)
            {
                throw new Bork2dException(
                    $"vkResetFences failed with VkResult {resetFences}.");
            }

            Result resetCommandBuffer = device.Api.ResetCommandBuffer(
                frame.CommandBuffer,
                CommandBufferResetFlags.None);
            if (resetCommandBuffer != Result.Success)
            {
                throw new Bork2dException(
                    $"vkResetCommandBuffer failed with VkResult {resetCommandBuffer}.");
            }

            OffscreenTarget target = _target!;
            SpriteFramePlan plan = _planner!.Plan(
                slot.Snapshot,
                (int)target.Extent.Width,
                (int)target.Extent.Height,
                frame.InstanceResources);
            RecordFrame(frame, _frameIndex, plan);
            Submit(frame);
            frame.InstanceResources.MarkSubmitted();
            frame.Capture.MarkSubmitted();
            _consumer.AttachToFrame(_frameIndex, slot);
            Interlocked.Increment(ref _lastFrameNumber);
            Interlocked.Increment(ref _framesDiscarded);
            PublishStats(slot.Snapshot.Sequence, target, plan);
            SignalProgress();
            _frameIndex = (_frameIndex + 1) % FramesInFlight;
        }
    }

    private bool IsStopping() => _control.IsStopRequested || _consumer.IsStopped;

    private void RecordFrame(OffscreenFrame frame, int imageIndex, SpriteFramePlan plan)
    {
        VulkanDevice device = _device!;
        OffscreenTarget target = _target!;
        CommandBuffer commandBuffer = frame.CommandBuffer;
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Result begin = device.Api.BeginCommandBuffer(commandBuffer, in beginInfo);
        if (begin != Result.Success)
        {
            throw new Bork2dException($"vkBeginCommandBuffer failed with VkResult {begin}.");
        }

        _instance!.BeginCachedCommandBufferLabel(
            device.Device.Handle,
            commandBuffer.Handle,
            _passLabel);

        // A camera may load instead of clearing, so one full-target opaque-black clear defines
        // every pixel before any camera pass runs.
        SpritePassRecorder.RecordFrameClear(
            device.Api,
            commandBuffer,
            target.GetView(imageIndex),
            target.Extent);

        bool capture = frame.Capture.BeginRecord(
            _captures,
            (int)target.Extent.Width,
            (int)target.Extent.Height,
            target.Format);
        SpritePassRecorder.RecordPasses(
            device.Api,
            commandBuffer,
            _spritePipeline!,
            _spritePipeline!.GetOrCreatePipeline(target.Format),
            frame.InstanceResources.DescriptorSet,
            target.GetView(imageIndex),
            target.GetImage(imageIndex),
            plan.Cameras,
            plan.CameraPlanCount);

        if (capture)
        {
            FrameCaptureRecorder.RecordCopy(
                device.Api,
                commandBuffer,
                target.GetImage(imageIndex),
                frame.Capture.StagingBuffer,
                (int)target.Extent.Width,
                (int)target.Extent.Height,
                toPresentSrc: false);
            frame.Capture.MarkRecorded();
        }

        _instance.EndCommandBufferLabel(device.Device.Handle, commandBuffer.Handle);

        Result end = device.Api.EndCommandBuffer(commandBuffer);
        if (end != Result.Success)
        {
            throw new Bork2dException($"vkEndCommandBuffer failed with VkResult {end}.");
        }
    }

    private void PublishStats(long sequence, OffscreenTarget target, SpriteFramePlan plan)
    {
        RenderStats stats = FrameRenderStats.Create(
            sequence,
            (int)target.Extent.Width,
            (int)target.Extent.Height,
            plan,
            _pages?.TexturePageCount ?? 0,
            _pages?.TexturePageCapacityInEntries ?? 0,
            Volatile.Read(ref _validationErrorCount),
            Volatile.Read(ref _validationWarningCount));
        _stats.Publish(in stats);
    }

    /// <summary>
    /// Completes captures whose frame fence has signaled, independent of frame reuse and snapshots.
    /// </summary>
    /// <remarks>
    /// Runs at the top of every render-loop iteration. Only frames that carry capture requests are
    /// waited on, so an idle loop still blocks on the snapshot/control wait without touching Vulkan.
    /// The snapshot slot is not released here; it stays held until the frame is reused or shutdown,
    /// as before.
    /// </remarks>
    private void RetireCompletedCaptures()
    {
        VulkanDevice device = _device!;
        for (var index = 0; index < _frames.Length; index++)
        {
            OffscreenFrame? frame = _frames[index];
            if (frame is null || !frame.Capture.HasRequests)
            {
                continue;
            }

            Fence fence = frame.Fence;
            Result wait = device.Api.WaitForFences(
                device.Device,
                1,
                &fence,
                true,
                ulong.MaxValue);
            if (wait != Result.Success)
            {
                throw new Bork2dException($"vkWaitForFences failed with VkResult {wait}.");
            }

            frame.InstanceResources.MarkFenceSignaled();
            frame.Capture.Complete();
        }
    }

    private void CompleteCaptures()
    {
        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.Capture.Complete();
        }
    }

    private ObjectDisposedException CreateCaptureClosedException()
        => new(
            nameof(VulkanOffscreenRenderer),
            "The offscreen renderer stopped before the capture request could be served.");

    private void Submit(OffscreenFrame frame)
    {
        VulkanDevice device = _device!;
        var commandBufferInfo = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = frame.CommandBuffer,
        };
        var submitInfo = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &commandBufferInfo,
        };

        Result result = device.Api.QueueSubmit2(
            device.Queue,
            1,
            &submitInfo,
            frame.Fence);
        if (result != Result.Success)
        {
            throw new Bork2dException($"vkQueueSubmit2 failed with VkResult {result}.");
        }
    }

    private void CreateFrames()
    {
        VulkanDevice device = _device!;
        SpritePipeline pipeline = _spritePipeline
            ?? throw new Bork2dException(
                "The offscreen renderer cannot create frames without the sprite pipeline.");
        _frames = new OffscreenFrame?[FramesInFlight];
        for (var index = 0; index < FramesInFlight; index++)
        {
            OffscreenFrame frame = OffscreenFrame.Create(
                device.Api,
                device.Device,
                device.PhysicalDevice,
                index,
                device.QueueFamilyIndex,
                pipeline,
                TexturePageCapacity.Initial,
                device.Limits.MaxStorageBufferRange,
                _instance,
                _logger);
            _frames[index] = frame;
            _instance!.SetObjectName(
                device.Device.Handle,
                ObjectType.CommandBuffer,
                frame.CommandBuffer.Handle,
                $"Bork2d offscreen frame {index} command buffer");
        }
    }

    /// <summary>
    /// Creates the texture-page cache, uploads the placeholder (waiting its fence at startup), and
    /// makes it resident at index 0. Failure is a renderer startup failure.
    /// </summary>
    private void CreatePageStore()
    {
        VulkanDevice device = _device!;
        SpritePipeline pipeline = _spritePipeline
            ?? throw new Bork2dException(
                "The offscreen renderer cannot create texture pages without the sprite pipeline.");
        var gpu = new VulkanTexturePages(device, pipeline, this, _instance, _logger);
        _pages = new TexturePageStore(
            _startInfo.Assets,
            gpu,
            (int)pipeline.MaxTexturePages,
            (int)device.Limits.MaxImageDimension2D,
            _logger);
        _pages.Start();
    }

    private void TransitionTargets()
    {
        VulkanDevice device = _device!;
        OffscreenTarget target = _target!;
        OffscreenFrame frame = _frames[0]!;
        Fence fence = frame.Fence;

        Result reset = device.Api.ResetFences(device.Device, 1, &fence);
        if (reset != Result.Success)
        {
            throw new Bork2dException($"vkResetFences failed with VkResult {reset}.");
        }

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Result begin = device.Api.BeginCommandBuffer(frame.CommandBuffer, in beginInfo);
        if (begin != Result.Success)
        {
            throw new Bork2dException($"vkBeginCommandBuffer failed with VkResult {begin}.");
        }

        target.RecordInitialLayoutTransition(frame.CommandBuffer);
        Result end = device.Api.EndCommandBuffer(frame.CommandBuffer);
        if (end != Result.Success)
        {
            throw new Bork2dException($"vkEndCommandBuffer failed with VkResult {end}.");
        }

        Submit(frame);
        Result wait = device.Api.WaitForFences(
            device.Device,
            1,
            &fence,
            true,
            CleanupFenceTimeoutNanoseconds);
        if (wait != Result.Success)
        {
            throw new Bork2dException(
                $"vkWaitForFences failed during the offscreen layout transition with VkResult "
                    + $"{wait}.");
        }
    }

    private WindowExtent ValidateExtent(VulkanDevice device)
    {
        int width = _startInfo.Width;
        int height = _startInfo.Height;
        if (width <= 0 || height <= 0)
        {
            throw new Bork2dException(
                $"The offscreen target extent must be positive; got {width}x{height}.");
        }

        uint max = device.Limits.MaxImageDimension2D;
        if ((uint)width > max || (uint)height > max)
        {
            throw new Bork2dException(
                $"The offscreen target extent {width}x{height} exceeds the selected device's "
                    + $"maxImageDimension2D of {max}.");
        }

        return new WindowExtent(width, height);
    }

    private Format SelectFormat(VulkanDevice device)
    {
        Format[] candidates = [Format.R8G8B8A8Srgb, Format.B8G8R8A8Srgb];
        var available = new OffscreenFormatSupport[candidates.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            FormatProperties properties = device.Api.GetPhysicalDeviceFormatProperties(
                device.PhysicalDevice,
                candidates[index]);
            available[index] = new OffscreenFormatSupport(
                candidates[index],
                (properties.OptimalTilingFeatures & FormatFeatureFlags.ColorAttachmentBit) != 0);
        }

        return SurfaceFormatPolicy.SelectOffscreen(available);
    }

    private void ObserveValidation()
    {
        UpdateValidationCounters();
        if (_instance?.FatalException is { } fatal)
        {
            throw fatal as Bork2dException
                ?? new Bork2dException("Vulkan validation reported a fatal error.", fatal);
        }
    }

    private void UpdateValidationCounters()
    {
        if (_instance is not { } instance)
        {
            return;
        }

        Volatile.Write(ref _validationErrorCount, instance.ValidationErrorCount);
        Volatile.Write(ref _validationWarningCount, instance.ValidationWarningCount);
    }

    private void PublishFatal(Exception exception)
    {
        var fatal = exception as Bork2dException
            ?? new Bork2dException(
                "The Vulkan offscreen renderer failed: " + exception.Message,
                exception);

        // A renderer fatal wakes a blocked producer, stops further acquisition, and faults pending
        // capture requests (M8 §6.12).
        _consumer.Ring.Stop();
        _captures.Close(fatal);
        _control.PublishFatal(fatal);
        SetStopReason(RendererStopReason.Fatal);
        SetStatus(RendererStatus.Faulted);
        _logger.LogError(fatal, "Vulkan offscreen renderer fatal failure.");
    }

    private void Cleanup()
    {
        UpdateValidationCounters();
        VulkanDevice? device = _device;
        if (device is not null)
        {
            WaitForFrameFences(device);
            try
            {
                Result idle = device.Api.DeviceWaitIdle(device.Device);
                if (idle != Result.Success)
                {
                    _logger.LogWarning(
                        "Vulkan device idle wait returned {Result} during offscreen shutdown.",
                        idle);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Vulkan device idle wait failed during offscreen shutdown.");
            }
        }

        if (_passLabel != 0 && _instance is { } instance)
        {
            try
            {
                instance.DestroyDebugLabel(_passLabel);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan debug label destruction failed.");
            }
            finally
            {
                _passLabel = 0;
            }
        }

        // Every frame fence was observed and the device is idle: every held snapshot slot returns
        // to Free before the device is destroyed. Capture requests recorded on those frames complete
        // here from their staging bytes; requests that never reached a frame fault instead.
        ReleaseHeldSnapshots();

        CompleteCaptures();
        _captures.Close(CreateCaptureClosedException());

        CapturePageCounters();
        CaptureInstanceCounters();

        _target?.Dispose();
        _target = null;

        // Frame descriptor sets/pools are destroyed before the page images/views they reference,
        // and the pages before the pipeline that owns their samplers and the device itself.
        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.Dispose();
            _frames[index] = null;
        }

        _pages?.Destroy();
        _pages = null;

        _spritePipeline?.Destroy();
        _spritePipeline = null;

        _device?.Destroy();
        _device = null;

        if (_ownsInstance)
        {
            _instance?.Destroy();
        }

        _instance = null;
    }

    private void WaitForDeviceIdle(string operation)
    {
        VulkanDevice device = _device!;
        Result result = device.Api.DeviceWaitIdle(device.Device);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkDeviceWaitIdle failed with VkResult {result} during {operation}.");
        }
    }

    private void WaitForFrameFences(VulkanDevice device)
    {
        for (var index = 0; index < _frames.Length; index++)
        {
            OffscreenFrame? frame = _frames[index];
            if (frame is null)
            {
                continue;
            }

            Fence fence = frame.Fence;
            try
            {
                Result result = device.Api.WaitForFences(
                    device.Device,
                    1,
                    &fence,
                    true,
                    CleanupFenceTimeoutNanoseconds);
                if (result != Result.Success)
                {
                    _logger.LogWarning(
                        "Vulkan offscreen frame fence wait returned {Result} during shutdown.",
                        result);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Vulkan offscreen frame fence wait failed during shutdown.");
            }
        }
    }

    private void ReleaseHeldSnapshots()
    {
        try
        {
            _consumer.ReleaseAll();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan snapshot-slot release failed during shutdown.");
        }
    }

    /// <summary>Records the page counters before the page store is destroyed for the stop log.</summary>
    private void CapturePageCounters()
    {
        TexturePageStore? pages = _pages;
        if (pages is null)
        {
            return;
        }

        _loggedPageCount = pages.TexturePageCount;
        _loggedPageCapacity = pages.TexturePageCapacityInEntries;
        _loggedPageGrowths = pages.GrowthCount;
        _loggedFailedPaths = pages.FailedPathCount;
    }

    /// <summary>Records the instance-buffer high-water marks before the frames are destroyed.</summary>
    private void CaptureInstanceCounters()
    {
        var capacity = 0;
        var growths = 0;
        for (var index = 0; index < _frames.Length; index++)
        {
            FrameInstanceResources? resources = _frames[index]?.InstanceResources;
            if (resources is null)
            {
                continue;
            }

            capacity = Math.Max(capacity, resources.InstanceCapacity);
            growths += resources.GrowthCount;
        }

        _loggedInstanceCapacity = capacity;
        _loggedInstanceGrowths = growths;
    }

    private void LogStarted()
    {
        OffscreenTarget? target = _target;
        if (target is null)
        {
            return;
        }

        _logger.LogInformation(
            "Vulkan renderer started: mode={Mode}, extent={Width}x{Height}, format={Format}, "
                + "images={ImageCount}, framesInFlight={FramesInFlight}, memoryBytes={MemoryBytes}, "
                + "handoff={Handoff}, ringDepth={RingDepth}, "
                + "oneFramePerSnapshot={OneFramePerSnapshot}, instanceResources={InstanceResources}, "
                + "instanceCapacity={InstanceCapacity}, texturePages={TexturePages}, "
                + "texturePageCapacity={TexturePageCapacity}, bindlessLimit={BindlessLimit}, "
                + "placeholder={Placeholder}, validation={Validation}, "
                + "framesPresented={FramesPresented}, "
                + "framesDiscarded={FramesDiscarded}, swapchainRecreations={SwapchainRecreations}, "
                + "validationErrors={ValidationErrors}, validationWarnings={ValidationWarnings}, "
                + "stopReason={StopReason}.",
            nameof(RendererMode.Offscreen),
            target.Extent.Width,
            target.Extent.Height,
            target.Format,
            target.ImageCount,
            FramesInFlight,
            target.MemoryBytes,
            "on",
            SnapshotRing.SlotCapacity,
            "on",
            HasInstanceResources ? "on" : "off",
            FrameInstanceResources.InitialCapacity,
            _pages?.TexturePageCount ?? 0,
            _pages?.TexturePageCapacityInEntries ?? 0,
            _spritePipeline?.MaxTexturePages ?? 0,
            _pages?.IsPlaceholderReady == true ? "on" : "off",
            _instance?.ValidationEnabled == true ? "on" : "off",
            0,
            Volatile.Read(ref _framesDiscarded),
            0,
            Volatile.Read(ref _validationErrorCount),
            Volatile.Read(ref _validationWarningCount),
            StopReason);
    }

    private void LogStopped()
    {
        SnapshotRingCounters snapshots = _consumer.Counters;
        _logger.LogInformation(
            "Vulkan renderer stopped: mode={Mode}, reason={Reason}, "
                + "snapshotsPublished={SnapshotsPublished}, snapshotsConsumed={SnapshotsConsumed}, "
                + "snapshotsSuperseded={SnapshotsSuperseded}, "
                + "backpressureWaits={BackpressureWaits}, maxLiveSnapshots={MaxLiveSnapshots}, "
                + "suspendedConsumptions={SuspendedConsumptions}, "
                + "framesPresented={FramesPresented}, framesDiscarded={FramesDiscarded}, "
                + "swapchainRecreations={SwapchainRecreations}, texturePages={TexturePages}, "
                + "texturePageCapacity={TexturePageCapacity}, pageGrowths={PageGrowths}, "
                + "failedPages={FailedPages}, instanceCapacity={InstanceCapacity}, "
                + "instanceGrowths={InstanceGrowths}, drawCalls={DrawCalls}, "
                + "instances={Instances}, cameras={Cameras}, "
                + "nonEmptyCameras={NonEmptyCameras}, skippedSprites={SkippedSprites}, "
                + "validationErrors={ValidationErrors}, validationWarnings={ValidationWarnings}, "
                + "lastFrameNumber={LastFrameNumber}, lastPresentResult={LastPresentResult}.",
            nameof(RendererMode.Offscreen),
            StopReason,
            snapshots.Published,
            snapshots.Consumed,
            snapshots.Superseded,
            snapshots.BackpressureWaits,
            snapshots.MaxLive,
            _consumer.SuspendedConsumptions,
            0,
            Volatile.Read(ref _framesDiscarded),
            0,
            _loggedPageCount,
            _loggedPageCapacity,
            _loggedPageGrowths,
            _loggedFailedPaths,
            _loggedInstanceCapacity,
            _loggedInstanceGrowths,
            _stats.Current.DrawCalls,
            _stats.Current.Instances,
            _stats.Current.Cameras,
            _stats.Current.NonEmptyCameras,
            _stats.Current.SkippedSprites,
            Volatile.Read(ref _validationErrorCount),
            Volatile.Read(ref _validationWarningCount),
            Volatile.Read(ref _lastFrameNumber),
            0);
    }

    private void SetStatus(RendererStatus status) => Volatile.Write(ref _status, (int)status);

    private void SetStopReason(RendererStopReason reason)
    {
        while (true)
        {
            var current = (RendererStopReason)Volatile.Read(ref _stopReason);
            if (current == RendererStopReason.Fatal
                || (current == RendererStopReason.Requested
                    && reason != RendererStopReason.Fatal))
            {
                return;
            }

            if (Interlocked.CompareExchange(
                ref _stopReason,
                (int)reason,
                (int)current) == (int)current)
            {
                return;
            }
        }
    }

    private void SignalProgress() => _progress.Set();
}
