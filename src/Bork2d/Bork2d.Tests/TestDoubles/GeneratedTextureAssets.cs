using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic in-memory <see cref="IAssets"/> for real-GPU rendering tests: generated RGBA8
/// textures complete immediately, so the page state machine advances without a baked pack.
/// </summary>
/// <remarks>
/// Register a texture with <see cref="AddTexture"/> (a valid 4x4 magenta payload by default) or
/// <see cref="AddFailedDescriptor"/> (a descriptor with no payload, which fails the page and draws
/// the placeholder). Requesting an unregistered path returns a faulted load, which the page store
/// maps to the failed/placeholder path.
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

    /// <summary>Registers a ready texture descriptor and payload for a path.</summary>
    /// <param name="path">The canonical descriptor path.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The same path, for convenient call sites.</returns>
    internal string AddTexture(string path, uint width = 4, uint height = 4)
    {
        _descriptors[path] = new TextureDescriptor
        {
            Width = width,
            Height = height,
            Format = TextureFormat.Rgba8,
            Filter = TextureFilter.Nearest,
            Wrap = TextureWrap.Clamp,
            Payload = $"data/{path}",
        };
        _payloads[$"data/{path}"] = CreatePixels(width, height);
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

    private static byte[] CreatePixels(uint width, uint height)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
            pixels[offset + 1] = 0;
            pixels[offset + 2] = 255;
            pixels[offset + 3] = 255;
        }

        return pixels;
    }
}
