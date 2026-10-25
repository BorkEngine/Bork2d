using Bork2d.Api;
using Bork2d.Common;

namespace Bork2d.Render;

// Temporary build anchor: keeps the declared project references in the compiled assembly
// metadata until real renderer code consumes them. The dependency matrix test asserts the
// emitted references.
internal static class AssemblyReferenceAnchor
{
    internal static Type ApiContract => typeof(IAssets);

    internal static Type CommonImplementation => typeof(Transform);

    internal static Type AssetsImplementation => typeof(Bork2d.Assets.AssemblyReferenceAnchor);
}
