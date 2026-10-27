namespace Bork2d.Bake.Project;

/// <summary>
/// One <c>[[assets]]</c> entry of the project descriptor: an asset root and its pack output
/// (D-M4-14).
/// </summary>
/// <remarks>
/// <para>
/// The first entry is the main/base pack; every later entry is a DLC/overlay pack. There is no
/// per-entry <c>main</c> flag: entries are ordered, and <see cref="Index"/> zero is the main pack.
/// </para>
/// <para>
/// <see cref="Root"/> is the absolute, validated asset-root directory under the descriptor
/// directory. <see cref="PackPath"/> is the descriptor-authored pack path, relative to the
/// descriptor's output root (the descriptor directory by default, or the CLI/MSBuild output root),
/// with <c>/</c> separators. It may contain subdirectories but never a rooted path, <c>.</c>, or
/// <c>..</c> segment.
/// </para>
/// </remarks>
internal sealed class ProjectPack
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectPack"/> class.
    /// </summary>
    /// <param name="index">The zero-based descriptor order; zero is the main pack.</param>
    /// <param name="root">The absolute asset-root directory.</param>
    /// <param name="relativeRoot">The asset root relative to the descriptor directory, with <c>/</c> separators.</param>
    /// <param name="packPath">The pack path relative to the output root, with <c>/</c> separators.</param>
    internal ProjectPack(int index, string root, string relativeRoot, string packPath)
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

    /// <summary>Gets the zero-based descriptor order; zero is the main pack.</summary>
    internal int Index { get; }

    /// <summary>Gets a value indicating whether this is the main/base pack (the first entry).</summary>
    internal bool IsMain => Index == 0;

    /// <summary>Gets the absolute asset-root directory.</summary>
    internal string Root { get; }

    /// <summary>Gets the asset root relative to the descriptor directory, with <c>/</c> separators.</summary>
    internal string RelativeRoot { get; }

    /// <summary>Gets the pack path relative to the output root, with <c>/</c> separators.</summary>
    internal string PackPath { get; }
}
