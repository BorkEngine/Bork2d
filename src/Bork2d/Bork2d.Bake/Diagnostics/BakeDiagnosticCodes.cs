namespace Bork2d.Bake.Diagnostics;

/// <summary>
/// The stable diagnostic-code ranges for the bake tooling.
/// </summary>
/// <remarks>
/// <para>
/// Codes are stable once a range is released. The final published table belongs to the M4
/// documentation work package; this class records the ranges and the codes WP1 uses.
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Range</term>
/// <description>Area</description>
/// </listheader>
/// <item>
/// <term><c>BK1xxx</c></term>
/// <description>Descriptor, projects, type registration, generated host, host loading.</description>
/// </item>
/// <item>
/// <term><c>BK2xxx</c></term>
/// <description>TOML parse/bind: syntax, type mismatch, required/unknown/missing member, range.</description>
/// </item>
/// <item>
/// <term><c>BK3xxx</c></term>
/// <description>Custom-data references and asset-graph validation.</description>
/// </item>
/// <item>
/// <term><c>BK4xxx</c></term>
/// <description>Texture discovery, sidecar, decoding, dimensions, payload.</description>
/// </item>
/// <item>
/// <term><c>BK5xxx</c></term>
/// <description>Pack layout/write, incremental manifest, up-to-date/force.</description>
/// </item>
/// <item>
/// <term><c>BK6xxx</c></term>
/// <description>MSBuild/target/host-build integration.</description>
/// </item>
/// </list>
/// </remarks>
internal static class BakeDiagnosticCodes
{
    /// <summary>The project descriptor could not be read.</summary>
    internal const string DescriptorReadFailed = "BK1001";

    /// <summary>The required <c>schema</c> key is missing.</summary>
    internal const string DescriptorSchemaMissing = "BK1002";

    /// <summary>The descriptor schema version is not supported.</summary>
    internal const string DescriptorSchemaUnsupported = "BK1003";

    /// <summary>A descriptor key is not part of the schema.</summary>
    internal const string DescriptorUnknownKey = "BK1004";

    /// <summary>A required descriptor key is missing.</summary>
    internal const string DescriptorMissingKey = "BK1005";

    /// <summary>A descriptor value has the wrong type or contents.</summary>
    internal const string DescriptorInvalidValue = "BK1006";

    /// <summary>The assets root is missing or is not a directory.</summary>
    internal const string DescriptorAssetsRoot = "BK1007";

    /// <summary>Two <c>[[types]]</c> entries use the same exact prefix.</summary>
    internal const string DescriptorDuplicatePrefix = "BK1008";

    /// <summary>A <c>[[types]]</c> prefix is not a canonical asset path.</summary>
    internal const string DescriptorInvalidPrefix = "BK1009";

    /// <summary>A <c>[textures]</c> filter or wrap value is not supported.</summary>
    internal const string DescriptorTextureSetting = "BK1010";

    /// <summary>TOML syntax (including duplicate keys) in a source file.</summary>
    /// <remarks>BK2xxx is the TOML parse/bind range; WP2 assigns the binder codes.</remarks>
    internal const string TomlSyntax = "BK2001";
}
