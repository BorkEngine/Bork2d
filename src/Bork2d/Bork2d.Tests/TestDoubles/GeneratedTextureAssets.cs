using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Bork2d.Tests.TestSupport;
using PolyType;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic in-memory <see cref="IAssets"/> for real-GPU rendering tests: generated KTX2
/// textures complete immediately, so the page state machine advances without a baked pack.
/// </summary>
/// <remarks>
/// Register a texture with <see cref="AddTexture(string, TextureFormat, byte, byte, byte, byte, uint, uint)"/>
/// (a valid 4x4 magenta Rgba8 texture by default) or <see cref="AddFailedDescriptor"/> (a descriptor
/// with no payload, which fails the page and draws the placeholder). Requesting an unregistered path
/// returns a faulted load, which the page store maps to the failed/placeholder path. Every payload is
/// produced by the bake's <see cref="TextureBaker"/>, so it is a complete KTX2 file with a full mip
/// chain that the runtime reader accepts.
/// </remarks>
internal sealed class GeneratedTextureAssets : IAssets
{
    private readonly Dictionary<string, TextureDescriptor> _descriptors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _payloads = new(StringComparer.Ordinal);

    /// <summary>Gets the number of descriptor loads requested.</summary>
    internal int DescriptorLoadCount { get; private set; }

    /// <summary>Gets the number of payload loads requested.</summary>
    internal int PayloadLoadCount { get; private set; }

    /// <summary>Registers a ready Rgba8 texture descriptor and KTX2 payload for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddTexture(string path, uint width = 4, uint height = 4)
        => AddSolidTexture(path, 255, 0, 255, 255, width, height);

    /// <summary>Registers a ready solid-color Rgba8 texture for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="r">The linear red byte.</param>
    /// <param name="g">The linear green byte.</param>
    /// <param name="b">The linear blue byte.</param>
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
        => AddTexture(path, TextureFormat.Rgba8, r, g, b, a, width, height);

    /// <summary>Registers a ready solid-color texture in a concrete format for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="format">The concrete format the baker must produce.</param>
    /// <param name="r">The linear red byte.</param>
    /// <param name="g">The linear green byte.</param>
    /// <param name="b">The linear blue byte.</param>
    /// <param name="a">The alpha byte.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddTexture(
        string path,
        TextureFormat format,
        byte r,
        byte g,
        byte b,
        byte a = 255,
        uint width = 4,
        uint height = 4)
    {
        BakedTexture baked = Ktx2TextureFactory.BakeSolid(
            format,
            (int)width,
            (int)height,
            r,
            g,
            b,
            a);
        _descriptors[path] = Ktx2TextureFactory.Descriptor(
            $"data/{path}",
            baked,
            width,
            height);
        _payloads[$"data/{path}"] = baked.Payload.ToArray();
        return path;
    }

    /// <summary>Registers a baked texture payload and descriptor verbatim.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="baked">The baked KTX2 payload and descriptor values.</param>
    /// <param name="width">The base width in pixels.</param>
    /// <param name="height">The base height in pixels.</param>
    /// <param name="filter">The sampler filter.</param>
    /// <param name="wrap">The sampler wrap.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddBakedTexture(
        string path,
        BakedTexture baked,
        uint width,
        uint height,
        TextureFilter filter = TextureFilter.Nearest,
        TextureWrap wrap = TextureWrap.Clamp)
    {
        ArgumentNullException.ThrowIfNull(baked);
        _descriptors[path] = Ktx2TextureFactory.Descriptor(
            $"data/{path}",
            baked,
            width,
            height,
            filter,
            wrap);
        _payloads[$"data/{path}"] = baked.Payload.ToArray();
        return path;
    }

    /// <summary>Registers a descriptor that has valid dimensions but no payload (a failed page).</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="width">The declared width in pixels.</param>
    /// <param name="height">The declared height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddFailedDescriptor(string path, uint width, uint height)
    {
        _descriptors[path] = new TextureDescriptor
        {
            Width = width,
            Height = height,
            Format = TextureFormat.Rgba8,
            Filter = TextureFilter.Nearest,
            Wrap = TextureWrap.Clamp,
            Payload = DataRef.None,
        };
        return path;
    }

    /// <inheritdoc/>
    public Task<T> LoadAsset<T>(string assetPath)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        if (typeof(T) != typeof(TextureDescriptor))
        {
            throw new NotSupportedException(
                $"GeneratedTextureAssets only loads {nameof(TextureDescriptor)} assets, not "
                    + $"{typeof(T)}.");
        }

        DescriptorLoadCount++;
        if (!_descriptors.TryGetValue(assetPath, out TextureDescriptor? descriptor))
        {
            return Task.FromException<T>(
                new KeyNotFoundException($"No generated texture descriptor at '{assetPath}'."));
        }

        return Task.FromResult((T)(object)descriptor);
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
        PayloadLoadCount++;
        return _payloads.TryGetValue(assetPath, out byte[]? payload)
            ? Task.FromResult(payload)
            : Task.FromException<byte[]>(
                new KeyNotFoundException($"No generated texture payload at '{assetPath}'."));
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
        "Generated texture tests do not mount packs.");

    /// <inheritdoc/>
    public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
        => throw new NotSupportedException("Generated texture tests do not browse assets.");

    /// <inheritdoc/>
    public bool Exists(string assetPath)
        => throw new NotSupportedException("Generated texture tests do not query assets.");
}
