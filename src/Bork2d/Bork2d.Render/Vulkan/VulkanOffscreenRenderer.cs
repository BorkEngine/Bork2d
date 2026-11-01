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
/// The renderer keeps only the fixed-target driver: instance ownership, the permanent sRGB target,
/// its one-time layout transition, and the discard path. Thread lifecycle, readiness, counters,
/// snapshot handoff, captures, and texture setup/teardown come from the shared
/// <see cref="RendererLifecycle"/>, <see cref="RendererCaptureService"/>, and
/// <see cref="RendererTextures"/> compositions.
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
    private const string PassLabelText = "Bork2d offscreen passes";
    private const ulong CleanupFenceTimeoutNanoseconds = 1_000_000_000;

    private readonly RendererStartInfo _startInfo;
    private readonly IVulkanInstanceFactory _instanceFactory;
    private readonly ILogger _logger;
    private readonly VulkanFrameSet<OffscreenFrame> _frames;
    private readonly RendererCaptureService _captures;
    private readonly RendererLifecycle _lifecycle;

    private RendererTextures? _textures;
    private OffscreenTarget? _target;
    private RenderPassRegistry? _passes;
    private VulkanDevice? _device;
    private IVulkanInstance? _instance;
    private bool _ownsInstance;
    private nint _passLabel;
    private int _frameIndex;
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
        _frames = new VulkanFrameSet<OffscreenFrame>("Vulkan offscreen frame fence", _logger);
        _captures = new RendererCaptureService(
            _frames,
            nameof(VulkanOffscreenRenderer),
            "offscreen");
        _lifecycle = new RendererLifecycle(_logger, _captures, FramesInFlight);
    }

    /// <inheritdoc/>
    public bool IsReady => _lifecycle.IsReady;

    /// <inheritdoc/>
    public RendererStatus Status => _lifecycle.Status;

    /// <inheritdoc/>
    public RendererStopReason StopReason => _lifecycle.StopReason;

    /// <inheritdoc/>
    public RendererCounters Counters => _lifecycle.Counters;

    /// <inheritdoc/>
    public RenderStats RenderStats => _lifecycle.RenderStats;

    /// <inheritdoc/>
    public Exception? FatalException => _lifecycle.FatalException;

    /// <inheritdoc/>
    public SnapshotRingCounters SnapshotCounters => _lifecycle.SnapshotCounters;

    /// <inheritdoc/>
    public long SuspendedConsumptions => _lifecycle.SuspendedConsumptions;

    /// <inheritdoc/>
    public Task<FrameCaptureResult> CaptureFrameAsync(CancellationToken cancellationToken = default)
        => _captures.Request(cancellationToken);

    /// <summary>Gets the renderer's snapshot ring (GPU tests observe slot state through it).</summary>
    internal SnapshotRing SnapshotRing => _lifecycle.Consumer.Ring;

    /// <summary>Gets a value indicating whether every GPU frame owns its empty instance resources.</summary>
    internal bool HasInstanceResources => _frames.HasInstanceResources;

    /// <summary>Gets a value indicating whether the sprite pipeline was created at startup.</summary>
    internal bool HasSpritePipeline => _textures?.Pipeline is not null;

    /// <summary>Gets the texture-page cache (GPU tests observe the placeholder and counts).</summary>
    internal TexturePageStore? PageStore => _textures?.Pages;

    /// <summary>Gets the largest instance-buffer capacity among the frames (GPU tests/diagnostics).</summary>
    internal int InstanceCapacityHighWater => _frames.InstanceCapacityHighWater;

    /// <summary>Gets the total number of instance-buffer growths across the frames.</summary>
    internal int InstanceGrowthCount => _frames.InstanceGrowthCount;

    /// <summary>Gets a value indicating whether the instance actually enabled validation.</summary>
    /// <remarks>GPU tests use it to require validation evidence (D-M6-21).</remarks>
    internal bool ValidationEnabled => _validationEnabled;

    /// <inheritdoc/>
    public SnapshotSlot? AcquireSnapshotSlot() => _lifecycle.AcquireSnapshotSlot();

    /// <inheritdoc/>
    public void PublishSnapshot(SnapshotSlot slot) => _lifecycle.PublishSnapshot(slot);

    /// <inheritdoc/>
    public void AbandonSnapshotSlot(SnapshotSlot slot) => _lifecycle.AbandonSnapshotSlot(slot);

    /// <inheritdoc/>
    public void WakeSnapshotWait() => _lifecycle.WakeSnapshotWait();

    /// <inheritdoc/>
    public void Start() => _lifecycle.Start(Startup, RunFrames, Cleanup, PublishFatal, LogStopped);

    /// <inheritdoc/>
    public void WaitForReady() => _lifecycle.WaitForReady();

    /// <inheritdoc/>
    public bool WaitForReady(TimeSpan timeout) => _lifecycle.WaitForReady(timeout);

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
    public void RequestStop() => _lifecycle.RequestStop();

    /// <inheritdoc/>
    public void Join() => _lifecycle.Join();

    /// <inheritdoc/>
    public bool Join(TimeSpan timeout) => _lifecycle.Join(timeout);

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for a renderer predicate (GPU tests only;
    /// event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForProgress(Func<bool> predicate, TimeSpan timeout)
        => _lifecycle.WaitForProgress(predicate, timeout);

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for the counters to satisfy a predicate
    /// (GPU tests only; event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The counter condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForProgress(Func<RendererCounters, bool> predicate, TimeSpan timeout)
        => _lifecycle.WaitForProgress(predicate, timeout);

    /// <summary>Runs on the render thread: creates the fixed target and marks readiness.</summary>
    /// <returns><see langword="false"/> when a stop was requested before readiness.</returns>
    private bool Startup()
    {
        IVulkanInstance instance;
        if (_startInfo.Instance is { } supplied)
        {
            instance = supplied;
        }
        else
        {
            instance = _instanceFactory.Create(
                new VulkanInstanceRequest
                {
                    RequiredExtensions = [],
                    ValidationRequested = _startInfo.ValidationRequested,
                    EngineName = "Bork2d",
                    LoggerFactory = _startInfo.LoggerFactory,
                });
            _ownsInstance = true;
        }

        _instance = instance;
        if (instance.Handle == 0)
        {
            throw new Bork2dException(
                "Offscreen Vulkan rendering requires a non-zero Vulkan instance handle.");
        }

        _validationEnabled = instance.ValidationEnabled;

        if (_lifecycle.Control.IsStopRequested)
        {
            return false;
        }

        _device = VulkanDeviceSelector.Select(
            instance,
            requirePresent: false,
            surfaceHandle: 0,
            _logger);
        if (_lifecycle.Control.IsStopRequested)
        {
            return false;
        }

        WindowExtent extent = ValidateExtent(_device);
        Format format = SelectFormat(_device);
        _target = OffscreenTarget.Create(
            _device,
            extent,
            format,
            ColorImages,
            instance,
            _logger);
        _textures = new RendererTextures(
            _device,
            instance,
            _startInfo.Assets,
            _logger,
            _frames,
            "offscreen",
            _startInfo.AssetCatalogVersion,
            _startInfo.TextureUploadBudgetBytes,
            _startInfo.TextureResidencyBudgetBytes);
        _textures.EnsurePipeline(format);
        if (_lifecycle.Control.IsStopRequested)
        {
            return false;
        }

        CreateFrames();
        _textures.CreatePageStore();
        _passes = new RenderPassRegistry(
            new SpriteRenderPass(_textures.Pipeline!, _textures.Pages!, _logger));
        TransitionTargets();
        _passLabel = instance.CreateDebugLabel(PassLabelText);
        _lifecycle.UpdateValidationCounters(instance);
        if (_lifecycle.Control.IsStopRequested)
        {
            return false;
        }

        _lifecycle.MarkReady();
        LogStarted();
        return true;
    }

    private void RunFrames()
    {
        VulkanDevice device = _device!;
        while (true)
        {
            _lifecycle.ObserveValidation(_instance);
            if (_lifecycle.IsStopping)
            {
                return;
            }

            // Complete captures from their own frame fences before waiting for a new snapshot: a
            // submitted capture must not depend on the game publishing another snapshot.
            _captures.RetireCompletedCaptures(device);

            // Q7=G1: one GPU frame per consumed snapshot; with none, wait without repeating.
            if (!_lifecycle.Consumer.HasReadySnapshot)
            {
                if (_lifecycle.Wait.Wait() == SnapshotWaitOutcome.Stopped)
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

            if (_lifecycle.IsStopping)
            {
                return;
            }

            // The frame fence has signaled: the frame's instance buffer is free to grow, its
            // recorded capture (if any) can complete from the staging bytes, and the slot it held is
            // retired before the newest Ready is taken for this frame. A null result means another
            // consumer took it; no frame.
            _captures.CompleteFrame(frame);
            SnapshotSlot? slot = _lifecycle.Consumer.TakeForFreeFrame(_frameIndex);
            if (slot is null)
            {
                continue;
            }

            // Advance the texture-page state machine once per frame, before recording.
            _textures!.Pump();

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
            _passes!.Plan(
                slot.Snapshot,
                (int)target.Extent.Width,
                (int)target.Extent.Height,
                frame.InstanceResources);
            RecordFrame(frame, _frameIndex);
            Submit(frame);
            frame.InstanceResources.MarkSubmitted();
            frame.Capture.MarkSubmitted();
            _lifecycle.Consumer.AttachToFrame(_frameIndex, slot);
            _lifecycle.IncrementLastFrameNumber();
            _lifecycle.IncrementFramesDiscarded();
            _lifecycle.PublishStats(
                slot.Snapshot.Sequence,
                (int)target.Extent.Width,
                (int)target.Extent.Height,
                _passes.Stats,
                _textures.Pages?.TexturePageCount ?? 0,
                _textures.Pages?.TexturePageCapacityInEntries ?? 0,
                _textures.Pages?.PendingUploadCount ?? 0,
                _textures.Pages?.PendingUploadBytes ?? 0);
            _lifecycle.SignalProgress();
            _frameIndex = (_frameIndex + 1) % FramesInFlight;
        }
    }

    private void RecordFrame(OffscreenFrame frame, int imageIndex)
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

        bool capture = _captures.BeginRecord(
            frame,
            (int)target.Extent.Width,
            (int)target.Extent.Height,
            target.Format);
        _passes!.Record(
            new RenderPassRecordContext(
                device.Api,
                commandBuffer,
                target.GetView(imageIndex),
                target.GetImage(imageIndex),
                frame.InstanceResources.DescriptorSet,
                target.Format));

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
        SpritePipeline pipeline = _textures?.Pipeline
            ?? throw new Bork2dException(
                "The offscreen renderer cannot create frames without the sprite pipeline.");
        var frames = new OffscreenFrame?[FramesInFlight];

        // Attach before creating: a mid-loop failure must leave the already-created frames visible
        // to cleanup, exactly like the per-renderer frame array did.
        _frames.Attach(frames);
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
            frames[index] = frame;
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

    private void PublishFatal(Exception exception)
    {
        var fatal = exception as Bork2dException
            ?? new Bork2dException(
                "The Vulkan offscreen renderer failed: " + exception.Message,
                exception);

        _lifecycle.PublishFatal(fatal);
        _logger.LogError(fatal, "Vulkan offscreen renderer fatal failure.");
    }

    private void Cleanup()
    {
        _lifecycle.UpdateValidationCounters(_instance);
        VulkanDevice? device = _device;
        if (device is not null)
        {
            _frames.WaitForFences(device);
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
        _lifecycle.ReleaseHeldSnapshots("shutdown");

        _captures.CompleteAll();
        _captures.CloseForStop();

        _textures?.CaptureStopCounters();
        _frames.CaptureStopCounters();

        _target?.Dispose();
        _target = null;

        // Frame descriptor sets/pools are destroyed before the page images/views they reference,
        // and the pages before the pipeline that owns their samplers and the device itself.
        _frames.DisposeFrames();
        _textures?.Destroy();

        _device?.Destroy();
        _device = null;

        if (_ownsInstance)
        {
            _instance?.Destroy();
        }

        _instance = null;
    }

    private void LogStarted()
    {
        OffscreenTarget? target = _target;
        if (target is null)
        {
            return;
        }

        TexturePageStore? pages = _textures?.Pages;
        RendererCounters counters = _lifecycle.Counters;
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
            pages?.TexturePageCount ?? 0,
            pages?.TexturePageCapacityInEntries ?? 0,
            _textures?.MaxTexturePages ?? 0,
            pages?.IsPlaceholderReady == true ? "on" : "off",
            _instance?.ValidationEnabled == true ? "on" : "off",
            0,
            counters.FramesDiscarded,
            0,
            _lifecycle.ValidationErrorCount,
            _lifecycle.ValidationWarningCount,
            _lifecycle.StopReason);
    }

    private void LogStopped()
    {
        RendererCounters counters = _lifecycle.Counters;
        SnapshotRingCounters snapshots = _lifecycle.SnapshotCounters;
        RenderStats stats = _lifecycle.RenderStats;
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
            _lifecycle.StopReason,
            snapshots.Published,
            snapshots.Consumed,
            snapshots.Superseded,
            snapshots.BackpressureWaits,
            snapshots.MaxLive,
            _lifecycle.SuspendedConsumptions,
            0,
            counters.FramesDiscarded,
            0,
            _textures?.StopLogPageCount ?? 0,
            _textures?.StopLogPageCapacity ?? 0,
            _textures?.StopLogPageGrowths ?? 0,
            _textures?.StopLogFailedPaths ?? 0,
            _frames.StopLogInstanceCapacity,
            _frames.StopLogInstanceGrowths,
            stats.DrawCalls,
            stats.Instances,
            stats.Cameras,
            stats.NonEmptyCameras,
            stats.SkippedSprites,
            _lifecycle.ValidationErrorCount,
            _lifecycle.ValidationWarningCount,
            counters.LastFrameNumber,
            0);
    }
}
