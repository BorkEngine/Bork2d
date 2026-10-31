using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Microsoft.Extensions.Logging;

namespace Bork2d.Render.Textures;

/// <summary>
/// The render-thread-owned texture-page cache and state machine (M8 §6.5): one page per canonical
/// <see cref="TextureDescriptor"/> path, asynchronous prefetch through the app's <see cref="IAssets"/>
/// seam, content validation, the placeholder, dynamic capacity, and upload completion.
/// </summary>
/// <remarks>
/// <para>
/// Page identity is the canonical descriptor path. The first successful load wins for the run;
/// later descriptors for the same path reuse the page. Failed paths are <see cref="TexturePageState.Failed"/>
/// for the run and are never retried or re-loaded. A null/absent texture reference maps directly to
/// the placeholder at index <c>0</c>.
/// </para>
/// <para>
/// The state machine is <see cref="TexturePageState.Pending"/> (the descriptor/payload load runs on
/// the asset loader thread and is polled without blocking) → content validated and the next real
/// index assigned → <see cref="TexturePageState.Uploading"/> (the GPU submitted a staging copy with
/// its own fence) → <see cref="TexturePageState.Ready"/> (the fence signaled and the descriptor is
/// written into both per-frame bindless sets) or <see cref="TexturePageState.Failed"/> (the
/// placeholder draws instead). Pending pages are skipped by drawing; placeholder-drawn sprites are
/// not skipped. An index is only assigned once the content validated, so a failed path never
/// consumes capacity or triggers growth.
/// </para>
/// <para>
/// The render thread calls <see cref="EnsurePage(string?)"/> while planning a frame and
/// <see cref="Pump"/> once per frame; nothing here waits on a page load, and only the startup
/// placeholder waits on an upload fence. <see cref="Destroy"/> stops and ignores pending
/// completions and tears the GPU pages down; it is idempotent and never throws.
/// </para>
/// </remarks>
internal sealed class TexturePageStore
{
    private readonly IAssets _assets;
    private readonly ITexturePageGpu _gpu;
    private readonly ILogger _logger;
    private readonly int _maxImageDimension2D;
    private readonly int _limit;
    private readonly Dictionary<string, TexturePage> _pages = new(StringComparer.Ordinal);
    private readonly List<TexturePage> _pending = [];
    private readonly List<TexturePage> _uploading = [];
    private int _capacity = TexturePageCapacity.Initial;
    private int _nextIndex = TexturePageCapacity.PlaceholderIndex + 1;
    private int _readyRealPages;
    private int _failedPaths;
    private int _growths;
    private bool _placeholderReady;
    private bool _destroyed;

    /// <summary>Initializes a new instance of the <see cref="TexturePageStore"/> class.</summary>
    /// <param name="assets">
    /// The app's asset service. The render thread calls it asynchronously; it stays valid until the
    /// renderer joins, which is before the service stops.
    /// </param>
    /// <param name="gpu">The Vulkan upload/descriptor seam (a fake in deterministic tests).</param>
    /// <param name="effectiveLimit">
    /// The effective bindless page limit (the smaller of the per-stage and per-set update-after-bind
    /// sampled-image limits), including the placeholder.
    /// </param>
    /// <param name="maxImageDimension2D">The device's maximum 2D image dimension.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for once-per-path content diagnostics.</param>
    /// <exception cref="Bork2dException">The limit cannot support the initial capacity.</exception>
    internal TexturePageStore(
        IAssets assets,
        ITexturePageGpu gpu,
        int effectiveLimit,
        int maxImageDimension2D,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(logger);

        TexturePageCapacity.EnsureSupportsInitialCapacity(effectiveLimit);
        _assets = assets;
        _gpu = gpu;
        _logger = logger;
        _limit = effectiveLimit;
        _maxImageDimension2D = maxImageDimension2D;
    }

    /// <summary>Gets the resident page count, including the placeholder (public <c>TexturePages</c>).</summary>
    internal int TexturePageCount => (_placeholderReady ? 1 : 0) + _readyRealPages;

    /// <summary>Gets the current capacity in entries, including the placeholder.</summary>
    internal int TexturePageCapacityInEntries => _capacity;

    /// <summary>Gets the number of paths failed for the run (public placeholder accounting).</summary>
    internal int FailedPathCount => _failedPaths;

    /// <summary>Gets the number of descriptor/payload loads still in flight.</summary>
    internal int PendingLoadCount => _pending.Count;

    /// <summary>Gets the number of uploads submitted and not yet complete.</summary>
    internal int UploadingCount => _uploading.Count;

    /// <summary>Gets the number of capacity growths this run.</summary>
    internal int GrowthCount => _growths;

    /// <summary>Gets a value indicating whether the placeholder page is resident and ready.</summary>
    internal bool IsPlaceholderReady => _placeholderReady;

