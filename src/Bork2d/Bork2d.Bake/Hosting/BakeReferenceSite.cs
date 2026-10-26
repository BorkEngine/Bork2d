namespace Bork2d.Bake.Hosting;

/// <summary>
/// One reference value found while binding a TOML source into a typed asset.
/// </summary>
/// <param name="MemberPath">The member path, for example <c>texture</c> or <c>items[2].icon</c>.</param>
/// <param name="ReferencePath">The canonical reference path as written in the TOML source.</param>
/// <param name="IsData">
/// <see langword="true"/> for a <c>DataRef</c> (raw target); <see langword="false"/> for an
/// <c>AssetRef&lt;T&gt;</c> (MessagePack target).
/// </param>
/// <param name="Line">The 1-based source line of the reference value, or <see langword="null"/>.</param>
/// <param name="Column">The 1-based source column of the reference value, or <see langword="null"/>.</param>
/// <remarks>
/// The location is captured while binding, so a reference diagnostic from the validation pass points
/// at the value that produced the reference without a second document pass. The source file is the
/// bound asset's source path.
/// </remarks>
internal readonly record struct BakeReferenceSite(
    string MemberPath,
    string ReferencePath,
    bool IsData,
    int? Line = null,
    int? Column = null);
