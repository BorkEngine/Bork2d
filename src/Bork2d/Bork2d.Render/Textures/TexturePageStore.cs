using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Microsoft.Extensions.Logging;

namespace Bork2d.Render.Textures;

/// <summary>
/// The render-thread-owned texture-page cache and state machine (M8 §6.5): one page per canonical
/// <see cref="TextureDescriptor"/> path in the current asset catalog, asynchronous prefetch through
/// the app's <see cref="IAssets"/> seam, content validation, the placeholder, dynamic capacity, a
/// per-frame upload budget, and upload completion.
/// </summary>
/// <remarks>
/// <para>
/// Page identity is the canonical descriptor path plus the asset catalog version. The first
/// successful load in the current catalog wins; later descriptors for the same path reuse the page.
/// Failed paths are <see cref="TexturePageState.Failed"/> for the current catalog and are never
/// retried until the catalog changes. A null/absent texture reference maps directly to the
/// placeholder at index <c>0</c>.
/// </para>
/// <para>
/// The store checks <see cref="IAssetCatalogVersion.CatalogVersion"/> in <see cref="EnsurePage"/> and
/// <see cref="Pump"/>. When a later overlay mount changes it, the current page map, in-flight loads,
/// queued uploads, and failed-path state are retired, so the next <see cref="EnsurePage"/> for a path
/// starts a fresh load from the new winning entry and a previously failed path is retried. Uploads
/// that were already submitted finish so their GPU resources are released, but they never become
/// visible or count as ready. Retired pages keep their image resident until the reclamation safety
/// age proves no in-flight frame can sample them; their descriptor slots and image-memory ranges are
/// then recycled.
/// </para>
/// <para>
/// Residency is bounded by <see cref="ResidencyBudgetBytes"/> over the declared payload bytes
/// (<c>width * height * 4</c>) of the current catalog's ready pages. When the total exceeds the
/// budget, <see cref="Pump"/> evicts ready pages least recently seen first, but only pages the safety
/// age proves no in-flight frame can sample: with two GPU frames in flight, a page is reclaimable
/// only when <c>_frameSequence - LastSeenFrame &gt; 2</c>, so the two plans that could still be in
/// flight can never reference it. If every ready page is still recent, the over-budget set is kept
/// and eviction is retried on a later pump; the placeholder is never a candidate. A reclaimed
/// descriptor slot goes to a free list and the next upload reuses it before growing the table, and a
/// reclaimed image-memory range returns to the image pool for reuse.
/// </para>
/// <para>
/// The state machine is <see cref="TexturePageState.Pending"/> (the descriptor/payload load runs on
/// the asset loader thread and is polled without blocking) → content validated and queued for
/// upload (still <see cref="TexturePageState.Pending"/>, so drawing skips it) →
/// <see cref="TexturePageState.Uploading"/> (the GPU submitted a staging copy with its own fence) →
/// <see cref="TexturePageState.Ready"/> (the fence signaled and the descriptor is written into both
/// per-frame bindless sets) or <see cref="TexturePageState.Failed"/> (the placeholder draws
/// instead). Pending pages are skipped by drawing; placeholder-drawn sprites are not skipped. An
/// index is only assigned once the content validated and the upload budget admitted it, so a failed
/// path never consumes capacity or triggers growth.
/// </para>
/// <para>
/// <see cref="Pump"/> starts uploads up to the per-frame byte budget, charging
/// <c>width * height * 4</c> per page. The first queued upload of a frame always starts even when it
/// alone exceeds the budget, so one large texture cannot starve forever. Pages seen most recently by
/// <see cref="EnsurePage"/> start first; because the planner marks visibility during the frame
/// before the next pump, that priority is a next-frame ordering, not a same-frame guarantee.
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
    /// <summary>The default texture residency budget in bytes (1 GiB), enforced by page eviction.</summary>
    internal const long DefaultResidencyBudgetBytes = 1L << 30;

    /// <summary>The default per-frame texture upload budget in bytes (128 MiB).</summary>
    internal const int DefaultUploadBudgetBytes = 128 * 1024 * 1024;

    /// <summary>
    /// The number of frame pumps a page must stay unseen before its descriptor slot and image memory
    /// may be reclaimed.
    /// </summary>
    /// <remarks>
    /// The store pumps once per frame and the renderers keep two GPU frames in flight. A page last
    /// seen while planning the frame recorded at sequence <c>S</c> can still be sampled by that frame
    /// and by the frame recorded at <c>S + 1</c>, so reclaiming is safe only once
    /// <c>_frameSequence - LastSeenFrame &gt; ReclaimSafetyAgeFrames</c>: the reclaiming pump then runs
    /// before a new plan, and every frame that could reference the page is complete.
    /// </remarks>
    internal const int ReclaimSafetyAgeFrames = 2;

    private readonly IAssets _assets;
    private readonly ITexturePageGpu _gpu;
    private readonly ILogger _logger;
    private readonly int _maxImageDimension2D;
    private readonly int _limit;
    private readonly IAssetCatalogVersion? _catalogVersion;
    private readonly int _uploadBudgetBytes;
    private readonly long _residencyBudgetBytes;
    private readonly Dictionary<string, TexturePage> _pages = new(StringComparer.Ordinal);
    private readonly List<TexturePage> _pending = [];
    private readonly List<TexturePage> _uploadQueue = [];
    private readonly List<TexturePage> _uploading = [];
    private readonly List<TexturePage> _retired = [];
    private readonly List<int> _freeIndices = [];
    private readonly List<TexturePage> _evictionCandidates = [];
    private int _capacity;
    private int _nextIndex = TexturePageCapacity.PlaceholderIndex + 1;
    private int _readyRealPages;
    private int _failedPaths;
    private int _growths;
    private int _evictions;
    private int _reclaimedRetiredPages;
    private int _descriptorSlotReuses;
    private long _catalogVersionSeen;
    private long _frameSequence;
    private long _loadSequence;
    private long _pendingUploadBytes;
    private long _residentPayloadBytes;
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
    /// <param name="catalogVersion">
    /// The asset catalog identity seam, or <see langword="null"/> when the implementation cannot
    /// change its catalog (tests and non-pack assets). A <see langword="null"/> seam never retires
    /// pages.
    /// </param>
    /// <param name="uploadBudgetBytes">
    /// The maximum validated payload bytes whose uploads <see cref="Pump"/> may start per frame.
    /// </param>
    /// <param name="residencyBudgetBytes">
    /// The texture residency budget in bytes. Ready-page payload bytes above it are evicted least
    /// recently seen first, subject to <see cref="ReclaimSafetyAgeFrames"/>. It must be positive.
    /// </param>
    /// <param name="initialCapacity">
    /// The startup table capacity, including the placeholder. The renderer passes its startup
    /// reservation here so a normal run does not grow at the policy's base capacity. It must be at
    /// least <see cref="TexturePageCapacity.Initial"/> and at most
    /// <paramref name="effectiveLimit"/>.
    /// </param>
    /// <exception cref="Bork2dException">The limit cannot support the initial capacity.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="uploadBudgetBytes"/> or <paramref name="residencyBudgetBytes"/> is not
    /// positive, or <paramref name="initialCapacity"/> is outside the limit.
    /// </exception>
    internal TexturePageStore(
        IAssets assets,
        ITexturePageGpu gpu,
        int effectiveLimit,
        int maxImageDimension2D,
        ILogger logger,
        IAssetCatalogVersion? catalogVersion = null,
        int uploadBudgetBytes = DefaultUploadBudgetBytes,
        long residencyBudgetBytes = DefaultResidencyBudgetBytes,
        int initialCapacity = TexturePageCapacity.Initial)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uploadBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(residencyBudgetBytes);

        TexturePageCapacity.EnsureSupportsInitialCapacity(effectiveLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            initialCapacity,
            TexturePageCapacity.Initial);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialCapacity, effectiveLimit);
        _assets = assets;
        _gpu = gpu;
        _logger = logger;
        _limit = effectiveLimit;
        _maxImageDimension2D = maxImageDimension2D;
        _catalogVersion = catalogVersion;
        _catalogVersionSeen = catalogVersion?.CatalogVersion ?? 0;
        _uploadBudgetBytes = uploadBudgetBytes;
        _residencyBudgetBytes = residencyBudgetBytes;
        _capacity = initialCapacity;
    }

    /// <summary>
    /// Gets the ready page count in the current catalog, including the placeholder (public
    /// <c>TexturePages</c>). Pages retired by a mount await residency reclamation and are not counted.
    /// </summary>
    internal int TexturePageCount => (_placeholderReady ? 1 : 0) + _readyRealPages;

    /// <summary>Gets the current capacity in entries, including the placeholder.</summary>
    internal int TexturePageCapacityInEntries => _capacity;

    /// <summary>Gets the number of paths failed in the current catalog (public placeholder accounting).</summary>
    internal int FailedPathCount => _failedPaths;

    /// <summary>Gets the number of descriptor/payload loads still in flight.</summary>
    internal int PendingLoadCount => _pending.Count;

    /// <summary>Gets the number of uploads submitted and not yet complete.</summary>
    internal int UploadingCount => _uploading.Count;

    /// <summary>
    /// Gets the number of content-ready pages queued for upload but not begun (public
    /// <c>TextureUploadsPending</c>).
    /// </summary>
    internal int PendingUploadCount => _uploadQueue.Count;

    /// <summary>
    /// Gets the declared payload bytes of <see cref="PendingUploadCount"/> (public
    /// <c>TextureUploadBytesPending</c>).
    /// </summary>
    internal long PendingUploadBytes => _pendingUploadBytes;

    /// <summary>Gets the configured per-frame upload budget in bytes.</summary>
    internal int UploadBudgetBytes => _uploadBudgetBytes;

    /// <summary>
    /// Gets the configured texture residency budget in bytes. Ready-page payload bytes above it are
    /// evicted least recently seen first.
    /// </summary>
    internal long ResidencyBudgetBytes => _residencyBudgetBytes;

    /// <summary>
    /// Gets the declared payload bytes of the current catalog's ready pages, which the residency
    /// budget bounds. Retired pages awaiting reclamation and uploading pages are not counted.
    /// </summary>
    internal long ResidentPayloadBytes => _residentPayloadBytes;

    /// <summary>Gets the number of ready pages evicted by the residency policy this run.</summary>
    internal int EvictionCount => _evictions;

    /// <summary>
    /// Gets the number of pages retired by a catalog change whose descriptor slot and image memory
    /// the residency policy reclaimed this run.
    /// </summary>
    internal int ReclaimedRetiredPageCount => _reclaimedRetiredPages;

    /// <summary>
    /// Gets the number of retired pages still waiting for the reclamation safety age. They do not
    /// count as resident.
    /// </summary>
    internal int RetiredPageCount => _retired.Count;

    /// <summary>Gets the number of descriptor indices waiting on the free list for reuse.</summary>
    internal int FreeDescriptorSlotCount => _freeIndices.Count;

    /// <summary>
    /// Gets the number of uploads that took a reclaimed descriptor index instead of the next fresh
    /// one this run.
    /// </summary>
    internal int DescriptorSlotReuseCount => _descriptorSlotReuses;

    /// <summary>Gets the number of capacity growths this run.</summary>
    internal int GrowthCount => _growths;

    /// <summary>
    /// Gets the number of completed faulted loads the store observed while shutting down.
    /// </summary>
    /// <remarks>
    /// A teardown diagnostic: a load that completes with a fault between frames but is never
    /// advanced by <see cref="Pump"/> is still observed at <see cref="Destroy"/>, so the runtime
    /// never raises <c>UnobservedTaskException</c> for a page the renderer stopped consuming. The
    /// runtime never reads the value; tests assert the teardown path observed the fault.
    /// </remarks>
    internal int ObservedTeardownFaults { get; private set; }

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
    /// load on first sighting in the current catalog.
    /// </summary>
    /// <remarks>
    /// This is the entry point the M8 draw work calls per candidate sprite. The steady-state path
    /// (an already-known path) is a dictionary lookup returning a value struct, so it allocates
    /// nothing and never blocks. It records the frame as the path's last-seen marker, which the
    /// upload planner uses as the next-frame priority. A null or empty path maps straight to the
    /// placeholder with the placeholder's own 8×8 extent. A catalog change observed here retires the
    /// previous pages first, so the path reloads from the new catalog.
    /// </remarks>
    /// <param name="path">The canonical texture descriptor path, or <see langword="null"/>.</param>
    /// <returns>The page lookup for this frame.</returns>
    internal TexturePageLookup EnsurePage(string? path)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ObserveCatalogVersion();

        if (string.IsNullOrEmpty(path))
        {
            return PlaceholderLookup(declaredWidth: 0, declaredHeight: 0);
        }

        if (_pages.TryGetValue(path, out TexturePage? page))
        {
            page.LastSeenFrame = _frameSequence;
            return LookupFor(page);
        }

        var created = new TexturePage(path)
        {
            LastSeenFrame = _frameSequence,
            LoadSequence = _loadSequence++,
        };
        _pages.Add(path, created);
        StartLoad(created);
        return LookupFor(created);
    }

    /// <summary>
    /// Advances the state machine once per frame: reclaims pages old enough to be safe, consumes
    /// completed loads into the upload queue, enforces the residency budget, starts as many queued
    /// uploads as the per-frame budget admits, and promotes uploads whose fence has signaled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The frame sequence advances before reclamation and queue planning, so the pages the planner
    /// marked during the previous frame are the ones prioritized here and a page reclaimed this
    /// pump was not seen by the two previous pumps' plans.
    /// </para>
    /// <para>
    /// Eviction runs before uploads start so a reclaimed descriptor index can serve this frame's
    /// upload instead of growing the table. A page whose upload completes after the budget check
    /// may leave the resident total above the budget until a later pump retries.
    /// </para>
    /// </remarks>
    /// <exception cref="Bork2dException">
    /// The page capacity reached the device limit, or a Vulkan upload failed.
    /// </exception>
    internal void Pump()
    {
        if (_destroyed)
        {
            return;
        }

        _frameSequence++;
        ObserveCatalogVersion();
        ReclaimRetiredPages();
        AdvanceLoads();
        EnforceResidencyBudget();
        StartQueuedUploads();
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
            ObserveFault(page.DescriptorTask, countTeardownFault: true);
            ObserveFault(page.PayloadTask, countTeardownFault: true);
        }

        _pending.Clear();
        _uploadQueue.Clear();
        _uploading.Clear();
        _retired.Clear();
        _freeIndices.Clear();
        _evictionCandidates.Clear();
        _pages.Clear();
        _pendingUploadBytes = 0;
        _residentPayloadBytes = 0;
        _gpu.Destroy();
    }

    /// <summary>
    /// Retires every page that belongs to the catalog version observed by <see cref="Pump"/> or
    /// <see cref="EnsurePage"/>.
    /// </summary>
    /// <remarks>
    /// The page map, in-flight loads, queued uploads, and failed-path state are dropped; already
    /// submitted uploads keep running so their staging/command resources are released, but they are
    /// marked retired and never become visible or count as ready. Every ready page moves to the
    /// retirement list, which keeps its image resident but drops it from the resident accounting;
    /// the residency policy reclaims it, and its descriptor slot and image-memory range, once the
    /// safety age proves no in-flight frame can sample it. A retired in-flight upload joins the
    /// retirement list when it completes.
    /// </remarks>
    /// <param name="version">The new catalog version.</param>
    private void RetireForCatalogChange(long version)
    {
        _catalogVersionSeen = version;

        foreach (TexturePage page in _pending)
        {
            // The loader may still complete after the mount; the page is no longer consumed, so only
            // observe a later fault. These are not teardown faults.
            ObserveFault(page.DescriptorTask, countTeardownFault: false);
            ObserveFault(page.PayloadTask, countTeardownFault: false);
            page.DescriptorTask = null;
            page.PayloadTask = null;
        }

        foreach (TexturePage page in _pages.Values)
        {
            if (page.State != TexturePageState.Ready)
            {
                continue;
            }

            // A frame recorded before the mount may still sample the image, so the retirement list
            // restarts the safety clock at the retirement pump.
            page.LastSeenFrame = _frameSequence;
            _retired.Add(page);
        }

        foreach (TexturePage page in _uploading)
        {
            page.Retired = true;
        }

        _pending.Clear();
        _uploadQueue.Clear();
        _pages.Clear();
        _readyRealPages = 0;
        _failedPaths = 0;
        _pendingUploadBytes = 0;
        _residentPayloadBytes = 0;
    }

    private void ObserveCatalogVersion()
    {
        if (_catalogVersion is not { } catalog)
        {
            return;
        }

        long version = catalog.CatalogVersion;
        if (version != _catalogVersionSeen)
        {
            RetireForCatalogChange(version);
        }
    }

    private void ObserveFault(Task? task, bool countTeardownFault)
    {
        if (task is null)
        {
            return;
        }

        if (task.IsCompleted)
        {
            // The load completed between frames but Pump never advanced it, so the task may be
            // faulted and unobserved. Reading Exception marks it observed, which keeps teardown
            // from raising UnobservedTaskException for a page the renderer no longer consumes.
            _ = task.Exception;
            if (task.IsFaulted && countTeardownFault)
            {
                ObservedTeardownFaults++;
            }

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

                QueueUpload(page, payload);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Parks a validated page in the upload queue. The page stays <see cref="TexturePageState.Pending"/>
    /// so drawing keeps skipping it until <see cref="StartQueuedUploads"/> admits it.
    /// </summary>
    private void QueueUpload(TexturePage page, byte[] payload)
    {
        page.Payload = payload;
        page.PayloadBytes = payload.Length;
        _uploadQueue.Add(page);
        _pendingUploadBytes += page.PayloadBytes;
    }

    /// <summary>
    /// Starts queued uploads up to the per-frame byte budget, most recently seen pages first.
    /// </summary>
    /// <remarks>
    /// The first upload of a frame always starts, even when it alone exceeds the budget, so a large
    /// texture cannot starve forever; later uploads only start while the frame total stays within
    /// the budget. The sequence advances in <see cref="Pump"/> before this runs, so the markers the
    /// planner set during the previous frame decide this frame's order.
    /// </remarks>
    private void StartQueuedUploads()
    {
        if (_uploadQueue.Count == 0)
        {
            return;
        }

        _uploadQueue.Sort(CompareUploadPriority);
        long startedBytes = 0;
        while (_uploadQueue.Count > 0)
        {
            TexturePage page = _uploadQueue[0];
            long pageBytes = page.PayloadBytes;
            if (startedBytes > 0 && startedBytes + pageBytes > _uploadBudgetBytes)
            {
                break;
            }

            _uploadQueue.RemoveAt(0);
            _pendingUploadBytes -= pageBytes;
            BeginUpload(page);
            startedBytes += pageBytes;
        }
    }

    private static int CompareUploadPriority(TexturePage left, TexturePage right)
    {
        int byLastSeen = right.LastSeenFrame.CompareTo(left.LastSeenFrame);
        return byLastSeen != 0
            ? byLastSeen
            : left.LoadSequence.CompareTo(right.LoadSequence);
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
            _uploading.RemoveAt(index);

            // A retired upload only releases its staging/command resources; it must not appear in
            // the ready count or in any lookup. Its image becomes resident when the GPU completes
            // the copy, so the retirement list reclaims it after the safety age.
            if (page.Retired)
            {
                page.LastSeenFrame = _frameSequence;
                _retired.Add(page);
                continue;
            }

            page.State = TexturePageState.Ready;
            _readyRealPages++;
            _residentPayloadBytes += page.PayloadBytes;
        }
    }

    private void BeginUpload(TexturePage page)
    {
        byte[] payload = page.Payload
            ?? throw new InvalidOperationException("A queued texture page has no payload to upload.");
        page.Payload = null;

        if (_freeIndices.Count > 0)
        {
            // A reclaimed slot is safe to reuse: the reclaim safety age already proved no in-flight
            // frame references its previous page, and the GPU rewrote it to the placeholder.
            page.Index = _freeIndices[^1];
            _freeIndices.RemoveAt(_freeIndices.Count - 1);
            _descriptorSlotReuses++;
        }
        else
        {
            if (_nextIndex >= _capacity)
            {
                Grow();
            }

            page.Index = _nextIndex;
            _nextIndex++;
        }

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

    /// <summary>
    /// Reclaims retired pages whose last frame is older than <see cref="ReclaimSafetyAgeFrames"/>,
    /// returning their descriptor slots and image-memory ranges.
    /// </summary>
    private void ReclaimRetiredPages()
    {
        for (var index = _retired.Count - 1; index >= 0; index--)
        {
            TexturePage page = _retired[index];
            if (!IsSafeToReclaim(page.LastSeenFrame))
            {
                continue;
            }

            _retired.RemoveAt(index);
            _gpu.RetirePage(page.Index);
            _freeIndices.Add(page.Index);
            _reclaimedRetiredPages++;
            _logger.LogDebug(
                "Retired texture page reclaimed: index={Index}, bytes={Bytes}, "
                    + "remainingRetired={RemainingRetired}.",
                page.Index,
                page.PayloadBytes,
                _retired.Count);
        }
    }

    /// <summary>
    /// Evicts ready pages least recently seen first until the resident payload fits the budget.
    /// </summary>
    /// <remarks>
    /// Pages seen by the previous pump or the one before it are never candidates, so the two in-flight
    /// GPU frames can never still sample an evicted page. When every ready page is that recent the
    /// over-budget set is kept and the next pump retries.
    /// </remarks>
    private void EnforceResidencyBudget()
    {
        if (_residentPayloadBytes <= _residencyBudgetBytes)
        {
            return;
        }

        _evictionCandidates.Clear();
        foreach (TexturePage page in _pages.Values)
        {
            if (page.State == TexturePageState.Ready && IsSafeToReclaim(page.LastSeenFrame))
            {
                _evictionCandidates.Add(page);
            }
        }

        if (_evictionCandidates.Count == 0)
        {
            return;
        }

        _evictionCandidates.Sort(CompareEvictionPriority);
        foreach (TexturePage page in _evictionCandidates)
        {
            if (_residentPayloadBytes <= _residencyBudgetBytes)
            {
                break;
            }

            Evict(page);
        }

        _evictionCandidates.Clear();
    }

    private void Evict(TexturePage page)
    {
        _pages.Remove(page.Path);
        _readyRealPages--;
        _residentPayloadBytes -= page.PayloadBytes;
        _gpu.RetirePage(page.Index);
        _freeIndices.Add(page.Index);
        _evictions++;
        _logger.LogDebug(
            "Texture page evicted: path={Path}, index={Index}, bytes={Bytes}, "
                + "residentBytes={ResidentBytes}, residentPages={ResidentPages}.",
            page.Path,
            page.Index,
            page.PayloadBytes,
            _residentPayloadBytes,
            _readyRealPages);
    }

    private bool IsSafeToReclaim(long lastSeenFrame)
        => _frameSequence - lastSeenFrame > ReclaimSafetyAgeFrames;

    private static int CompareEvictionPriority(TexturePage left, TexturePage right)
    {
        int byLastSeen = left.LastSeenFrame.CompareTo(right.LastSeenFrame);
        return byLastSeen != 0
            ? byLastSeen
            : left.LoadSequence.CompareTo(right.LoadSequence);
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

        // Once per failed path until the catalog changes; never per frame or per sprite (M8 §6.10).
        _logger.LogWarning(
            "Texture page '{Path}' failed ({Failure}); the renderer draws the placeholder for this "
                + "path until the asset catalog changes and does not retry it before then.",
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
