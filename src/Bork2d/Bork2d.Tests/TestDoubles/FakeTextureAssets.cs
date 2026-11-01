using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic <see cref="IAssets"/> for texture-page tests: every descriptor/payload load
/// returns a task the test completes explicitly, so a pending page is modelled without sleeps.
/// </summary>
/// <remarks>
/// The fake records every path and call count, so tests can prove one load per page and no retry
/// within a catalog. It also implements the catalog-version seam so a test can model an overlay
/// mount deterministically.
/// </remarks>
internal sealed class FakeTextureAssets : IAssets, IAssetCatalogVersion
{
    private readonly Dictionary<string, TaskCompletionSource<TextureDescriptor>> _descriptors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<byte[]>> _payloads =
        new(StringComparer.Ordinal);
    private readonly List<string> _loadAssetPaths = [];
    private readonly List<string> _loadDataPaths = [];

    /// <summary>
    /// Gets or sets the catalog version reported to the page store. Incrementing it models an
    /// overlay mount that changes which entry a path resolves to.
    /// </summary>
    internal long CatalogVersion { get; set; }

    long IAssetCatalogVersion.CatalogVersion => CatalogVersion;

    /// <summary>Gets the number of <see cref="LoadAsset{T}"/> calls.</summary>
    internal int LoadAssetCount => _loadAssetPaths.Count;

    /// <summary>Gets the number of <see cref="LoadData(string)"/> calls.</summary>
    internal int LoadDataCount => _loadDataPaths.Count;

    /// <summary>Gets the descriptor paths requested, in call order.</summary>
    internal IReadOnlyList<string> LoadAssetPaths => _loadAssetPaths;

    /// <summary>Gets the payload paths requested, in call order.</summary>
    internal IReadOnlyList<string> LoadDataPaths => _loadDataPaths;

    /// <summary>Gets or sets an exception thrown synchronously by descriptor loads.</summary>
    internal Exception? DescriptorLoadException { get; set; }

    /// <summary>Gets or sets an exception thrown synchronously by payload loads.</summary>
    internal Exception? PayloadLoadException { get; set; }

    /// <summary>Gets the controllable descriptor load for a path, creating it on first use.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <returns>The completion source the test drives.</returns>
    internal TaskCompletionSource<TextureDescriptor> DescriptorSource(string path)
    {
        if (!_descriptors.TryGetValue(path, out TaskCompletionSource<TextureDescriptor>? source))
        {
            source = new TaskCompletionSource<TextureDescriptor>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _descriptors.Add(path, source);
        }

        return source;
    }

    /// <summary>Gets the controllable payload load for a path, creating it on first use.</summary>
    /// <param name="path">The canonical payload path.</param>
    /// <returns>The completion source the test drives.</returns>
    internal TaskCompletionSource<byte[]> PayloadSource(string path)
    {
        if (!_payloads.TryGetValue(path, out TaskCompletionSource<byte[]>? source))
        {
            source = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _payloads.Add(path, source);
        }

        return source;
    }

    /// <summary>Completes the descriptor load for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="descriptor">The descriptor value.</param>
    internal void CompleteDescriptor(string path, TextureDescriptor descriptor)
        => DescriptorSource(path).SetResult(descriptor);

    /// <summary>Completes the payload load for a path.</summary>
    /// <param name="path">The canonical payload path.</param>
    /// <param name="payload">The payload bytes.</param>
    internal void CompletePayload(string path, byte[] payload)
        => PayloadSource(path).SetResult(payload);

    /// <summary>
    /// Forgets a descriptor load so the next request starts a fresh controllable task. Tests use
    /// this after changing the catalog version to model a path that now resolves to new content.
    /// </summary>
    /// <param name="path">The canonical descriptor path.</param>
    internal void ResetDescriptor(string path) => _descriptors.Remove(path);

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(string assetPath)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        if (typeof(T) != typeof(TextureDescriptor))
        {
            throw new NotSupportedException(
                $"FakeTextureAssets only loads {nameof(TextureDescriptor)} assets, not {typeof(T)}.");
        }

        _loadAssetPaths.Add(assetPath);
        if (DescriptorLoadException is { } exception)
        {
            throw exception;
        }

        return (Task<T>)(object)DescriptorSource(assetPath).Task;
    }

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(AssetRef<T> reference)
        where T : IShapeable<T>
    {
        if (!reference.HasValue)
        {
            throw new ArgumentException("The reference is absent.", nameof(reference));
        }

        return LoadAsset<T>(reference.Path!);
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(string assetPath)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        _loadDataPaths.Add(assetPath);
        if (PayloadLoadException is { } exception)
        {
            throw exception;
        }

        return PayloadSource(assetPath).Task;
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(DataRef reference)
    {
        if (!reference.HasValue)
        {
            throw new ArgumentException("The reference is absent.", nameof(reference));
        }

        return LoadData(reference.Path!);
    }

    /// <inheritdoc/>
    public void LoadPack(string packPath) => throw new NotSupportedException(
        "Texture-page tests do not mount packs.");

    /// <inheritdoc/>
    public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
        => throw new NotSupportedException("Texture-page tests do not browse assets.");

    /// <inheritdoc/>
    public bool Exists(string assetPath)
        => throw new NotSupportedException("Texture-page tests do not query assets.");
}
