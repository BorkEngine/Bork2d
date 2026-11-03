using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Loading;
using Bork2d.Runtime.Hosting;
using Microsoft.Extensions.Logging;
using PolyType;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// The <see cref="IAssets"/> seam for real-GPU renderer tests: generated RGBA8 textures complete
/// immediately, while shader assets and SPIR-V resolve through a started <see cref="AssetService"/>
/// that mounts the embedded built-in pack.
/// </summary>
/// <remarks>
/// The renderer now loads its default shader from the asset service at startup, so a real renderer
/// cannot start against <see cref="FakeAssets"/>. This seam keeps the deterministic generated
/// texture path while serving <c>shader/*</c> entries from <see cref="BuiltInAssets.PackBytes"/>.
/// Dispose it after the renderer has joined: it stops the asset service and its loader thread.
/// </remarks>
internal sealed class TestRendererAssets : IAssets, IDisposable
{
    private const string ShaderPathPrefix = "shader/";

    private readonly GeneratedTextureAssets _textures = new();
    private readonly AssetService _service;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CapturingLoggerProvider _provider = new();
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

    /// <summary>Registers a ready texture descriptor and payload for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddTexture(string path, uint width = 4, uint height = 4)
        => _textures.AddTexture(path, width, height);

    /// <summary>Registers a descriptor that has valid dimensions but no payload (a failed page).</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="width">The declared width in pixels.</param>
    /// <param name="height">The declared height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddFailedDescriptor(string path, uint width, uint height)
        => _textures.AddFailedDescriptor(path, width, height);

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(string assetPath)
        where T : IShapeable<T>
    {
        if (typeof(T) == typeof(ShaderAsset))
        {
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
        => IsShaderPath(assetPath)
            ? _service.LoadData(assetPath)
            : _textures.LoadData(assetPath);

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
    public bool Exists(string assetPath) => IsShaderPath(assetPath) && _service.Exists(assetPath);

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

    private static bool IsShaderPath(string assetPath)
        => assetPath.StartsWith(ShaderPathPrefix, StringComparison.Ordinal);
}
