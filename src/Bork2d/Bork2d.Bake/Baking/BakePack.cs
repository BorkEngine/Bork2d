namespace Bork2d.Bake.Baking;

/// <summary>
/// One configured asset pack: an asset root and its pack output path.
/// </summary>
/// <remarks>
/// <para>
/// Packs are ordered. The first pack is the main/base pack; every later pack is a DLC/overlay pack.
/// There is no per-pack <c>main</c> flag: <see cref="Index"/> zero is the main pack.
/// </para>
/// <para>
/// <see cref="Root"/> is the absolute, validated asset-root directory under the project root.
/// <see cref="RelativeRoot"/> is that root relative to the project root with <c>/</c> separators.
/// <see cref="PackPath"/> is the configured pack path relative to the output root (the build output
/// directory or <c>--output-root</c>) with <c>/</c> separators. It may contain subdirectories but
/// never a rooted path, <c>.</c>, or <c>..</c> segment.
/// </para>
/// </remarks>
internal sealed class BakePack
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakePack"/> class.
    /// </summary>
    /// <param name="index">The zero-based configuration order; zero is the main pack.</param>
    /// <param name="root">The absolute asset-root directory.</param>
    /// <param name="relativeRoot">The asset root relative to the project root, with <c>/</c> separators.</param>
    /// <param name="packPath">The pack path relative to the output root, with <c>/</c> separators.</param>
    internal BakePack(int index, string root, string relativeRoot, string packPath)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);

        Index = index;
        Root = root;
        RelativeRoot = relativeRoot;
        PackPath = packPath;
    }

    /// <summary>Gets the zero-based configuration order; zero is the main pack.</summary>
    internal int Index { get; }

    /// <summary>Gets a value indicating whether this is the main/base pack (the first entry).</summary>
    internal bool IsMain => Index == 0;

    /// <summary>Gets the absolute asset-root directory.</summary>
    internal string Root { get; }

    /// <summary>Gets the asset root relative to the project root, with <c>/</c> separators.</summary>
    internal string RelativeRoot { get; }

    /// <summary>Gets the pack path relative to the output root, with <c>/</c> separators.</summary>
    internal string PackPath { get; }
}
