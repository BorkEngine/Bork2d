namespace Bork2d.Bake.Builder;

// Temporary build anchor: keeps the declared Api/Assets project references in the compiled
// assembly metadata even though the public builder surface hides them behind the internal bake
// implementation. The dependency matrix test asserts the emitted references.
internal static class AssemblyReferenceAnchor
{
    internal static Type ApiContract => typeof(Bork2d.Api.Assets.IAssets);

    internal static Type AssetsTooling => typeof(Bork2d.Assets.Pack.PackBuilder);
}
