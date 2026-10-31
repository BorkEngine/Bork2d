using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Core;
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
internal sealed unsafe class VulkanRenderer : IRenderer, IBindlessPageWriter
{
    private const int FramesInFlight = 2;
    private const string ThreadName = "Bork2d.Render";
    private const ulong CleanupFenceTimeoutNanoseconds = 1_000_000_000;

    private readonly RendererStartInfo _startInfo;
    private readonly ILogger _logger;
    private readonly RendererControlChannel _control = new();
    private readonly SnapshotConsumer _consumer = new(new SnapshotRing(), FramesInFlight);
    private readonly SnapshotControlWait _wait;
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly ManualResetEventSlim _progress = new(initialState: false);
    private readonly RenderStatsBoard _stats = new();

    private WindowedFrame?[] _frames = [];
    private Thread? _thread;
    private VulkanDevice? _device;
    private WindowedSwapchain? _swapchain;
    private SpritePipeline? _spritePipeline;
    private TexturePageStore? _pages;
    private KhrSurface? _surfaceApi;
    private KhrSwapchain? _swapchainApi;
    private IVulkanInstance? _instance;
    private nint _surfaceHandle;
    private nint _clearLabel;
    private int _frameIndex;
    private int _started;
    private int _status = (int)RendererStatus.NotStarted;
    private int _stopReason = (int)RendererStopReason.None;
    private int _readyFlag;
    private long _framesPresented;
    private long _swapchainRecreations;
    private long _lastFrameNumber;
    private int _lastPresentResult;
    private int _validationErrorCount;
    private int _validationWarningCount;
    private int _loggedPageCount;
    private int _loggedPageCapacity;
    private int _loggedPageGrowths;
    private int _loggedFailedPaths;
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
        FramesPresented: Volatile.Read(ref _framesPresented),
        FramesDiscarded: 0,
        SwapchainRecreations: Volatile.Read(ref _swapchainRecreations),
        ValidationErrorCount: Volatile.Read(ref _validationErrorCount),
        ValidationWarningCount: Volatile.Read(ref _validationWarningCount),
        LastFrameNumber: Volatile.Read(ref _lastFrameNumber),
        LastPresentResult: Volatile.Read(ref _lastPresentResult));

    /// <inheritdoc/>
    public RenderStats RenderStats => _stats.Current;

    /// <inheritdoc/>
    public Exception? FatalException => _control.FatalException;

    /// <inheritdoc/>
    public SnapshotRingCounters SnapshotCounters => _consumer.Counters;

    /// <inheritdoc/>
    public long SuspendedConsumptions => _consumer.SuspendedConsumptions;

    /// <summary>Gets the renderer's snapshot ring (GPU tests observe slot state through it).</summary>
    internal SnapshotRing SnapshotRing => _consumer.Ring;

    /// <summary>
    /// Gets a value indicating whether the instance the windowed renderer was started with has the
    /// validation layer enabled (host-level GPU tests gate their validation evidence on it).
    /// </summary>
    internal bool ValidationEnabled => _instance?.ValidationEnabled == true;

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

    /// <inheritdoc/>
    void IBindlessPageWriter.EnsureCapacity(int capacity)
    {
        // Growth happens between frames: the device is idle and every in-flight frame's fence has
        // signaled before the per-frame descriptor pools/sets are recreated.
        WaitForDeviceIdle("texture page capacity growth");

        for (var index = 0; index < _frames.Length; index++)
        {
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
    public void PostWindowExtent(WindowExtent extent) => _control.PostExtent(extent);

    /// <inheritdoc/>
    public void PostFullscreen(bool fullscreen) => _control.PostFullscreen(fullscreen);

    /// <inheritdoc/>
    public void PostMinimized(bool minimized) => _control.PostMinimized(minimized);

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
    /// Frees every non-<c>Writing</c> slot once the render thread has joined.
    /// </summary>
    /// <remarks>
    /// Join is the quiescent point (Q9=I1): a fatal mid-tick may publish after the renderer's
    /// cleanup, so the ring cannot be fully drained until the render thread is gone. A slot still
    /// <c>Writing</c> belongs to a producer and is never touched.
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
            _instance = _startInfo.Instance
                ?? throw new Bork2dException(
                    "Windowed Vulkan rendering requires a non-null Vulkan instance.");
            if (_startInfo.SurfaceHandle == 0)
            {
                throw new Bork2dException(
                    "Windowed Vulkan rendering requires a non-zero surface handle.");
            }

            _surfaceHandle = _startInfo.SurfaceHandle;
            if (_control.IsStopRequested)
            {
                SetStopReason(RendererStopReason.Requested);
                return;
            }

            // D-M6-06: Start returned long ago; selection and swapchain creation run here.
            _device = VulkanDeviceSelector.Select(
                _instance,
                requirePresent: true,
                _surfaceHandle,
                _logger);
            if (_control.IsStopRequested)
            {
                SetStopReason(RendererStopReason.Requested);
                return;
            }

            _surfaceApi = LoadSurfaceExtension();
            _swapchainApi = LoadSwapchainExtension();
            CreateSwapchain(reason: null);
            if (_control.IsStopRequested)
            {
                SetStopReason(RendererStopReason.Requested);
                return;
            }

            CreateFrames();
            CreatePageStore();
            _clearLabel = _instance.CreateDebugLabel("Bork2d clear pass");
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
        while (true)
        {
            ObserveValidation();
            if (IsStopping())
            {
                return;
            }

            DrainControlMessages();
            if (IsStopping())
            {
                return;
            }

            if (_minimized || _requestedExtent.IsZeroOrMinimized)
            {
                if (DrainWhileMinimized())
                {
                    continue;
                }

                if (_wait.Wait() == SnapshotWaitOutcome.Stopped)
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

                    if (_wait.Wait() == SnapshotWaitOutcome.Stopped)
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
                    if (!_consumer.HasReadySnapshot)
                    {
                        if (_wait.Wait() == SnapshotWaitOutcome.Stopped)
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

    private bool IsStopping() => _control.IsStopRequested || _consumer.IsStopped;

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

        if (!_consumer.DrainSuspended())
        {
            return false;
        }

        SignalProgress();
        return true;
    }

    private void DrainControlMessages()
    {
        while (_control.TryDequeue(out RendererControlMessage message))
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

        // The frame fence has signaled: the snapshot the frame held is retired, then the newest
        // Ready snapshot is taken for this frame. With no snapshot there is no GPU frame.
        SnapshotSlot? slot = _consumer.TakeForFreeFrame(_frameIndex);
        if (slot is null)
        {
            return;
        }

        // Advance the texture-page state machine once per frame, before recording: completed
        // descriptor/payload loads prepare uploads, completed uploads write their bindless
        // descriptor, and a capacity growth recreates the frame sets while the device is idle.
        _pages!.Pump();

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
            _consumer.ReleaseTaken(slot);
            throw CreateFatalForResult(acquire);
        }

        if (acquire is Result.ErrorOutOfDateKhr or Result.ErrorSurfaceLostKhr)
        {
            // The snapshot never became a GPU frame: free it immediately (never wait a fence).
            _consumer.ReleaseTaken(slot);
            _lastResult = acquire;
            return;
        }

        if (acquire != Result.Success && acquire != Result.SuboptimalKhr)
        {
            _consumer.ReleaseTaken(slot);
            throw new Bork2dException(
                $"vkAcquireNextImageKHR failed with VkResult {acquire}.");
        }

        Interlocked.Increment(ref _lastFrameNumber);

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

        RecordClear(frame, imageIndex, swapchain, device);
        Submit(frame, imageIndex, swapchain, device);
        _consumer.AttachToFrame(_frameIndex, slot);

        Result present = Present(imageIndex, device, swapchain);
        Volatile.Write(ref _lastPresentResult, (int)present);
        if (present is Result.Success or Result.SuboptimalKhr)
        {
            Interlocked.Increment(ref _framesPresented);
            SignalProgress();
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

    private void RecordClear(
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
            _clearLabel);
        var attachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = swapchain.GetView(imageIndex),
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
            RenderArea = new Rect2D { Extent = swapchain.Extent },
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

        var toPresent = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.None,
            DstAccessMask = AccessFlags2.None,
            OldLayout = ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.PresentSrcKhr,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = swapchain.GetImage(imageIndex),
            SubresourceRange = range,
        };
        var presentDependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &toPresent,
        };
        device.Api.CmdPipelineBarrier2(commandBuffer, &presentDependency);

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
        EnsureSpritePipeline();

        _lastResult = Result.Success;
        if (reason is not null)
        {
            Interlocked.Increment(ref _swapchainRecreations);
            SignalProgress();
        }
    }

    /// <summary>
    /// Creates the sprite pipeline owner on first use and the pipeline for the current swapchain
    /// format. Failure propagates as a renderer startup/recreation failure (D-M6-14).
    /// </summary>
    private void EnsureSpritePipeline()
    {
        SpritePipeline pipeline = _spritePipeline
            ??= SpritePipeline.Create(_device!, _instance, _logger);
        pipeline.GetOrCreatePipeline(_swapchain!.Format);
    }

    private void Recreate(SwapchainRecreateReason reason)
    {
        WaitForDeviceIdle("swapchain recreation");

        // vkDeviceWaitIdle completed every in-flight fence: their snapshot slots are free.
        ReleaseHeldSnapshots("swapchain recreation");
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
        WaitForDeviceIdle("surface recovery");
        ReleaseHeldSnapshots("surface recovery");
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

    private void CreateFrames()
    {
        VulkanDevice device = _device!;
        SpritePipeline pipeline = _spritePipeline
            ?? throw new Bork2dException(
                "The windowed renderer cannot create frames without the sprite pipeline.");
        _frames = new WindowedFrame?[FramesInFlight];
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
            _frames[index] = frame;
            _instance!.SetObjectName(
                device.Device.Handle,
                ObjectType.CommandBuffer,
                frame.CommandBuffer.Handle,
                $"Bork2d frame {index} command buffer");
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
                "The windowed renderer cannot create texture pages without the sprite pipeline.");
        var gpu = new VulkanTexturePages(device, pipeline, this, _instance, _logger);
        _pages = new TexturePageStore(
            _startInfo.Assets,
            gpu,
            (int)pipeline.MaxTexturePages,
            (int)device.Limits.MaxImageDimension2D,
            _logger);
        _pages.Start();
    }

    /// <summary>
    /// Releases every snapshot slot the renderer holds after the device became idle, before the
    /// swapchain is recreated or recovered (Q8/Q9; every slot must end Free).
    /// </summary>
    private void ReleaseHeldSnapshots(string operation)
    {
        try
        {
            _consumer.ReleaseAll();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Vulkan snapshot-slot release failed during {Operation}.",
                operation);
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
                "The Vulkan renderer failed: " + exception.Message,
                exception);

        // A renderer fatal wakes a blocked producer and stops further acquisition (Q9=I1).
        _consumer.Ring.Stop();
        _control.PublishFatal(fatal);
        SetStopReason(RendererStopReason.Fatal);
        SetStatus(RendererStatus.Faulted);
        _logger.LogError(fatal, "Vulkan renderer fatal failure.");
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
                        "Vulkan device idle wait returned {Result} during shutdown.",
                        idle);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan device idle wait failed during shutdown.");
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

        // Every frame fence was observed and the device is idle: no GPU frame references a
        // snapshot slot any more, so every held slot returns to Free before teardown.
        ReleaseHeldSnapshots("shutdown");

        CapturePageCounters();

        _swapchain?.Dispose();
        _swapchain = null;

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
        _surfaceApi = null;
        _swapchainApi = null;
    }

    private void WaitForFrameFences(VulkanDevice device)
    {
        for (var index = 0; index < _frames.Length; index++)
        {
            WindowedFrame? frame = _frames[index];
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
                        "Vulkan frame fence wait returned {Result} during shutdown.",
                        result);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan frame fence wait failed during shutdown.");
            }
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

    private void LogStarted()
    {
        WindowedSwapchain? swapchain = _swapchain;
        if (swapchain is null)
        {
            return;
        }

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
            FrameInstanceResources.InstanceCapacity,
            _pages?.TexturePageCount ?? 0,
            _pages?.TexturePageCapacityInEntries ?? 0,
            _spritePipeline?.MaxTexturePages ?? 0,
            _pages?.IsPlaceholderReady == true ? "on" : "off",
            _instance?.ValidationEnabled == true ? "on" : "off",
            Volatile.Read(ref _framesPresented),
            0,
            Volatile.Read(ref _swapchainRecreations),
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
                + "failedPages={FailedPages}, "
                + "validationErrors={ValidationErrors}, validationWarnings={ValidationWarnings}, "
                + "lastFrameNumber={LastFrameNumber}, lastPresentResult={LastPresentResult}.",
            nameof(RendererMode.Windowed),
            StopReason,
            snapshots.Published,
            snapshots.Consumed,
            snapshots.Superseded,
            snapshots.BackpressureWaits,
            snapshots.MaxLive,
            _consumer.SuspendedConsumptions,
            Volatile.Read(ref _framesPresented),
            0,
            Volatile.Read(ref _swapchainRecreations),
            _loggedPageCount,
            _loggedPageCapacity,
            _loggedPageGrowths,
            _loggedFailedPaths,
            Volatile.Read(ref _validationErrorCount),
            Volatile.Read(ref _validationWarningCount),
            Volatile.Read(ref _lastFrameNumber),
            Volatile.Read(ref _lastPresentResult));
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
