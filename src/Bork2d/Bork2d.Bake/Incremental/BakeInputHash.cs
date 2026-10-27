namespace Bork2d.Bake.Incremental;

/// <summary>
/// One tracked bake input: its project-relative diagnostic display path and content hash.
/// </summary>
/// <param name="Path">
/// The project-relative display path with <c>/</c> separators, or the absolute path for an input
/// outside the project root (the bake roots cannot escape the project root, so this is defensive).
/// </param>
/// <param name="Hash">The 32-character uppercase XXH3-128 hex digest of the file contents.</param>
internal readonly record struct BakeInputHash(string Path, string Hash);
