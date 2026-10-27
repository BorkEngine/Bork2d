namespace Bork2d.Bake.Incremental;

/// <summary>
/// One recorded pack output of an incremental bake: its absolute path and content hash.
/// </summary>
/// <remarks>
/// A project may produce several packs (D-M4-14). The skip rule requires every recorded output to
/// exist with the recorded hash, so a missing or externally modified DLC pack re-bakes the whole
/// project.
/// </remarks>
/// <param name="Path">The absolute pack path.</param>
/// <param name="Hash">The XXH3-128 content hash (32 uppercase hex characters).</param>
internal readonly record struct BakeOutputHash(string Path, string Hash);
