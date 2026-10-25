using Bork2d.Api;
using Bork2d.Common;

namespace Bork2d.Assets;

// Temporary build anchor: keeps the declared project references in the compiled assembly
// metadata until the M3 asset implementation consumes them. The dependency matrix test asserts
// the emitted references. Remove once real Assets code uses Api/Common.
internal static class AssemblyReferenceAnchor
{
    internal static Type ApiContract => typeof(IAssets);

    internal static Type CommonImplementation => typeof(Transform);
}
