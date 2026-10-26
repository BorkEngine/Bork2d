using Bork2d.Common;

namespace Bork2d.Assets;

/// <summary>
/// Keeps the declared <c>Bork2d.Assets</c> to <c>Bork2d.Common</c> project reference in the
/// compiled assembly metadata.
/// </summary>
/// <remarks>
/// The v1 pack reader/writer uses the BCL and <c>Bork2d.Api</c> only; later M3 work packages
/// consume <c>Bork2d.Common</c> types. The dependency matrix declares the reference and the
/// dependency test asserts it, so this anchor stays until real code consumes a Common type.
/// </remarks>
internal static class CommonReferenceAnchor
{
    /// <summary>Gets a Common type that anchors the emitted assembly reference.</summary>
    internal static Type CommonType => typeof(Transform);
}
