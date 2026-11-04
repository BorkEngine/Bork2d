using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic <see cref="IAssets"/> and catalog seam for GPU-free shader-catalog tests: the
/// default shader asset is mutable, custom SPIR-V payloads are completed explicitly, and incrementing
/// <see cref="CatalogVersion"/> models an overlay mount.
/// </summary>
/// <remarks>
/// Custom shader assets are never loaded by the renderer (the sprite carries the frozen metadata),
/// so only the default path serves a <see cref="ShaderAsset"/>. Every SPIR-V path serves a
/// controllable task, so a test can keep a shader pending, complete it with valid bytes, or fault
/// it without sleeps.
/// </remarks>
internal sealed class FakeShaderAssets : IAssets, IAssetCatalogVersion
{
    private readonly Dictionary<string, TaskCompletionSource<byte[]>> _spirV =
        new(StringComparer.Ordinal);
    private readonly List<string> _loadedAssetPaths = [];
    private readonly List<string> _loadedDataPaths = [];

    /// <summary>Gets or sets the built-in sprite default shader asset served at <c>shader/sprite</c>.</summary>
    internal ShaderAsset? DefaultAsset { get; set; } = new()
    {
        SpirV = "shader/sprite-spv",
        LayoutVersion = ShaderLayout.CurrentVersion,
        EntryPoint = "main",
    };

    /// <summary>Gets or sets the built-in line default shader asset served at <c>shader/line</c>.</summary>
    internal ShaderAsset? LineDefaultAsset { get; set; } = new()
    {
        SpirV = "shader/line-spv",
        LayoutVersion = ShaderLayout.CurrentVersion,
        EntryPoint = "main",
    };

    /// <summary>Gets or sets the catalog version; incrementing it models a pack mount.</summary>
    internal long CatalogVersion { get; set; }

    /// <summary>Gets the asset paths requested through <see cref="LoadAsset{T}(string)"/>.</summary>
    internal IReadOnlyList<string> LoadedAssetPaths => _loadedAssetPaths;

    /// <summary>Gets the raw-data paths requested through <see cref="LoadData(string)"/>.</summary>
    internal IReadOnlyList<string> LoadedDataPaths => _loadedDataPaths;

    /// <summary>Gets or sets an exception thrown synchronously by an asset load.</summary>
    internal Exception? AssetLoadException { get; set; }

    /// <summary>Gets or sets an exception thrown synchronously by a data load.</summary>
    internal Exception? DataLoadException { get; set; }

    long IAssetCatalogVersion.CatalogVersion => CatalogVersion;

    /// <summary>Gets the controllable SPIR-V load for a path, creating it on first use.</summary>
    /// <param name="path">The canonical raw-data path.</param>
    /// <returns>The source the test drives.</returns>
    internal TaskCompletionSource<byte[]> SpirVSource(string path)
    {
        if (!_spirV.TryGetValue(path, out TaskCompletionSource<byte[]>? source))
        {
            // Completing the source runs the catalog's async load continuation inline, so a test can
            // assert the next Pump's promotion deterministically.
            source = new TaskCompletionSource<byte[]>();
            _spirV.Add(path, source);
        }

        return source;
    }

    /// <summary>Completes a SPIR-V load with valid bytes.</summary>
    /// <param name="path">The canonical raw-data path.</param>
    /// <param name="bytes">The payload; defaults to a minimal SPIR-V module.</param>
    internal void CompleteSpirV(string path, byte[]? bytes = null)
        => SpirVSource(path).SetResult(bytes ?? CreateSpirV());

    /// <summary>Faults a SPIR-V load.</summary>
    /// <param name="path">The canonical raw-data path.</param>
    /// <param name="exception">The failure to surface.</param>
    internal void FailSpirV(string path, Exception exception)
        => SpirVSource(path).SetException(exception);

    /// <summary>
    /// Forgets a completed SPIR-V load so a later mount starts a fresh controllable task for the
    /// path's new winning content.
    /// </summary>
    /// <param name="path">The canonical raw-data path.</param>
    internal void ResetSpirV(string path) => _spirV.Remove(path);

    /// <summary>Creates a minimal valid SPIR-V payload (magic plus one dummy word).</summary>
    /// <returns>The payload bytes.</returns>
    internal static byte[] CreateSpirV()
    {
        var bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            Bork2d.Render.Shaders.ShaderStore.SpirVMagic);
        bytes[4] = 1;
        return bytes;
    }

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(string assetPath)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        _loadedAssetPaths.Add(assetPath);
        if (AssetLoadException is { } exception)
        {
            throw exception;
        }

        if (typeof(T) != typeof(ShaderAsset))
        {
            return Task.FromException<T>(
                new AssetException(
                    AssetErrorKind.NotFound,
                    assetPath,
                    $"No shader asset at '{assetPath}'."));
        }

        ShaderAsset? asset = string.Equals(
            assetPath,
            Bork2d.Render.Shaders.ShaderStore.LineShaderPath,
            StringComparison.Ordinal)
            ? LineDefaultAsset
            : DefaultAsset;
        if (asset is null)
        {
            return Task.FromException<T>(
                new AssetException(
                    AssetErrorKind.NotFound,
                    assetPath,
                    $"No shader asset at '{assetPath}'."));
        }

        return Task.FromResult((T)(object)asset);
    }

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(AssetRef<T> reference)
        where T : IShapeable<T>
        => reference.HasValue
            ? LoadAsset<T>(reference.Path!)
            : throw new ArgumentException("The reference is absent.", nameof(reference));

    /// <inheritdoc/>
    public Task<byte[]> LoadData(string assetPath)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        _loadedDataPaths.Add(assetPath);
        if (DataLoadException is { } exception)
        {
            throw exception;
        }

        return SpirVSource(assetPath).Task;
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(DataRef reference)
        => reference.HasValue
            ? LoadData(reference.Path!)
            : throw new ArgumentException("The reference is absent.", nameof(reference));

    /// <inheritdoc/>
    public void LoadPack(string packPath)
        => throw new NotSupportedException("Shader-catalog tests do not mount packs directly.");

    /// <inheritdoc/>
    public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
        => throw new NotSupportedException("Shader-catalog tests do not browse assets.");

    /// <inheritdoc/>
    public bool Exists(string assetPath)
        => throw new NotSupportedException("Shader-catalog tests do not query assets.");
}
