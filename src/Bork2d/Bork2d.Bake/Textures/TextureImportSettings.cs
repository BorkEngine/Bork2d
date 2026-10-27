using Bork2d.Bake.Project;

namespace Bork2d.Bake.Textures;

/// <summary>
/// The resolved import settings for one texture: the sampler state plus where it came from.
/// </summary>
/// <remarks>
/// Missing sidecar values fall back to the project descriptor's <c>[textures]</c> defaults. An
/// overridden value keeps the sidecar value span; a defaulted value has no span.
/// </remarks>
internal sealed class TextureImportSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextureImportSettings"/> class.
    /// </summary>
    /// <param name="filter">The sampler filter.</param>
    /// <param name="wrap">The sampler wrap mode.</param>
    /// <param name="sidecarPath">The absolute sidecar path, or <see langword="null"/> when no sidecar applied.</param>
    /// <param name="filterSpan">The sidecar <c>filter</c> span, or <see langword="null"/> when defaulted.</param>
    /// <param name="wrapSpan">The sidecar <c>wrap</c> span, or <see langword="null"/> when defaulted.</param>
    internal TextureImportSettings(
        BakeTextureFilter filter,
        BakeTextureWrap wrap,
        string? sidecarPath,
        TextureSourceSpan? filterSpan,
        TextureSourceSpan? wrapSpan)
    {
        Filter = filter;
        Wrap = wrap;
        SidecarPath = sidecarPath;
        FilterSpan = filterSpan;
        WrapSpan = wrapSpan;
    }

    /// <summary>Gets the resolved sampler filter.</summary>
    internal BakeTextureFilter Filter { get; }

    /// <summary>Gets the resolved sampler wrap mode.</summary>
    internal BakeTextureWrap Wrap { get; }

    /// <summary>Gets the absolute sidecar path, or <see langword="null"/> when the descriptor defaults applied.</summary>
    internal string? SidecarPath { get; }

    /// <summary>Gets the sidecar <c>filter</c> value span, or <see langword="null"/> when defaulted.</summary>
    internal TextureSourceSpan? FilterSpan { get; }

    /// <summary>Gets the sidecar <c>wrap</c> value span, or <see langword="null"/> when defaulted.</summary>
    internal TextureSourceSpan? WrapSpan { get; }

    /// <summary>
    /// Creates settings that use the descriptor defaults only.
    /// </summary>
    /// <param name="filter">The default sampler filter.</param>
    /// <param name="wrap">The default sampler wrap mode.</param>
    /// <returns>Settings with no sidecar.</returns>
    internal static TextureImportSettings Defaults(BakeTextureFilter filter, BakeTextureWrap wrap)
        => new(filter, wrap, sidecarPath: null, filterSpan: null, wrapSpan: null);
}
