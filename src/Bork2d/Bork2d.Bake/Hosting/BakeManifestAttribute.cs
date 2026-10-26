namespace Bork2d.Bake.Hosting;

/// <summary>
/// Marks the generated manifest type in a bake-host assembly.
/// </summary>
/// <remarks>
/// The attribute is internal and the generated host receives access through
/// <c>InternalsVisibleTo</c> to the fixed <see cref="BakeHostIdentity.AssemblyName"/>. The CLI
/// discovers the manifest from this attribute; there is no assembly scanning or reflection over
/// game members beyond instantiating this one known type.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
internal sealed class BakeManifestAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeManifestAttribute"/> class.
    /// </summary>
    /// <param name="manifestType">The type implementing <see cref="IBakeManifest"/>.</param>
    internal BakeManifestAttribute(Type manifestType)
    {
        ArgumentNullException.ThrowIfNull(manifestType);

        ManifestType = manifestType;
    }

    /// <summary>Gets the manifest type that implements <see cref="IBakeManifest"/>.</summary>
    internal Type ManifestType { get; }
}
