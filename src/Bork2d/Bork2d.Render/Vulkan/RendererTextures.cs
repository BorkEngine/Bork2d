using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Shaders;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The sprite pipeline, default shader store, texture-page store, and bindless page writer shared by
/// the windowed and offscreen renderers.
/// </summary>
/// <remarks>
/// <para>
/// Both target drivers create one sprite pipeline for the linear scene color format, the same
/// default <see cref="ShaderStore"/> loaded from the built-in pack, the same asynchronous
/// <see cref="TexturePageStore"/>, and the same production <see cref="VulkanTexturePages"/> GPU seam;
/// they also retire every in-flight frame fence and rewrite every frame's bindless descriptor set
/// when the page capacity grows. This composition owns that setup and teardown.
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
    private ShaderStore? _shaders;
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

    /// <summary>Gets the default shader store, or <see langword="null"/> before startup.</summary>
    internal ShaderStore? Shaders => _shaders;

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
    /// Creates the sprite pipeline owner, the shader catalog, and the default pipeline for the
    /// linear scene color format on first use; a later format creates only that format's default
    /// pipeline.
    /// </summary>
    /// <param name="format">The linear scene color format the 2D pipelines render into.</param>
    /// <exception cref="Bork2dException">
    /// The vertex module, default shader asset/module, or graphics pipeline is missing or invalid.
    /// </exception>
    internal void EnsurePipeline(Format format)
    {
        if (_pipeline is null)
        {
            byte[] vertexSpirV = ShaderStore.LoadSpirV(
                _assets,
                ShaderStore.VertexSpirVPath,
                "the built-in sprite vertex module");
            _pipeline = SpritePipeline.Create(_device, _instance, _logger, vertexSpirV);
            _shaders = ShaderStore.Create(_assets, _pipeline, _assetCatalogVersion, _logger);
        }

        _shaders!.EnsureDefaultPipeline(format);
    }

    /// <summary>
    /// Creates the texture-page cache, loads the built-in placeholder descriptor and KTX2 payload
    /// synchronously from <see cref="IAssets"/>, uploads the placeholder (waiting its fence at
    /// startup), and makes it resident at index 0. The built-in pack is always mounted, so a missing
    /// or invalid placeholder is a renderer startup failure.
    /// </summary>
    /// <param name="initialCapacity">
    /// The startup table capacity from <see cref="TexturePageCapacity.StartupCapacity(int)"/>,
    /// including the placeholder. It is passed to the page store so a normal run starts already
    /// reserved instead of growing at the policy's base capacity.
    /// </param>
    /// <exception cref="Bork2dException">
    /// The page store could not be created, or the built-in placeholder descriptor/payload is
    /// missing or invalid.
    /// </exception>
    internal void CreatePageStore(int initialCapacity)
    {
        SpritePipeline pipeline = _pipeline
            ?? throw new Bork2dException(
                $"The {_modeDescription} renderer cannot create texture pages without the sprite "
                    + "pipeline.");
        TextureDescriptor placeholder = LoadBlocking(
            _assets.LoadAsset<TextureDescriptor>(TexturePlaceholder.DescriptorPath),
            TexturePlaceholder.DescriptorPath,
            "the built-in texture placeholder descriptor");
        if (placeholder is null)
        {
            throw new Bork2dException(
                $"The built-in texture placeholder descriptor '{TexturePlaceholder.DescriptorPath}' "
                    + "loaded as null.");
        }

        if (!placeholder.Payload.HasValue)
        {
            throw new Bork2dException(
                $"The built-in texture placeholder descriptor '{TexturePlaceholder.DescriptorPath}' "
                    + "has no KTX2 payload reference.");
        }

        byte[] payload = LoadBlocking(
            _assets.LoadData(placeholder.Payload),
            placeholder.Payload.Path!,
            "the built-in texture placeholder payload");

        _gpu = new VulkanTexturePages(_device, pipeline, this, _instance, _logger);
        _pages = new TexturePageStore(
            _assets,
            _gpu,
            (int)pipeline.MaxTexturePages,
            (int)_device.Limits.MaxImageDimension2D,
            _logger,
            _assetCatalogVersion,
            _textureUploadBudgetBytes,
            _textureResidencyBudgetBytes,
            initialCapacity);
        _pages.Start(placeholder, payload);
    }

    private static T LoadBlocking<T>(Task<T> task, string path, string description)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new Bork2dException(
                $"{description} asset '{path}' could not be loaded: {exception.Message}",
                exception);
        }
    }


    /// <summary>
    /// Advances the shader catalog and the texture-page state machine once per frame, before
    /// recording, so completed loads prepare the next frame's modules/uploads and completed uploads
    /// write their bindless descriptor.
    /// </summary>
    internal void Pump()
    {
        _shaders!.Pump();
        _pages!.Pump();
    }

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

    /// <summary>
    /// Destroys the pages, the default shader module, and the pipeline in dependency order;
    /// idempotent and never throws.
    /// </summary>
    /// <remarks>
    /// Frame descriptor sets/pools are destroyed by the frame set before this runs, so the page
    /// images/views they reference can be freed before the pipeline that owns their samplers. The
    /// shader store is disposed before the pipeline; destroying a module still referenced by a
    /// pipeline object is safe, and the store may not be used after this point.
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

        _shaders?.Dispose();
        _shaders = null;

        _pipeline?.Destroy();
        _pipeline = null;
    }

    void IBindlessPageWriter.EnsureCapacity(int capacity)
        => _frames.EnsureBindlessCapacity(_device, capacity);

    void IBindlessPageWriter.WritePage(int pageIndex, ImageView imageView, Sampler sampler)
        => _frames.WritePage(pageIndex, imageView, sampler);
}
