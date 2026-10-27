namespace Bork2d.Bake.Incremental;

/// <summary>
/// One written pack output: its absolute path and content hash.
/// </summary>
/// <param name="Path">The absolute pack path under the output root.</param>
/// <param name="Hash">The 32-character uppercase XXH3-128 hex digest of the pack file.</param>
internal readonly record struct BakeOutputHash(string Path, string Hash);