    /// <summary>
    /// Creates, uploads (waiting for its fence), and makes the placeholder page resident at index
    /// <c>0</c>. Failure is a renderer startup failure.
    /// </summary>
    /// <exception cref="Bork2dException">The placeholder image or upload could not be created.</exception>
    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);

        _gpu.EnsureCapacity(_capacity);
        _gpu.Upload(
            TexturePageCapacity.PlaceholderIndex,
            TexturePlaceholder.CreateDescriptor(),
            TexturePlaceholder.CreatePixels());
        _gpu.WaitForUpload(TexturePageCapacity.PlaceholderIndex);
        _placeholderReady = true;
    }

    /// <summary>
    /// Returns the bindless page that currently represents a texture path, starting an asynchronous
    /// load on first sighting.
    /// </summary>
    /// <remarks>
    /// This is the entry point the M8 draw work calls per candidate sprite. The steady-state path
    /// (an already-known path) is a dictionary lookup returning a value struct, so it allocates
    /// nothing and never blocks. A null or empty path maps straight to the placeholder with the
    /// placeholder's own 8×8 extent.
    /// </remarks>
    /// <param name="path">The canonical texture descriptor path, or <see langword="null"/>.</param>
    /// <returns>The page lookup for this frame.</returns>
    internal TexturePageLookup EnsurePage(string? path)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);

        if (string.IsNullOrEmpty(path))
        {
            return PlaceholderLookup(declaredWidth: 0, declaredHeight: 0);
        }

        if (_pages.TryGetValue(path, out TexturePage? page))
        {
            return LookupFor(page);
        }

        var created = new TexturePage(path);
        _pages.Add(path, created);
        StartLoad(created);
        return LookupFor(created);
    }

    /// <summary>
    /// Advances the state machine once per frame: consumes completed loads, prepares uploads, and
    /// promotes uploads whose fence has signaled.
    /// </summary>
    /// <exception cref="Bork2dException">
    /// The page capacity reached the device limit, or a Vulkan upload failed.
    /// </exception>
    internal void Pump()
    {
        if (_destroyed)
        {
            return;
        }

        AdvanceLoads();
        CompleteUploads();
    }

    /// <summary>
    /// Reports the current state of a known path (tests and diagnostics).
    /// </summary>
    /// <param name="path">The canonical texture descriptor path.</param>
    /// <param name="state">The page state when the path is known.</param>
    /// <param name="failure">The content failure when the path is known.</param>
    /// <returns><see langword="true"/> when the path has a page record.</returns>
    internal bool TryGetPageState(string path, out TexturePageState state, out TexturePageFailure failure)
    {
        if (!_destroyed && _pages.TryGetValue(path, out TexturePage? page))
        {
            state = page.State;
            failure = page.Failure;
            return true;
        }

        state = default;
        failure = default;
        return false;
    }

    /// <summary>Destroys the GPU pages and drops the logical state; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;

        // Pending loads run on the asset loader; the renderer joins before the service stops, so
        // their completions are ignored here. Observe a later fault so it never surfaces as an
        // unobserved task exception.
        foreach (TexturePage page in _pending)
        {
            ObserveFault(page.DescriptorTask);
            ObserveFault(page.PayloadTask);
        }

        _pending.Clear();
        _uploading.Clear();
        _pages.Clear();
        _gpu.Destroy();
    }

    private static void ObserveFault(Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void StartLoad(TexturePage page)
    {
        page.State = TexturePageState.Pending;
        try
        {
            page.DescriptorTask = _assets.LoadAsset<TextureDescriptor>(page.Path);
        }
        catch (Exception)
        {
            // A synchronous load failure (invalid path, disposed service) is a content failure, not
            // a Vulkan failure: fail the path and keep the app running with the placeholder.
            Fail(page, TexturePageFailure.LoadFault);
            return;
        }

        _pending.Add(page);
    }

    private void AdvanceLoads()
    {
        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            TexturePage page = _pending[index];
            if (AdvancePage(page))
            {
                _pending.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Advances one page's load pipeline as far as the completed tasks allow, so a descriptor and
    /// payload that both completed between frames become content-ready in a single pump.
    /// </summary>
    /// <returns><see langword="true"/> when the page left the pending set.</returns>
    private bool AdvancePage(TexturePage page)
    {
        while (true)
        {
            if (page.DescriptorTask is { } descriptorTask)
            {
                if (!descriptorTask.IsCompleted)
                {
                    return false;
                }

                page.DescriptorTask = null;
                if (!TryTakeResult(descriptorTask, out TextureDescriptor? descriptor)
                    || descriptor is null)
                {
                    Fail(page, TexturePageFailure.MissingDescriptor);
                    return true;
                }

                page.Descriptor = descriptor;
                RecordDeclaredSize(page, descriptor);
                if (!descriptor.Payload.HasValue)
                {
                    Fail(page, TexturePageFailure.MissingPayload);
                    return true;
                }

                try
                {
                    page.PayloadTask = _assets.LoadData(descriptor.Payload);
                }
                catch (Exception)
                {
                    Fail(page, TexturePageFailure.LoadFault);
                    return true;
                }

                continue;
            }

            if (page.PayloadTask is { } payloadTask)
            {
                if (!payloadTask.IsCompleted)
                {
                    return false;
                }

                page.PayloadTask = null;
                if (!TryTakeResult(payloadTask, out byte[]? payload) || payload is null)
                {
                    Fail(page, TexturePageFailure.MissingPayload);
                    return true;
                }

                if (!Validate(page, payload, out TexturePageFailure failure))
                {
                    Fail(page, failure);
                    return true;
                }

                BeginUpload(page, payload);
                return true;
            }

            return false;
        }
    }

    private void CompleteUploads()
    {
        for (var index = _uploading.Count - 1; index >= 0; index--)
        {
            TexturePage page = _uploading[index];
            if (!_gpu.IsUploadComplete(page.Index))
            {
                continue;
            }

            _gpu.CompleteUpload(page.Index);
            page.State = TexturePageState.Ready;
            _readyRealPages++;
            _uploading.RemoveAt(index);
        }
    }

    private void BeginUpload(TexturePage page, byte[] payload)
    {
        if (_nextIndex >= _capacity)
        {
            Grow();
        }

        page.Index = _nextIndex;
        _nextIndex++;
        page.State = TexturePageState.Uploading;
        _uploading.Add(page);
        _gpu.Upload(page.Index, page.Descriptor!, payload);
    }

    private void Grow()
    {
        if (!TexturePageCapacity.TryGrow(_capacity, _limit, out int grown))
        {
            throw TexturePageCapacity.CreateLimitException(_capacity, _limit, _nextIndex);
        }

        int previous = _capacity;
        _gpu.EnsureCapacity(grown);
        _capacity = grown;
        _growths++;
        _logger.LogInformation(
            "Texture page capacity grew: from={PreviousCapacity}, to={Capacity}, limit={Limit}.",
            previous,
            _capacity,
            _limit);
    }

    private bool Validate(TexturePage page, byte[] payload, out TexturePageFailure failure)
    {
        TextureDescriptor descriptor = page.Descriptor!;
        if (descriptor.Format != TextureFormat.Rgba8)
        {
            failure = TexturePageFailure.UnsupportedFormat;
            return false;
        }

        if (descriptor.Width == 0 || descriptor.Height == 0)
        {
            failure = TexturePageFailure.InvalidDimensions;
            return false;
        }

        if (descriptor.Width > (uint)_maxImageDimension2D
            || descriptor.Height > (uint)_maxImageDimension2D)
        {
            failure = TexturePageFailure.DimensionLimit;
            return false;
        }

        ulong expected = (ulong)descriptor.Width * descriptor.Height * 4UL;
        if ((ulong)payload.Length != expected)
        {
            failure = TexturePageFailure.PayloadLength;
            return false;
        }

        failure = TexturePageFailure.None;
        return true;
    }

    private void RecordDeclaredSize(TexturePage page, TextureDescriptor descriptor)
    {
        if (descriptor.Width == 0
            || descriptor.Height == 0
            || descriptor.Width > (uint)_maxImageDimension2D
            || descriptor.Height > (uint)_maxImageDimension2D)
        {
            return;
        }

        page.Width = (int)descriptor.Width;
        page.Height = (int)descriptor.Height;
    }

    private void Fail(TexturePage page, TexturePageFailure failure)
    {
        if (page.State == TexturePageState.Failed)
        {
            return;
        }

        page.State = TexturePageState.Failed;
        page.Failure = failure;
        _failedPaths++;
        page.DescriptorTask = null;
        page.PayloadTask = null;

        // Once per failed path for the run; never per frame or per sprite (M8 §6.10).
        _logger.LogWarning(
            "Texture page '{Path}' failed ({Failure}); the renderer draws the placeholder for this "
                + "path for the rest of the run and does not retry it.",
            page.Path,
            failure);
    }

    private static bool TryTakeResult<T>(Task<T> task, out T? value)
    {
        if (task.IsCompletedSuccessfully)
        {
            value = task.Result;
            return true;
        }

        // Observe a fault so it never surfaces as an unobserved task exception.
        _ = task.Exception;
        value = default;
        return false;
    }

    private TexturePageLookup LookupFor(TexturePage page) => page.State switch
    {
        TexturePageState.Ready => new TexturePageLookup(
            TexturePageLookupKind.Ready,
            page.Index,
            page.Width,
            page.Height),
        TexturePageState.Failed => PlaceholderLookup(page.Width, page.Height),
        _ => new TexturePageLookup(TexturePageLookupKind.Pending, 0, 0, 0),
    };

    private static TexturePageLookup PlaceholderLookup(int declaredWidth, int declaredHeight)
    {
        // Owner-approved sizing: a failed texture keeps its declared dimensions when the descriptor
        // loaded and they are valid, even if the payload failed; otherwise the placeholder's 8x8.
        int width = declaredWidth > 0 ? declaredWidth : TexturePlaceholder.Size;
        int height = declaredHeight > 0 ? declaredHeight : TexturePlaceholder.Size;
        return new TexturePageLookup(
            TexturePageLookupKind.Placeholder,
            TexturePageCapacity.PlaceholderIndex,
            width,
            height);
    }
}
