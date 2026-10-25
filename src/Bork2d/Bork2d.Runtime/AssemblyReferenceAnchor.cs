namespace Bork2d.Runtime;

// Temporary build anchor: keeps the declared project references in the compiled assembly
// metadata until the M4 builder and later composition code consume them. The dependency matrix
// test asserts the emitted references.
internal static class AssemblyReferenceAnchor
{
    internal static Type ApiContract => typeof(Bork2d.Api.IEngine<>);

    internal static Type CommonImplementation => typeof(Bork2d.Common.Transform);

    internal static Type AssetsImplementation => typeof(Bork2d.Assets.AssemblyReferenceAnchor);

    internal static Type RenderImplementation => typeof(Bork2d.Render.AssemblyReferenceAnchor);
}
