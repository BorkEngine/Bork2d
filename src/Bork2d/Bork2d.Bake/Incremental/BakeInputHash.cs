namespace Bork2d.Bake.Incremental;

/// <summary>
/// One hashed bake input: a normalized source path and its XXH3-128 hex digest.
/// </summary>
/// <param name="Path">The normalized input path, relative to the project descriptor directory.</param>
/// <param name="Hash">The 32-character uppercase hex digest.</param>
internal readonly record struct BakeInputHash(string Path, string Hash);
