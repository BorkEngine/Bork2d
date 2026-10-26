namespace Bork2d.Bake.Textures;

/// <summary>
/// Discovery state for one texture source file.
/// </summary>
/// <remarks>
/// Mutable only while one import pass runs; the baked result exposes <see cref="BakedTexture"/>
/// instead. <see cref="AssetPath"/> and <see cref="PayloadPath"/> stay <see langword="null"/> when
/// the source path is invalid or reserved, so the pipeline can still associate a sidecar and report
/// a precise diagnostic before skipping the source.
/// </remarks>
internal sealed class TextureSourceEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextureSourceEntry"/> class.
    /// </summary>
    /// <param name="sourcePath">The absolute source image path.</param>
    /// <param name="relativePath">The asset-root-relative source path with <c>/</c> separators.</param>
    /// <param name="displayPath">The diagnostic display path for the source.</param>
    internal TextureSourceEntry(string sourcePath, string relativePath, string displayPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);

        SourcePath = sourcePath;
        RelativePath = relativePath;
        DisplayPath = displayPath;
    }

    /// <summary>Gets the absolute source image path.</summary>
    internal string SourcePath { get; }

    /// <summary>Gets the asset-root-relative source path with <c>/</c> separators.</summary>
    internal string RelativePath { get; }

    /// <summary>Gets the diagnostic display path for the source.</summary>
    internal string DisplayPath { get; }

    /// <summary>Gets or sets the canonical descriptor asset path, when the source path is valid.</summary>
    internal string? AssetPath { get; set; }

    /// <summary>Gets or sets the canonical raw payload path, when the source path is valid.</summary>
    internal string? PayloadPath { get; set; }

    /// <summary>Gets or sets the absolute <c>&lt;source&gt;.toml</c> sidecar path, when one exists.</summary>
    internal string? SidecarPath { get; set; }

    /// <summary>Gets or sets a value indicating whether another source maps to the same asset path.</summary>
    internal bool IsCollision { get; set; }
}
