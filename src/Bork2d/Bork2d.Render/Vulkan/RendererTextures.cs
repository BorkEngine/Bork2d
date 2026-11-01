using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The sprite pipeline, texture-page store, and bindless page writer shared by the windowed and
/// offscreen renderers.
/// </summary>
/// <remarks>
/// <para>
/// Both target drivers create the same sprite pipeline per output format, the same asynchronous
/// <see cref="TexturePageStore"/>, and the same production <see cref="VulkanTexturePages"/> GPU seam;
/// they also wait the device idle and rewrite every frame's bindless descriptor set when the page
/// capacity grows. This composition owns that setup and teardown.
/// </para>
/// <para>
/// The target driver still creates the frames (their descriptor sets come from
/// <see cref="SpritePipeline.DescriptorSetLayout"/>) and records its own draws. The composition only
/// implements <see cref="IBindlessPageWriter"/> by forwarding capacity growth and page writes to the
/// shared frame set, which is the narrow target adapter.
/// </para>
/// </remarks>
internal sealed class RendererTextures : IBindlessPageWriter
{
    private readonly VulkanDevice _device;
    private readonly IVulkanInstance? _instance;
    private readonly IAssets _assets;
    private readonly ILogger _logger;
    private readonly IVulkanFrameSet _frames;
    private readonly IAssetCatalogVersion? _assetCatalogVersion;
    private readonly long _textureResidencyBudgetBytes;
    private readonly int _textureUploadBudgetBytes;
    private readonly string _modeDescription;
    private SpritePipeline? _pipeline;
    private VulkanTexturePages? _gpu;
    private TexturePageStore? _pages;
    private int _stopLogPageCount;
    private int _stopLogPageCapacity;
    private int _stopLogPageGrowths;
    private int _stopLogFailedPaths;
    private int _destroyed;

    /// <summary>Initializes a new instance of the <see cref="RendererTextures"/> class.</summary>
    /// <param name="device">The selected logical device.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="assets">The app's asset service used for asynchronous texture loads.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger.</param>
    /// <param name="frames">The renderer's GPU frames in flight; the composition does not own them.</param>
    /// <param name="modeDescription">The renderer mode name used in startup failures.</param>
    /// <param name="assetCatalogVersion">
    /// The asset catalog identity seam, or <see langword="null"/> when the assets cannot change.
    /// </param>
    /// <param name="textureUploadBudgetBytes">The per-frame texture upload budget in bytes.</param>
    /// <param name="textureResidencyBudgetBytes">The texture residency budget in bytes.</param>
    internal RendererTextures(
        VulkanDevice device,
        IVulkanInstance? instance,
        IAssets assets,
        ILogger logger,
        IVulkanFrameSet frames,
        string modeDescription,
        IAssetCatalogVersion? assetCatalogVersion = null,
        int textureUploadBudgetBytes = TexturePageStore.DefaultUploadBudgetBytes,
        long textureResidencyBudgetBytes = TexturePageStore.DefaultResidencyBudgetBytes)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrEmpty(modeDescription);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureUploadBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureResidencyBudgetBytes);

        _device = device;
        _instance = instance;
        _assets = assets;
        _logger = logger;
        _frames = frames;
        _modeDescription = modeDescription;
        _assetCatalogVersion = assetCatalogVersion;
        _textureUploadBudgetBytes = textureUploadBudgetBytes;
        _textureResidencyBudgetBytes = textureResidencyBudgetBytes;
    }

    /// <summary>Gets the sprite pipeline owner, or <see langword="null"/> before startup.</summary>
    internal SpritePipeline? Pipeline => _pipeline;

    /// <summary>Gets the texture-page cache, or <see langword="null"/> before startup.</summary>
    internal TexturePageStore? Pages => _pages;

    /// <summary>Gets the bindless limit reported by the sprite pipeline, or <c>0</c>.</summary>
    internal uint MaxTexturePages => _pipeline?.MaxTexturePages ?? 0;

    /// <summary>Gets the page count recorded for the stop log.</summary>
    internal int StopLogPageCount => _stopLogPageCount;

    /// <summary>Gets the page capacity recorded for the stop log.</summary>
    internal int StopLogPageCapacity => _stopLogPageCapacity;

    /// <summary>Gets the page growth count recorded for the stop log.</summary>
    internal int StopLogPageGrowths => _stopLogPageGrowths;

    /// <summary>Gets the failed-path count recorded for the stop log.</summary>
    internal int StopLogFailedPaths => _stopLogFailedPaths;

    /// <summary>
    /// Creates the sprite pipeline owner on first use and the pipeline for the output format.
    /// </summary>
    /// <param name="format">The target's sRGB output color format.</param>
    /// <exception cref="Bork2dException">The pipeline could not be created.</exception>
    internal void EnsurePipeline(Format format)
    {
        SpritePipeline pipeline = _pipeline ??= SpritePipeline.Create(_device, _instance, _logger);
        pipeline.GetOrCreatePipeline(format);
    }

    /// <summary>
    /// Creates the texture-page cache, uploads the placeholder (waiting its fence at startup), and
    /// makes it resident at index 0. Failure is a renderer startup failure.
    /// </summary>
    /// <exception cref="Bork2dException">The page store could not be created.</exception>
    internal void CreatePageStore()
    {
        SpritePipeline pipeline = _pipeline
            ?? throw new Bork2dException(
                $"The {_modeDescription} renderer cannot create texture pages without the sprite "
                    + "pipeline.");
        _gpu = new VulkanTexturePages(_device, pipeline, this, _instance, _logger);
        _pages = new TexturePageStore(
            _assets,
            _gpu,
            (int)pipeline.MaxTexturePages,
            (int)_device.Limits.MaxImageDimension2D,
            _logger,
            _assetCatalogVersion,
            _textureUploadBudgetBytes,
            _textureResidencyBudgetBytes);
        _pages.Start();
    }

    /// <summary>
    /// Advances the texture-page state machine once per frame, before recording, so completed loads
    /// prepare uploads and completed uploads write their bindless descriptor.
    /// </summary>
    internal void Pump() => _pages!.Pump();

    /// <summary>Records the page counters before the page store is destroyed for the stop log.</summary>
    internal void CaptureStopCounters()
    {
        TexturePageStore? pages = _pages;
        if (pages is null)
        {
            return;
        }

        _stopLogPageCount = pages.TexturePageCount;
        _stopLogPageCapacity = pages.TexturePageCapacityInEntries;
        _stopLogPageGrowths = pages.GrowthCount;
        _stopLogFailedPaths = pages.FailedPathCount;
    }

    /// <summary>Destroys the pages and the pipeline in dependency order; idempotent and never throws.</summary>
    /// <remarks>
    /// Frame descriptor sets/pools are destroyed by the frame set before this runs, so the page
    /// images/views they reference can be freed before the pipeline that owns their samplers.
    /// </remarks>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        _pages?.Destroy();
        _pages = null;
        _gpu = null;

        _pipeline?.Destroy();
        _pipeline = null;
    }

    void IBindlessPageWriter.EnsureCapacity(int capacity)
        => _frames.EnsureBindlessCapacity(_device, capacity);

    void IBindlessPageWriter.WritePage(int pageIndex, ImageView imageView, Sampler sampler)
        => _frames.WritePage(pageIndex, imageView, sampler);
}
