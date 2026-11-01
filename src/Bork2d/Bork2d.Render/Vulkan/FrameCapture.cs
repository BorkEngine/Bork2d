using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One GPU frame's pooled readback staging buffer and the capture requests whose copy it records
/// (M8 §6.9).
/// </summary>
/// <remarks>
/// <para>
/// The copy is recorded after the last camera pass and before present/discard, so it includes every
/// camera clear and draw and excludes OS/window chrome. The staging buffer is host-visible coherent,
/// created lazily on the first capture, reused for the frame, and recreated only when the requested
/// extent grows; that recreation happens between frames, never while the frame is in flight.
/// </para>
/// <para>
/// Requests complete when the frame's fence is observed (the renderer's independent retirement pass,
/// frame reuse, or shutdown), not by blocking the render thread on the GPU. The staging bytes are
/// swizzled from <c>B8G8R8A8</c> to <c>RGBA8</c> when the target format is BGRA and copied directly
/// for <c>R8G8B8A8</c>, into one <see cref="byte"/> array per captured frame; every request served
/// by that frame receives the same shared immutable <see cref="FrameCaptureResult"/>, so the result
/// dimensions/format are the captured frame's actual extent and
/// <see cref="TextureFormat.Rgba8"/>. <see cref="Destroy"/> is idempotent and never throws.
/// </para>
/// </remarks>
internal sealed unsafe class FrameCapture
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly ILogger _logger;
    private readonly int _frameIndex;
    private readonly List<CaptureRequest> _requests = [];
    private Buffer _buffer;
    private DeviceMemory _memory;
    private void* _mapped;
    private ulong _allocationBytes;
    private int _capacityBytes;
    private int _width;
    private int _height;
    private Format _format;
    private FrameCaptureState _state;
    private int _destroyed;

    /// <summary>Initializes a new instance of the <see cref="FrameCapture"/> class.</summary>
    /// <param name="vk">The Vulkan API object that owns the device.</param>
    /// <param name="device">The selected logical device.</param>
    /// <param name="physicalDevice">The physical device used to select the memory type.</param>
    /// <param name="frameIndex">The GPU frame index, used for diagnostics only.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    internal FrameCapture(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        int frameIndex,
        ILogger logger)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _frameIndex = frameIndex;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether any request is attached to this frame.</summary>
    internal bool HasRequests => _requests.Count > 0;

    /// <summary>Gets the staging buffer to copy the image into.</summary>
    internal Buffer StagingBuffer => _buffer;

    /// <summary>Gets the staging buffer's allocated byte size.</summary>
    internal ulong StagingAllocationBytes => _allocationBytes;

    /// <summary>Gets the number of requests that were completed from this frame (diagnostics).</summary>
    internal int CompletedRequestCount { get; private set; }

    /// <summary>
    /// Takes the queue's pending requests and prepares the staging buffer for this frame's copy.
    /// </summary>
    /// <param name="queue">The renderer's request queue.</param>
    /// <param name="width">The captured frame width in pixels.</param>
    /// <param name="height">The captured frame height in pixels.</param>
    /// <param name="format">The target's color format.</param>
    /// <returns><see langword="true"/> when a copy must be recorded.</returns>
    /// <exception cref="Bork2dException">The staging buffer could not be created.</exception>
    internal bool BeginRecord(
        CaptureRequestQueue queue,
        int width,
        int height,
        Format format)
    {
        ArgumentNullException.ThrowIfNull(queue);
        int count = queue.TakePending(_requests);
        if (count == 0)
        {
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            FaultAll(
                new Bork2dException(
                    $"A frame capture needs a positive extent; got {width}x{height}."));
            return false;
        }

        ulong bytes = (ulong)width * (ulong)height * 4UL;
        if (bytes > int.MaxValue)
        {
            FaultAll(
                new Bork2dException(
                    $"A {width}x{height} frame capture needs {bytes} bytes, which exceeds the "
                        + "largest supported byte array."));
            return false;
        }

        EnsureStaging((int)bytes);
        _width = width;
        _height = height;
        _format = format;
        _state.Begin();
        return true;
    }

    /// <summary>Marks that the image-to-buffer copy was recorded into this frame's command buffer.</summary>
    internal void MarkRecorded() => _state.MarkRecorded();

    /// <summary>
    /// Marks that the frame command buffer was submitted successfully.
    /// </summary>
    /// <remarks>
    /// The renderer calls this only after <c>vkQueueSubmit2</c> returns success. A frame that
    /// recorded a copy but never reached submission must fault its requests on cleanup instead of
    /// copying staging bytes the GPU never wrote.
    /// </remarks>
    internal void MarkSubmitted() => _state.MarkSubmitted();

    /// <summary>
    /// Completes every attached request from the fence-observed staging bytes.
    /// </summary>
    /// <remarks>
    /// The renderer calls this once the frame's fence has signaled (the independent retirement pass,
    /// frame reuse, or shutdown). The staging bytes are swizzled/copied into one array for the
    /// captured frame, and every request that frame serves receives the same shared immutable
    /// result. A request whose frame never recorded a copy, or recorded one but was never submitted,
    /// is faulted instead of returning uninitialized bytes.
    /// </remarks>
    internal void Complete()
    {
        if (_requests.Count == 0)
        {
            return;
        }

        if (!_state.CanComplete)
        {
            string reason = _state.RecordedButNotSubmitted
                ? "The frame capture was recorded but the frame was never submitted, so its "
                    + "staging bytes were not written by the GPU."
                : "The renderer stopped before the frame copy was recorded.";
            FaultAll(new ObjectDisposedException(nameof(FrameCapture), reason));
            _state.Reset();
            _width = 0;
            _height = 0;
            return;
        }

        int length = _width * _height * 4;
        byte[]? pixels = null;
        for (var index = 0; index < _requests.Count; index++)
        {
            CaptureRequest request = _requests[index];
            if (request.IsCompleted)
            {
                continue;
            }

            // One allocation per captured frame, shared by every request it serves; a cancelled
            // request therefore allocates nothing.
            pixels ??= CopyPixels(length);
            request.Complete(new FrameCaptureResult(pixels, _width, _height, TextureFormat.Rgba8));
            CompletedRequestCount++;
        }

        _requests.Clear();
        _state.Reset();
        _width = 0;
        _height = 0;
    }

    /// <summary>Faults every attached request (stop/fatal) and clears the list.</summary>
    /// <param name="exception">The failure every request observes.</param>
    internal void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        FaultAll(exception);
        _state.Reset();
    }

    /// <summary>Unmaps and frees the staging buffer; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        FaultAll(
            new ObjectDisposedException(
                nameof(FrameCapture),
                "The renderer stopped before the capture frame completed."));
        _state.Reset();

        if (_mapped is not null && _memory.Handle != 0)
        {
            try
            {
                _vk.UnmapMemory(_device, _memory);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan capture staging unmap failed.");
            }

            _mapped = null;
        }

        if (_buffer.Handle != 0)
        {
            try
            {
                _vk.DestroyBuffer(_device, _buffer, null);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan capture staging buffer destruction failed.");
            }

            _buffer = default;
        }

        if (_memory.Handle != 0)
        {
            try
            {
                _vk.FreeMemory(_device, _memory, null);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan capture staging memory free failed.");
            }

            _memory = default;
        }

        _allocationBytes = 0;
        _capacityBytes = 0;
    }

    private void FaultAll(Exception exception)
    {
        for (var index = 0; index < _requests.Count; index++)
        {
            _requests[index].Fault(exception);
        }

        _requests.Clear();
    }

    private void EnsureStaging(int bytes)
    {
        if (_capacityBytes >= bytes)
        {
            return;
        }

        var buffer = default(Buffer);
        var memory = default(DeviceMemory);
        void* mapped = null;
        var allocationBytes = 0UL;
        try
        {
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = (ulong)bytes,
                Usage = BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
            };
            Result result = _vk.CreateBuffer(_device, in bufferInfo, null, out buffer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateBuffer failed for frame {_frameIndex}'s capture staging with "
                        + $"VkResult {result} ({bytes} bytes).");
            }

            MemoryRequirements requirements = _vk.GetBufferMemoryRequirements(_device, buffer);
            PhysicalDeviceMemoryProperties memoryProperties =
                _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice);
            uint memoryTypeIndex = SelectMemoryType(
                memoryProperties,
                requirements.MemoryTypeBits,
                _frameIndex);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = memoryTypeIndex,
            };
            result = _vk.AllocateMemory(_device, in allocateInfo, null, out memory);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkAllocateMemory failed for frame {_frameIndex}'s capture staging with "
                        + $"VkResult {result} ({requirements.Size} bytes, memory type "
                        + $"{memoryTypeIndex}).");
            }

            result = _vk.BindBufferMemory(_device, buffer, memory, 0);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkBindBufferMemory failed for frame {_frameIndex}'s capture staging with "
                        + $"VkResult {result}.");
            }

            void* pointer = null;
            result = _vk.MapMemory(_device, memory, 0, requirements.Size, 0, &pointer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkMapMemory failed for frame {_frameIndex}'s capture staging with VkResult "
                        + $"{result} ({requirements.Size} bytes).");
            }

            mapped = pointer;
            allocationBytes = requirements.Size;
        }
        catch
        {
            DestroyStagingBestEffort(buffer, memory, mapped);
            throw;
        }

        Buffer previousBuffer = _buffer;
        DeviceMemory previousMemory = _memory;
        void* previousMapped = _mapped;
        _buffer = buffer;
        _memory = memory;
        _mapped = mapped;
        _allocationBytes = allocationBytes;
        _capacityBytes = bytes;

        if (previousBuffer.Handle != 0 || previousMemory.Handle != 0)
        {
            DestroyStagingBestEffort(previousBuffer, previousMemory, previousMapped);
        }
    }

    private byte[] CopyPixels(int length)
    {
        var source = new ReadOnlySpan<byte>(_mapped, length);
        var pixels = new byte[length];
        if (IsBgra(_format))
        {
            for (var index = 0; index < length; index += 4)
            {
                pixels[index] = source[index + 2];
                pixels[index + 1] = source[index + 1];
                pixels[index + 2] = source[index];
                pixels[index + 3] = source[index + 3];
            }

            return pixels;
        }

        source.CopyTo(pixels);
        return pixels;
    }

    private static bool IsBgra(Format format)
        => format is Format.B8G8R8A8Srgb or Format.B8G8R8A8Unorm;

    private void DestroyStagingBestEffort(Buffer buffer, DeviceMemory memory, void* mapped)
    {
        if (mapped is not null && memory.Handle != 0)
        {
            try
            {
                _vk.UnmapMemory(_device, memory);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan capture staging unmap failed.");
            }
        }

        if (buffer.Handle != 0)
        {
            try
            {
                _vk.DestroyBuffer(_device, buffer, null);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan capture staging buffer destruction failed.");
            }
        }

        if (memory.Handle != 0)
        {
            try
            {
                _vk.FreeMemory(_device, memory, null);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan capture staging memory free failed.");
            }
        }
    }

    private static uint SelectMemoryType(
        in PhysicalDeviceMemoryProperties properties,
        uint memoryTypeBits,
        int frameIndex)
    {
        const MemoryPropertyFlags required =
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (var index = 0; index < properties.MemoryTypeCount; index++)
        {
            if ((memoryTypeBits & (1u << index)) == 0)
            {
                continue;
            }

            if ((properties.MemoryTypes[index].PropertyFlags & required) == required)
            {
                return (uint)index;
            }
        }

        throw new Bork2dException(
            $"No host-visible coherent Vulkan memory type supports frame {frameIndex}'s capture "
                + $"staging buffer (memoryTypeBits=0x{memoryTypeBits:X}).");
    }
}
