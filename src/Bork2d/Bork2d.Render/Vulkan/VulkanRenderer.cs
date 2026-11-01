using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Sprites;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The production windowed Vulkan renderer: one render thread owns the device, swapchain, frames,
/// the snapshot ring, and the dynamic-rendering clear, and records exactly one GPU frame per
/// consumed snapshot until the host stops it (Q7=G1, D-M6-06).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> creates one background thread and returns immediately. The thread selects a
/// present-capable device, creates the swapchain and two frames in flight, then marks the renderer
/// ready. The host observes readiness, status, counters, and the one-shot fatal error through
/// <see cref="IRenderer"/>; it drives the snapshot handoff through the slot API in M7 WP-4.
/// </para>
/// <para>
/// The renderer keeps only the swapchain driver: surface/present selection, acquire/present,
/// resize/recreation, single surface recovery, and the minimized drain. Thread lifecycle, readiness,
/// counters, snapshot handoff, captures, and texture setup/teardown come from the shared
/// <see cref="RendererLifecycle"/>, <see cref="RendererCaptureService"/>, and
/// <see cref="RendererTextures"/> compositions.
/// </para>
/// <para>
/// With no <c>Ready</c> snapshot the render thread blocks on the combined snapshot/control wait; it
/// never repeats or re-presents the previous snapshot. A snapshot slot is held until the consuming
/// GPU frame's fence is observed and is released when that frame is reused or during the
/// stop/fatal/idle drain. While minimized or at zero extent the suspended consumer frees the newest
/// snapshot without GPU work and the thread waits for control or a snapshot (Q8=H1).
/// </para>
/// <para>
/// Steady-state frames allocate nothing, log nothing, and never call <c>vkDeviceWaitIdle</c>.
/// Recreation and shutdown are the only places <c>vkDeviceWaitIdle</c> is allowed (D-M6-15). The
/// renderer never calls SDL and never destroys the host-owned instance or surface.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanRenderer : IRenderer
{
    private const int FramesInFlight = 2;

    private readonly RendererStartInfo _startInfo;
    private readonly ILogger _logger;
    private readonly VulkanFrameSet<WindowedFrame> _frames;
    private readonly RendererCaptureService _captures;
    private readonly RendererLifecycle _lifecycle;

    private RendererTextures? _textures;
    private VulkanDevice? _device;
    private WindowedSwapchain? _swapchain;
    private RenderPassRegistry? _passes;
    private KhrSurface? _surfaceApi;
    private KhrSwapchain? _swapchainApi;
    private IVulkanInstance? _instance;
    private nint _surfaceHandle;
    private nint _passLabel;
    private int _frameIndex;
    private Result _lastResult = Result.Success;
    private WindowExtent _requestedExtent;
    private bool _fullscreen;
    private bool _fullscreenChanged;
    private bool _minimized;
    private bool _minimizedLogged;
    private bool _surfaceRecoveryAttempted;

    /// <summary>Initializes a new instance of the <see cref="VulkanRenderer"/> class.</summary>
    /// <param name="startInfo">The validated windowed start request.</param>
    /// <exception cref="Bork2dException">The start request has no asset seam.</exception>
    internal VulkanRenderer(RendererStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (startInfo.Assets is null)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires the app's IAssets seam so the renderer can "
                    + "prefetch texture descriptors and payloads (M8 §6.5).");
        }

        _startInfo = startInfo;
        _logger = startInfo.LoggerFactory.CreateLogger(RenderLog.Category);
        _requestedExtent = new WindowExtent(startInfo.Width, startInfo.Height);
        _frames = new VulkanFrameSet<WindowedFrame>("Vulkan frame fence", _logger);
        _captures = new RendererCaptureService(_frames, nameof(VulkanRenderer), "windowed");
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

    /// <summary>
    /// Gets a value indicating whether the instance the windowed renderer was started with has the
    /// validation layer enabled (host-level GPU tests gate their validation evidence on it).
    /// </summary>
    internal bool ValidationEnabled => _instance?.ValidationEnabled == true;

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
    public void PostWindowExtent(WindowExtent extent) => _lifecycle.Control.PostExtent(extent);

    /// <inheritdoc/>
    public void PostFullscreen(bool fullscreen) => _lifecycle.Control.PostFullscreen(fullscreen);

    /// <inheritdoc/>
    public void PostMinimized(bool minimized) => _lifecycle.Control.PostMinimized(minimized);

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

    /// <summary>Runs on the render thread: creates the windowed Vulkan objects and marks readiness.</summary>
    /// <returns><see langword="false"/> when a stop was requested before readiness.</returns>
    private bool Startup()
    {
        IVulkanInstance instance = _startInfo.Instance
            ?? throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-null Vulkan instance.");
        _instance = instance;
        if (_startInfo.SurfaceHandle == 0)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-zero surface handle.");
        }

        _surfaceHandle = _startInfo.SurfaceHandle;
        if (_lifecycle.Control.IsStopRequested)
        {
            return false;
        }

        // D-M6-06: Start returned long ago; selection and swapchain creation run here.
        _device = VulkanDeviceSelector.Select(
            instance,
            requirePresent: true,
            _surfaceHandle,
            _logger);
        if (_lifecycle.Control.IsStopRequested)
        {
            return false;
        }

        _surfaceApi = LoadSurfaceExtension();
        _swapchainApi = LoadSwapchainExtension();
        _textures = new RendererTextures(
            _device,
            instance,
            _startInfo.Assets,
            _logger,
            _frames,
            "windowed");
        CreateSwapchain(reason: null);
        if (_lifecycle.Control.IsStopRequested)
        {
            return false;
        }

        CreateFrames();
        _textures.CreatePageStore();
        _passes = new RenderPassRegistry(
            new SpriteRenderPass(_textures.Pipeline!, _textures.Pages!, _logger));
        _passLabel = instance.CreateDebugLabel("Bork2d windowed passes");
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
        while (true)
        {
            _lifecycle.ObserveValidation(_instance);
            if (_lifecycle.IsStopping)
            {
                return;
            }

            DrainControlMessages();
            if (_lifecycle.IsStopping)
            {
                return;
            }

            // Complete captures from their own frame fences before waiting for a new snapshot or
            // draining a minimized frame: a submitted capture must not depend on the game
            // publishing another snapshot.
            _captures.RetireCompletedCaptures(_device!);

            if (_minimized || _requestedExtent.IsZeroOrMinimized)
            {
                if (DrainWhileMinimized())
                {
                    continue;
                }

                if (_lifecycle.Wait.Wait() == SnapshotWaitOutcome.Stopped)
                {
                    return;
                }

                continue;
            }

            _minimizedLogged = false;
            if (_fullscreenChanged)
            {
                _fullscreenChanged = false;
                Recreate(SwapchainRecreateReason.Fullscreen);
                continue;
            }

            WindowedSwapchain swapchain = _swapchain!;
            SwapchainAction action = SwapchainPolicy.DecideFrame(
                ToExtent(_requestedExtent),
                swapchain.Extent,
                _lastResult,
                _surfaceRecoveryAttempted);
            switch (action)
            {
                case SwapchainAction.SkipMinimized:
                    if (DrainWhileMinimized())
                    {
                        continue;
                    }

                    if (_lifecycle.Wait.Wait() == SnapshotWaitOutcome.Stopped)
                    {
                        return;
                    }

                    break;

                case SwapchainAction.Fatal:
                    throw CreateFatalForResult(_lastResult);

                case SwapchainAction.RecoverSurface:
                    RecoverSurface();
                    break;

                case SwapchainAction.RecreateAndRetry:
                    Recreate(ReasonForRecreate());
                    break;

                case SwapchainAction.PresentThenRecreate:
                    Recreate(SwapchainRecreateReason.Suboptimal);
                    break;

                case SwapchainAction.Present:
                    // Q7=G1: with no consumed snapshot there is no GPU frame; wait, never repeat.
                    if (!_lifecycle.Consumer.HasReadySnapshot)
                    {
                        if (_lifecycle.Wait.Wait() == SnapshotWaitOutcome.Stopped)
                        {
                            return;
                        }

                        break;
                    }

                    PresentFrame();
                    break;

                default:
                    throw new Bork2dException(
                        $"The windowed renderer received unexpected swapchain action {action}.");
            }
        }
    }

    /// <summary>
    /// Drains the newest Ready snapshot without GPU work while minimized or at zero extent
    /// (Q8=H1). Returns <see langword="true"/> when a snapshot was dropped.
    /// </summary>
    private bool DrainWhileMinimized()
    {
        if (!_minimizedLogged)
        {
            _logger.LogDebug(
                "Vulkan renderer minimized: reason=Minimize, extent={Width}x{Height}; "
                    + "draining snapshots without GPU work.",
                _requestedExtent.Width,
                _requestedExtent.Height);
            _minimizedLogged = true;
        }

        if (!_lifecycle.Consumer.DrainSuspended())
        {
            return false;
        }

        _lifecycle.SignalProgress();
        return true;
    }

    private void DrainControlMessages()
    {
        while (_lifecycle.Control.TryDequeue(out RendererControlMessage message))
        {
            switch (message.MessageKind)
            {
                case RendererControlMessage.Kind.Extent:
                    _requestedExtent = message.Extent;
                    break;

                case RendererControlMessage.Kind.Fullscreen:
                    if (_fullscreen != message.Flag)
                    {
                        _fullscreen = message.Flag;
                        _fullscreenChanged = true;
                    }

                    break;

                case RendererControlMessage.Kind.Minimized:
                    _minimized = message.Flag;
                    break;

                default:
                    break;
            }
        }
    }

    private void PresentFrame()
    {
        VulkanDevice device = _device!;
        WindowedSwapchain swapchain = _swapchain!;
        WindowedFrame frame = _frames[_frameIndex]!;
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

        // The frame fence has signaled: the frame's instance buffer is free to grow, its recorded
        // capture (if any) can complete, and the snapshot the frame held is retired before the
        // newest Ready snapshot is taken. With no snapshot there is no GPU frame.
        _captures.CompleteFrame(frame);
        SnapshotSlot? slot = _lifecycle.Consumer.TakeForFreeFrame(_frameIndex);
        if (slot is null)
        {
            return;
        }

        // Advance the texture-page state machine once per frame, before recording: completed
        // descriptor/payload loads prepare uploads, completed uploads write their bindless
        // descriptor, and a capacity growth recreates the frame sets while the device is idle.
        _textures!.Pump();

        uint imageIndex = 0;
        Result acquire = _swapchainApi!.AcquireNextImage(
            device.Device,
            swapchain.Handle,
            ulong.MaxValue,
            frame.ImageAvailable,
            default,
            ref imageIndex);
        if (acquire == Result.ErrorDeviceLost)
        {
            _lifecycle.Consumer.ReleaseTaken(slot);
            throw CreateFatalForResult(acquire);
        }

        if (acquire is Result.ErrorOutOfDateKhr or Result.ErrorSurfaceLostKhr)
        {
            // The snapshot never became a GPU frame: free it immediately (never wait a fence).
            _lifecycle.Consumer.ReleaseTaken(slot);
            _lastResult = acquire;
            return;
        }

        if (acquire != Result.Success && acquire != Result.SuboptimalKhr)
        {
            _lifecycle.Consumer.ReleaseTaken(slot);
            throw new Bork2dException(
                $"vkAcquireNextImageKHR failed with VkResult {acquire}.");
        }

        _lifecycle.IncrementLastFrameNumber();

        Result resetFences = device.Api.ResetFences(device.Device, 1, &fence);
        if (resetFences != Result.Success)
        {
            throw new Bork2dException($"vkResetFences failed with VkResult {resetFences}.");
        }

        Result resetCommandBuffer = device.Api.ResetCommandBuffer(
            frame.CommandBuffer,
            CommandBufferResetFlags.None);
        if (resetCommandBuffer != Result.Success)
        {
            throw new Bork2dException(
                $"vkResetCommandBuffer failed with VkResult {resetCommandBuffer}.");
        }

        _passes!.Plan(
            slot.Snapshot,
            (int)swapchain.Extent.Width,
            (int)swapchain.Extent.Height,
            frame.InstanceResources);
        RecordFrame(frame, imageIndex, swapchain, device);
        Submit(frame, imageIndex, swapchain, device);
        frame.InstanceResources.MarkSubmitted();
        frame.Capture.MarkSubmitted();
        _lifecycle.Consumer.AttachToFrame(_frameIndex, slot);

        Result present = Present(imageIndex, device, swapchain);
        _lifecycle.SetLastPresentResult((int)present);
        _lifecycle.PublishStats(
            slot.Snapshot.Sequence,
            (int)swapchain.Extent.Width,
            (int)swapchain.Extent.Height,
            _passes.Stats,
            _textures.Pages?.TexturePageCount ?? 0,
            _textures.Pages?.TexturePageCapacityInEntries ?? 0);
        if (present is Result.Success or Result.SuboptimalKhr)
        {
            _lifecycle.IncrementFramesPresented();
            _lifecycle.SignalProgress();
        }

        if (present == Result.ErrorDeviceLost)
        {
            throw CreateFatalForResult(present);
        }

        if (present is Result.ErrorSurfaceLostKhr or Result.ErrorOutOfDateKhr)
        {
            _lastResult = present;
        }
        else if (present == Result.SuboptimalKhr || acquire == Result.SuboptimalKhr)
        {
            _lastResult = Result.SuboptimalKhr;
        }
        else if (present != Result.Success)
        {
            throw new Bork2dException($"vkQueuePresentKHR failed with VkResult {present}.");
        }
        else
        {
            _lastResult = Result.Success;
        }

        _frameIndex = (_frameIndex + 1) % FramesInFlight;
    }

    private void RecordFrame(
        WindowedFrame frame,
        uint imageIndex,
        WindowedSwapchain swapchain,
        VulkanDevice device)
    {
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

        // A newly acquired image is UNDEFINED; the frame clear and every camera pass run in
        // COLOR_ATTACHMENT_OPTIMAL.
        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        var toColor = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.None,
            SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.ColorAttachmentOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = swapchain.GetImage(imageIndex),
            SubresourceRange = range,
        };
        var dependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &toColor,
        };
        device.Api.CmdPipelineBarrier2(commandBuffer, &dependency);

        _instance!.BeginCachedCommandBufferLabel(
            device.Device.Handle,
            commandBuffer.Handle,
            _passLabel);

        // A newly acquired image is UNDEFINED and a camera may load instead of clearing, so one
        // full-target opaque-black clear defines every pixel before any camera pass runs.
        SpritePassRecorder.RecordFrameClear(
            device.Api,
            commandBuffer,
            swapchain.GetView(imageIndex),
            swapchain.Extent);

        bool capture = _captures.BeginRecord(
            frame,
            (int)swapchain.Extent.Width,
            (int)swapchain.Extent.Height,
            swapchain.Format);
        _passes!.Record(
            new RenderPassRecordContext(
                device.Api,
                commandBuffer,
                swapchain.GetView(imageIndex),
                swapchain.GetImage(imageIndex),
                frame.InstanceResources.DescriptorSet,
                swapchain.Format));

        if (capture)
        {
            // The capture copy includes every camera clear/draw and ends in PRESENT_SRC_KHR.
            FrameCaptureRecorder.RecordCopy(
                device.Api,
                commandBuffer,
                swapchain.GetImage(imageIndex),
                frame.Capture.StagingBuffer,
                (int)swapchain.Extent.Width,
                (int)swapchain.Extent.Height,
                toPresentSrc: true);
            frame.Capture.MarkRecorded();
        }
        else
        {
            FrameCaptureRecorder.RecordPresentTransition(
                device.Api,
                commandBuffer,
                swapchain.GetImage(imageIndex));
        }

        _instance.EndCommandBufferLabel(device.Device.Handle, commandBuffer.Handle);

        Result end = device.Api.EndCommandBuffer(commandBuffer);
        if (end != Result.Success)
        {
            throw new Bork2dException($"vkEndCommandBuffer failed with VkResult {end}.");
        }
    }

    private void Submit(
        WindowedFrame frame,
        uint imageIndex,
        WindowedSwapchain swapchain,
        VulkanDevice device)
    {
        var commandBufferInfo = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = frame.CommandBuffer,
        };
        var waitInfo = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = frame.ImageAvailable,
            StageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
        };
        var signalInfo = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = swapchain.GetRenderFinished(imageIndex),
            StageMask = PipelineStageFlags2.AllCommandsBit,
        };
        var submitInfo = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            WaitSemaphoreInfoCount = 1,
            PWaitSemaphoreInfos = &waitInfo,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &commandBufferInfo,
            SignalSemaphoreInfoCount = 1,
            PSignalSemaphoreInfos = &signalInfo,
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

    private Result Present(
        uint imageIndex,
        VulkanDevice device,
        WindowedSwapchain swapchain)
    {
        Semaphore renderFinished = swapchain.GetRenderFinished(imageIndex);
        SwapchainKHR swapchainHandle = swapchain.Handle;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderFinished,
            SwapchainCount = 1,
            PSwapchains = &swapchainHandle,
            PImageIndices = &imageIndex,
            PResults = null,
        };
        return _swapchainApi!.QueuePresent(device.Queue, &presentInfo);
    }

    private void CreateSwapchain(SwapchainRecreateReason? reason)
    {
        WindowedSwapchain? old = _swapchain;
        Extent2D oldExtent = old?.Extent ?? default;
        WindowedSwapchain created = WindowedSwapchain.Create(
            _device!,
            _surfaceApi!,
            _swapchainApi!,
            _surfaceHandle,
            _requestedExtent,
            _startInfo.VSync,
            old?.Handle ?? default,
            oldExtent,
            reason,
            _instance!,
            _logger);

        _swapchain = created;

        // A surface-defined CurrentExtent is the authority: WindowedSwapchain.ResolveExtent may
        // ignore the posted hint. Sync the request to what the swapchain actually got so
        // SwapchainPolicy.DecideFrame cannot see a permanent mismatch and recreate forever. Later
        // platform posts update _requestedExtent and trigger the next recreation normally.
        _requestedExtent = new WindowExtent((int)created.Extent.Width, (int)created.Extent.Height);

        old?.Dispose();

        // The sprite pipeline needs the final swapchain color format; a recreation that selects a
        // different format creates that format's pipeline lazily. Creation failure is fatal and the
        // new swapchain is already the current one, so cleanup owns it.
        _textures!.EnsurePipeline(created.Format);

        _lastResult = Result.Success;
        if (reason is not null)
        {
            _lifecycle.IncrementSwapchainRecreations();
            _lifecycle.SignalProgress();
        }
    }

    private void Recreate(SwapchainRecreateReason reason)
    {
        VulkanDeviceIdle.Wait(_device!, "swapchain recreation");

        // vkDeviceWaitIdle completed every in-flight fence: their snapshot slots are free.
        _lifecycle.ReleaseHeldSnapshots("swapchain recreation");
        CreateSwapchain(reason);
        _lastResult = Result.Success;
    }

    private void RecoverSurface()
    {
        if (_surfaceRecoveryAttempted)
        {
            throw new Bork2dException(
                "Vulkan surface was lost a second time; recovery is limited to one attempt "
                    + "(D-M6-16).");
        }

        _surfaceRecoveryAttempted = true;
        Result lostResult = _lastResult;
        VulkanDeviceIdle.Wait(_device!, "surface recovery");
        _lifecycle.ReleaseHeldSnapshots("surface recovery");
        _swapchain?.Dispose();
        _swapchain = null;

        Func<nint>? createReplacement = _startInfo.CreateReplacementSurface;
        if (createReplacement is null)
        {
            throw new Bork2dException(
                "Vulkan surface was lost, but no replacement-surface callback was provided.");
        }

        nint replacement;
        try
        {
            replacement = createReplacement();
        }
        catch (Exception exception)
        {
            throw new Bork2dException(
                "The Vulkan replacement surface could not be created.",
                exception);
        }

        if (replacement == 0)
        {
            throw new Bork2dException(
                "The Vulkan replacement-surface callback returned a zero surface handle.");
        }

        _surfaceHandle = replacement;
        CreateSwapchain(SwapchainRecreateReason.SurfaceLost);
        _logger.LogWarning(
            "Vulkan surface lost and recovered: result={Result}, attempt={Attempt}, "
                + "newExtent={Width}x{Height}, presentMode={PresentMode}.",
            lostResult,
            1,
            _swapchain!.Extent.Width,
            _swapchain.Extent.Height,
            _swapchain.PresentMode);
    }

    private void CreateFrames()
    {
        VulkanDevice device = _device!;
        SpritePipeline pipeline = _textures?.Pipeline
            ?? throw new Bork2dException(
                "The windowed renderer cannot create frames without the sprite pipeline.");
        var frames = new WindowedFrame?[FramesInFlight];

        // Attach before creating: a mid-loop failure must leave the already-created frames visible
        // to cleanup, exactly like the per-renderer frame array did.
        _frames.Attach(frames);
        for (var index = 0; index < FramesInFlight; index++)
        {
            WindowedFrame frame = WindowedFrame.Create(
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
                $"Bork2d frame {index} command buffer");
        }
    }

    private KhrSurface LoadSurfaceExtension()
    {
        VulkanDevice device = _device!;
        if (!device.Api.TryGetInstanceExtension(device.Instance, out KhrSurface surfaceApi))
        {
            throw new Bork2dException(
                $"Windowed rendering requires {VulkanNames.SurfaceExtension}, but it is not "
                    + "available on the instance.");
        }

        return surfaceApi;
    }

    private KhrSwapchain LoadSwapchainExtension()
    {
        VulkanDevice device = _device!;
        if (!device.Api.TryGetDeviceExtension(
            device.Instance,
            device.Device,
            out KhrSwapchain swapchainApi))
        {
            throw new Bork2dException(
                $"Windowed rendering requires {VulkanNames.SwapchainExtension}, but it is not "
                    + "available on the device.");
        }

        return swapchainApi;
    }

    private SwapchainRecreateReason ReasonForRecreate()
    {
        if (_lastResult == Result.ErrorOutOfDateKhr)
        {
            return SwapchainRecreateReason.OutOfDate;
        }

        if (_lastResult == Result.SuboptimalKhr)
        {
            return SwapchainRecreateReason.Suboptimal;
        }

        return SwapchainRecreateReason.Resize;
    }

    private static Extent2D ToExtent(WindowExtent extent)
        => new((uint)Math.Max(0, extent.Width), (uint)Math.Max(0, extent.Height));

    private Bork2dException CreateFatalForResult(Result result)
    {
        if (result == Result.ErrorDeviceLost)
        {
            return new Bork2dException(
                "Vulkan device lost (VK_ERROR_DEVICE_LOST); device loss is fatal in M6.");
        }

        if (result == Result.ErrorSurfaceLostKhr && _surfaceRecoveryAttempted)
        {
            return new Bork2dException(
                "Vulkan surface was lost a second time; recovery is limited to one attempt "
                    + "(D-M6-16).");
        }

        return new Bork2dException(
            $"The windowed renderer received an unexpected VkResult {result}.");
    }

    private void PublishFatal(Exception exception)
    {
        var fatal = exception as Bork2dException
            ?? new Bork2dException(
                "The Vulkan renderer failed: " + exception.Message,
                exception);

        _lifecycle.PublishFatal(fatal);
        _logger.LogError(fatal, "Vulkan renderer fatal failure.");
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
                        "Vulkan device idle wait returned {Result} during shutdown.",
                        idle);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan device idle wait failed during shutdown.");
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

        // Every frame fence was observed and the device is idle: no GPU frame references a
        // snapshot slot any more, so every held slot returns to Free before teardown. Capture
        // requests recorded on those frames complete here; queued ones fault.
        _lifecycle.ReleaseHeldSnapshots("shutdown");

        _captures.CompleteAll();
        _captures.CloseForStop();

        _textures?.CaptureStopCounters();
        _frames.CaptureStopCounters();

        _swapchain?.Dispose();
        _swapchain = null;

        // Frame descriptor sets/pools are destroyed before the page images/views they reference,
        // and the pages before the pipeline that owns their samplers and the device itself.
        _frames.DisposeFrames();
        _textures?.Destroy();

        _device?.Destroy();
        _device = null;
        _surfaceApi = null;
        _swapchainApi = null;
    }

    private void LogStarted()
    {
        WindowedSwapchain? swapchain = _swapchain;
        if (swapchain is null)
        {
            return;
        }

        TexturePageStore? pages = _textures?.Pages;
        RendererCounters counters = _lifecycle.Counters;
        _logger.LogInformation(
            "Vulkan renderer started: mode={Mode}, extent={Width}x{Height}, format={Format}, "
                + "colorSpace={ColorSpace}, presentMode={PresentMode}, images={ImageCount}, "
                + "framesInFlight={FramesInFlight}, handoff={Handoff}, ringDepth={RingDepth}, "
                + "oneFramePerSnapshot={OneFramePerSnapshot}, instanceResources={InstanceResources}, "
                + "instanceCapacity={InstanceCapacity}, texturePages={TexturePages}, "
                + "texturePageCapacity={TexturePageCapacity}, bindlessLimit={BindlessLimit}, "
                + "placeholder={Placeholder}, validation={Validation}, "
                + "framesPresented={FramesPresented}, "
                + "framesDiscarded={FramesDiscarded}, swapchainRecreations={SwapchainRecreations}, "
                + "validationErrors={ValidationErrors}, validationWarnings={ValidationWarnings}, "
                + "stopReason={StopReason}.",
            nameof(RendererMode.Windowed),
            swapchain.Extent.Width,
            swapchain.Extent.Height,
            swapchain.Format,
            swapchain.ColorSpace,
            swapchain.PresentMode,
            swapchain.ImageCount,
            FramesInFlight,
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
            counters.FramesPresented,
            0,
            counters.SwapchainRecreations,
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
            nameof(RendererMode.Windowed),
            _lifecycle.StopReason,
            snapshots.Published,
            snapshots.Consumed,
            snapshots.Superseded,
            snapshots.BackpressureWaits,
            snapshots.MaxLive,
            _lifecycle.SuspendedConsumptions,
            counters.FramesPresented,
            0,
            counters.SwapchainRecreations,
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
            counters.LastPresentResult);
    }
}
