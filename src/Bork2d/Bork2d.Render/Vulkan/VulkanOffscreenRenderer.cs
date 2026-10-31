using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
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
internal sealed unsafe class VulkanOffscreenRenderer : IRenderer
{
    private const int FramesInFlight = 2;
    private const int ColorImages = 2;
    private const string ThreadName = "Bork2d.Render";
    private const string ClearLabelText = "Bork2d offscreen clear pass";
    private const ulong CleanupFenceTimeoutNanoseconds = 1_000_000_000;

    private readonly RendererStartInfo _startInfo;
    private readonly IVulkanInstanceFactory _instanceFactory;
    private readonly ILogger _logger;
    private readonly RendererControlChannel _control = new();
    private readonly SnapshotConsumer _consumer = new(new SnapshotRing(), FramesInFlight);
    private readonly SnapshotControlWait _wait;
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly ManualResetEventSlim _progress = new(initialState: false);

    private OffscreenFrame?[] _frames = [];
    private OffscreenTarget? _target;
    private SpritePipeline? _spritePipeline;
    private Thread? _thread;
    private VulkanDevice? _device;
    private IVulkanInstance? _instance;
    private bool _ownsInstance;
    private nint _clearLabel;
    private int _frameIndex;
    private int _started;
    private int _status = (int)RendererStatus.NotStarted;
    private int _stopReason = (int)RendererStopReason.None;
    private int _readyFlag;
    private long _framesDiscarded;
    private long _lastFrameNumber;
    private int _validationErrorCount;
    private int _validationWarningCount;
    private volatile bool _validationEnabled;

    /// <summary>Initializes a new instance of the <see cref="VulkanOffscreenRenderer"/> class.</summary>
    /// <param name="startInfo">The validated offscreen start request.</param>
    /// <param name="instanceFactory">Creates the owned instance when the host supplies none.</param>
    internal VulkanOffscreenRenderer(
        RendererStartInfo startInfo,
        IVulkanInstanceFactory instanceFactory)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(instanceFactory);
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
    public Exception? FatalException => _control.FatalException;

    /// <inheritdoc/>
    public SnapshotRingCounters SnapshotCounters => _consumer.Counters;

    /// <inheritdoc/>
    public long SuspendedConsumptions => _consumer.SuspendedConsumptions;

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
            TransitionTargets();
            _clearLabel = _instance.CreateDebugLabel(ClearLabelText);
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

            // The frame fence has signaled: the slot it held is retired, then the newest Ready is
            // taken for this frame. A null result means another consumer took it; no frame.
            SnapshotSlot? slot = _consumer.TakeForFreeFrame(_frameIndex);
            if (slot is null)
            {
                continue;
            }

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

            RecordClear(frame, _frameIndex);
            Submit(frame);
            _consumer.AttachToFrame(_frameIndex, slot);
            Interlocked.Increment(ref _lastFrameNumber);
            Interlocked.Increment(ref _framesDiscarded);
            SignalProgress();
            _frameIndex = (_frameIndex + 1) % FramesInFlight;
        }
    }

    private bool IsStopping() => _control.IsStopRequested || _consumer.IsStopped;

    private void RecordClear(OffscreenFrame frame, int imageIndex)
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
            _clearLabel);
        var attachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = target.GetView(imageIndex),
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue
            {
                Color = new ClearColorValue
                {
                    Float32_0 = 0f,
                    Float32_1 = 0f,
                    Float32_2 = 0f,
                    Float32_3 = 1f,
                },
            },
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D { Extent = target.Extent },
            LayerCount = 1,
            ViewMask = 0,
            ColorAttachmentCount = 1,
            PColorAttachments = &attachment,
            PDepthAttachment = null,
            PStencilAttachment = null,
        };
        device.Api.CmdBeginRendering(commandBuffer, &rendering);
        device.Api.CmdEndRendering(commandBuffer);
        _instance.EndCommandBufferLabel(device.Device.Handle, commandBuffer.Handle);

        Result end = device.Api.EndCommandBuffer(commandBuffer);
        if (end != Result.Success)
        {
            throw new Bork2dException($"vkEndCommandBuffer failed with VkResult {end}.");
        }
    }

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
        _frames = new OffscreenFrame?[FramesInFlight];
        for (var index = 0; index < FramesInFlight; index++)
        {
            OffscreenFrame frame = OffscreenFrame.Create(
                device.Api,
                device.Device,
                device.PhysicalDevice,
                index,
                device.QueueFamilyIndex,
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

        // A renderer fatal wakes a blocked producer and stops further acquisition (Q9=I1).
        _consumer.Ring.Stop();
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

        if (_clearLabel != 0 && _instance is { } instance)
        {
            try
            {
                instance.DestroyDebugLabel(_clearLabel);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan debug label destruction failed.");
            }
            finally
            {
                _clearLabel = 0;
            }
        }

        // Every frame fence was observed and the device is idle: every held snapshot slot returns
        // to Free before the device is destroyed.
        ReleaseHeldSnapshots();

        _target?.Dispose();
        _target = null;

        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.Dispose();
            _frames[index] = null;
        }

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
                + "validation={Validation}, framesPresented={FramesPresented}, "
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
                + "swapchainRecreations={SwapchainRecreations}, "
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
