namespace Bork2d.Bake;

// Temporary build anchor: keeps the declared Api/Assets project references in the compiled
// assembly metadata until real tooling code consumes an Api type. The dependency matrix test
// asserts the emitted references.
internal static class AssemblyReferenceAnchor
{
    internal static Type ApiContract => typeof(Bork2d.Api.Assets.IAssets);

    internal static Type AssetsTooling => typeof(Bork2d.Assets.Pack.PackBuilder);
}
