using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Render.Diagnostics;
using Microsoft.Extensions.Logging;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The production windowed Vulkan renderer: one render thread owns the device, swapchain, frames,
/// and the dynamic-rendering clear, and presents continuously until the host stops it
/// (D-M6-01, D-M6-06).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> creates one background thread and returns immediately. The thread selects a
/// present-capable device, creates the swapchain and two frames in flight, then marks the renderer
/// ready. The host observes readiness, status, counters, and the one-shot fatal error through
/// <see cref="IRenderer"/>.
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
    private const string ThreadName = "Bork2d.Render";
    private const ulong CleanupFenceTimeoutNanoseconds = 1_000_000_000;

    private readonly RendererStartInfo _startInfo;
    private readonly ILogger _logger;
    private readonly RendererControlChannel _control = new();
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly ManualResetEventSlim _progress = new(initialState: false);

    private WindowedFrame?[] _frames = [];
    private Thread? _thread;
    private VulkanDevice? _device;
    private WindowedSwapchain? _swapchain;
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
    private long _minimizedSkips;
    private Result _lastResult = Result.Success;
    private WindowExtent _requestedExtent;
    private bool _fullscreen;
    private bool _fullscreenChanged;
    private bool _minimized;
    private bool _minimizedLogged;
    private bool _surfaceRecoveryAttempted;

    /// <summary>Initializes a new instance of the <see cref="VulkanRenderer"/> class.</summary>
    /// <param name="startInfo">The validated windowed start request.</param>
    internal VulkanRenderer(RendererStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        _startInfo = startInfo;
        _logger = startInfo.LoggerFactory.CreateLogger(RenderLog.Category);
        _requestedExtent = new WindowExtent(startInfo.Width, startInfo.Height);
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
    public Exception? FatalException => _control.FatalException;

    /// <summary>Gets the number of times the renderer skipped acquire/present while minimized.</summary>
    /// <remarks>Diagnostic only; the GPU tests use it to observe the minimized policy.</remarks>
    internal long MinimizedSkips => Volatile.Read(ref _minimizedSkips);

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
    public void Join() => _thread?.Join();

    /// <inheritdoc/>
    public bool Join(TimeSpan timeout)
    {
        Thread? thread = _thread;
        return thread is null || thread.Join(timeout);
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for the counters to satisfy a predicate
    /// (GPU tests only; event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The counter condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForProgress(Func<RendererCounters, bool> predicate, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (predicate(Counters))
            {
                return true;
            }

            _progress.Reset();
            if (predicate(Counters))
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
                return predicate(Counters);
            }
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
        while (!_control.IsStopRequested)
        {
            ObserveValidation();
            if (_control.IsStopRequested)
            {
                break;
            }

            DrainControlMessages();
            if (_control.IsStopRequested)
            {
                break;
            }

            if (_minimized || _requestedExtent.IsZeroOrMinimized)
            {
                WaitWhileMinimized();
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
                    WaitWhileMinimized();
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
                    PresentFrame();
                    break;

                default:
                    throw new Bork2dException(
                        $"The windowed renderer received unexpected swapchain action {action}.");
            }
        }
    }

    private void WaitWhileMinimized()
    {
        if (!_minimizedLogged)
        {
            _logger.LogDebug(
                "Vulkan renderer minimized: reason=Minimize, extent={Width}x{Height}; "
                    + "skipping acquire/present.",
                _requestedExtent.Width,
                _requestedExtent.Height);
            _minimizedLogged = true;
        }

        Interlocked.Increment(ref _minimizedSkips);
        SignalProgress();
        _control.WaitForWork();
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
            throw CreateFatalForResult(acquire);
        }

        if (acquire is Result.ErrorOutOfDateKhr or Result.ErrorSurfaceLostKhr)
        {
            _lastResult = acquire;
            return;
        }

        if (acquire != Result.Success && acquire != Result.SuboptimalKhr)
        {
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
        old?.Dispose();
        _lastResult = Result.Success;
        if (reason is not null)
        {
            Interlocked.Increment(ref _swapchainRecreations);
            SignalProgress();
        }
    }

    private void Recreate(SwapchainRecreateReason reason)
    {
        WaitForDeviceIdle("swapchain recreation");
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
        _frames = new WindowedFrame?[FramesInFlight];
        for (var index = 0; index < FramesInFlight; index++)
        {
            WindowedFrame frame = WindowedFrame.Create(
                device.Api,
                device.Device,
                device.QueueFamilyIndex,
                _logger);
            _frames[index] = frame;
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

        _swapchain?.Dispose();
        _swapchain = null;

        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.Dispose();
            _frames[index] = null;
        }

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
                + "framesInFlight={FramesInFlight}, validation={Validation}, "
                + "framesPresented={FramesPresented}, framesDiscarded={FramesDiscarded}, "
                + "swapchainRecreations={SwapchainRecreations}, "
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
        _logger.LogInformation(
            "Vulkan renderer stopped: mode={Mode}, reason={Reason}, "
                + "framesPresented={FramesPresented}, framesDiscarded={FramesDiscarded}, "
                + "swapchainRecreations={SwapchainRecreations}, "
                + "validationErrors={ValidationErrors}, validationWarnings={ValidationWarnings}, "
                + "lastFrameNumber={LastFrameNumber}, lastPresentResult={LastPresentResult}.",
            nameof(RendererMode.Windowed),
            StopReason,
            Volatile.Read(ref _framesPresented),
            0,
            Volatile.Read(ref _swapchainRecreations),
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
