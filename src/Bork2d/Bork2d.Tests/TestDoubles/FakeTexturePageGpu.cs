using Bork2d.Engine.Assets;
using Bork2d.Render.Textures;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic <see cref="ITexturePageGpu"/> that records capacity and upload requests and lets
/// a test decide when an upload completes, so the page state machine runs without Vulkan.
/// </summary>
internal sealed class FakeTexturePageGpu : ITexturePageGpu
{
    private readonly List<TexturePageUpload> _uploads = [];
    private readonly HashSet<int> _pending = [];
    private readonly HashSet<int> _completed = [];
    private int _resident;

    /// <summary>Gets the capacities requested through <see cref="EnsureCapacity"/>.</summary>
    internal List<int> CapacityRequests { get; } = [];

    /// <summary>Gets the uploads requested through <see cref="Upload"/>.</summary>
    internal IReadOnlyList<TexturePageUpload> Uploads => _uploads;

    /// <summary>
    /// Gets or sets a value indicating whether every submitted upload's fence reports complete
    /// immediately (the default, so one pump makes a page ready).
    /// </summary>
    internal bool UploadsCompleteImmediately { get; set; } = true;

    /// <inheritdoc/>
    public int ResidentPageCount => _resident;

    /// <summary>Gets the number of <see cref="Destroy"/> calls.</summary>
    internal int DestroyCount { get; private set; }

    /// <inheritdoc/>
    public int PendingUploadCount => _pending.Count;

    /// <inheritdoc/>
    public void EnsureCapacity(int capacity) => CapacityRequests.Add(capacity);

    /// <inheritdoc/>
    public void Upload(int pageIndex, TextureDescriptor descriptor, byte[] payload)
    {
        _pending.Add(pageIndex);
        _uploads.Add(
            new TexturePageUpload(
                pageIndex,
                (int)descriptor.Width,
                (int)descriptor.Height,
                descriptor.Format,
                descriptor.Filter,
                descriptor.Wrap,
                payload.Length));
    }

    /// <inheritdoc/>
    public bool IsUploadComplete(int pageIndex)
        => UploadsCompleteImmediately || _completed.Contains(pageIndex);

    /// <inheritdoc/>
    public void CompleteUpload(int pageIndex)
    {
        _pending.Remove(pageIndex);
        _completed.Remove(pageIndex);
        _resident++;
    }

    /// <inheritdoc/>
    public void WaitForUpload(int pageIndex)
    {
        _pending.Remove(pageIndex);
        _completed.Remove(pageIndex);
        _resident++;
    }

    /// <inheritdoc/>
    public void Destroy() => DestroyCount++;

    /// <summary>Reports a specific upload's fence as signaled (when auto-complete is off).</summary>
    /// <param name="pageIndex">The page index.</param>
    internal void SignalUpload(int pageIndex) => _completed.Add(pageIndex);
}

/// <summary>One recorded upload request.</summary>
/// <param name="Index">The assigned bindless page index.</param>
/// <param name="Width">The descriptor width.</param>
/// <param name="Height">The descriptor height.</param>
/// <param name="Format">The descriptor format.</param>
/// <param name="Filter">The descriptor filter.</param>
/// <param name="Wrap">The descriptor wrap.</param>
/// <param name="PayloadLength">The validated payload length in bytes.</param>
internal readonly record struct TexturePageUpload(
    int Index,
    int Width,
    int Height,
    TextureFormat Format,
    TextureFilter Filter,
    TextureWrap Wrap,
    int PayloadLength);
