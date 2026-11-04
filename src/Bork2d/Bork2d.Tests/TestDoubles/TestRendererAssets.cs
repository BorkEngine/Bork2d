using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Loading;
using Bork2d.Runtime.Hosting;
using Microsoft.Extensions.Logging;
using PolyType;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// The <see cref="IAssets"/> seam for real-GPU renderer tests: generated RGBA8 textures complete
/// immediately, while the engine-owned built-in assets resolve through a started
/// <see cref="AssetService"/> that mounts the embedded built-in pack.
/// </summary>
/// <remarks>
/// <para>
/// The renderer loads its default and present shaders from the asset service at startup, so a real
/// renderer cannot start against <see cref="FakeAssets"/>. This seam keeps the deterministic
/// generated texture path while serving the <c>shader/*</c> and <c>present/*</c> entries from
/// <see cref="BuiltInAssets.PackBytes"/>. It also serves test-authored custom
/// <see cref="ShaderAsset"/> metadata and raw SPIR-V payloads, including a controllable pending
/// payload, and implements the internal catalog-version seam so a remount can be modelled. Dispose it
/// after the renderer has joined: it stops the asset service and its loader thread.
/// </para>
/// <para>
/// The shader maps are guarded by one lock because the loader thread can read them while the test
/// thread registers a new shader.
/// </para>
/// </remarks>
internal sealed class TestRendererAssets : IAssets, IAssetCatalogVersion, IDisposable
{
    private static readonly string[] _builtInPathPrefixes = ["shader/", "present/"];

    private readonly GeneratedTextureAssets _textures = new();
    private readonly AssetService _service;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CapturingLoggerProvider _provider = new();
    private readonly Lock _shaderLock = new();
    private readonly Dictionary<string, ShaderAsset> _shaderAssets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<byte[]>> _shaderSpirV =
        new(StringComparer.Ordinal);
    private long _catalogVersion;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="TestRendererAssets"/> class.</summary>
    internal TestRendererAssets()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(_provider);
        });
        _service = new AssetService(
            basePackPath: null,
            _loggerFactory,
            BuiltInAssets.PackBytes);
        _service.Start();
    }

    /// <summary>Gets the captured asset-service logs.</summary>
    internal CapturingLoggerProvider Provider => _provider;

    /// <summary>Gets the current catalog version.</summary>
    internal long CatalogVersion
    {
        get
        {
            lock (_shaderLock)
            {
                return _catalogVersion;
            }
        }
    }

    long IAssetCatalogVersion.CatalogVersion => CatalogVersion;

    /// <summary>Registers a ready texture descriptor and payload for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddTexture(string path, uint width = 4, uint height = 4)
        => _textures.AddTexture(path, width, height);

    /// <summary>Registers a ready solid-color RGBA8 texture for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="r">The sRGB red byte.</param>
    /// <param name="g">The sRGB green byte.</param>
    /// <param name="b">The sRGB blue byte.</param>
    /// <param name="a">The alpha byte.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddSolidTexture(
        string path,
        byte r,
        byte g,
        byte b,
        byte a = 255,
        uint width = 4,
        uint height = 4)
        => _textures.AddSolidTexture(path, r, g, b, a, width, height);

    /// <summary>Registers a descriptor that has valid dimensions but no payload (a failed page).</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="width">The declared width in pixels.</param>
    /// <param name="height">The declared height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddFailedDescriptor(string path, uint width, uint height)
        => _textures.AddFailedDescriptor(path, width, height);

    /// <summary>Registers a custom shader whose SPIR-V payload completes immediately.</summary>
    /// <param name="assetPath">The shader asset path (the captured sprite keys the raw path).</param>
    /// <param name="asset">The shader metadata.</param>
    /// <param name="spirV">The compiled fragment module.</param>
    /// <returns>The same asset path, for convenient call sites.</returns>
    internal string AddShader(string assetPath, ShaderAsset asset, byte[] spirV)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(spirV);
        string rawPath = asset.SpirV.Path
            ?? throw new ArgumentException("The shader asset has no SPIR-V reference.", nameof(asset));
        var source = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(spirV);
        lock (_shaderLock)
        {
            _shaderAssets[assetPath] = asset;
            _shaderSpirV[rawPath] = source;
        }

        return assetPath;
    }

    /// <summary>Registers a custom shader with a controllable SPIR-V payload.</summary>
    /// <param name="assetPath">The shader asset path (the captured sprite keys the raw path).</param>
    /// <param name="asset">The shader metadata.</param>
    /// <returns>The same asset path, for convenient call sites.</returns>
    internal string AddPendingShader(string assetPath, ShaderAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string rawPath = asset.SpirV.Path
            ?? throw new ArgumentException("The shader asset has no SPIR-V reference.", nameof(asset));
        lock (_shaderLock)
        {
            _shaderAssets[assetPath] = asset;
            _shaderSpirV[rawPath] = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return assetPath;
    }

    /// <summary>Completes a pending custom shader's SPIR-V payload.</summary>
    /// <param name="assetPath">The asset path passed to <see cref="AddPendingShader"/>.</param>
    /// <param name="spirV">The compiled fragment module.</param>
    internal void CompleteShader(string assetPath, byte[] spirV)
    {
        ArgumentNullException.ThrowIfNull(spirV);
        TaskCompletionSource<byte[]> source;
        lock (_shaderLock)
        {
            ShaderAsset asset = _shaderAssets[assetPath];
            source = _shaderSpirV[asset.SpirV.Path!];
        }

        source.SetResult(spirV);
    }

    /// <summary>Increments the catalog version, modelling an overlay mount.</summary>
    internal void MountCatalog()
    {
        lock (_shaderLock)
        {
            _catalogVersion++;
        }
    }

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(string assetPath)
        where T : IShapeable<T>
    {
        if (typeof(T) == typeof(ShaderAsset))
        {
            lock (_shaderLock)
            {
                if (_shaderAssets.TryGetValue(assetPath, out ShaderAsset? shader))
                {
                    return Task.FromResult((T)(object)shader);
                }
            }

            return _service.LoadAsset<T>(assetPath);
        }

        return _textures.LoadAsset<T>(assetPath);
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
        lock (_shaderLock)
        {
            if (_shaderSpirV.TryGetValue(assetPath, out TaskCompletionSource<byte[]>? source))
            {
                return source.Task;
            }
        }

        return IsBuiltInAssetPath(assetPath)
            ? _service.LoadData(assetPath)
            : _textures.LoadData(assetPath);
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(DataRef reference)
        => reference.HasValue
            ? LoadData(reference.Path!)
            : throw new ArgumentException("The reference is absent.", nameof(reference));

    /// <inheritdoc/>
    public void LoadPack(string packPath) => _service.LoadPack(packPath);

    /// <inheritdoc/>
    public IReadOnlyList<AssetInfo> Browse(string? prefix = null) => _service.Browse(prefix);

    /// <inheritdoc/>
    public bool Exists(string assetPath)
    {
        lock (_shaderLock)
        {
            if (_shaderAssets.ContainsKey(assetPath))
            {
                return true;
            }
        }

        return IsBuiltInAssetPath(assetPath) && _service.Exists(assetPath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _service.Dispose();
        _loggerFactory.Dispose();
    }

    private static bool IsBuiltInAssetPath(string assetPath)
    {
        foreach (string prefix in _builtInPathPrefixes)
        {
            if (assetPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
