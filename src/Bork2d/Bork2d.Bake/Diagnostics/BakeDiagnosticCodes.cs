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

    /// <summary>The <c>Project.Common</c> assembly or another required assembly was not found.</summary>
    internal const string HostReferenceMissing = "BK1011";

    /// <summary>A required assembly could not be read as managed metadata.</summary>
    internal const string HostMetadataUnreadable = "BK1012";

    /// <summary>The generated host file could not be written.</summary>
    internal const string HostWriteFailed = "BK1013";

    /// <summary>The generated host assembly is missing.</summary>
    internal const string HostAssemblyMissing = "BK1014";

    /// <summary>The generated host assembly, manifest, or ALC load failed.</summary>
    internal const string HostLoadFailed = "BK1015";

    /// <summary>The generated host does not expose an <c>IBakeManifest</c> implementation.</summary>
    internal const string HostContractInvalid = "BK1016";

    /// <summary>A tooling or asset type identity is not shared with the default load context.</summary>
    internal const string HostTypeIdentityMismatch = "BK1017";

    /// <summary>The generated host is stale for the current project descriptor.</summary>
    internal const string HostStale = "BK1018";

    /// <summary>A mapped root type has an unsupported shape kind at host load.</summary>
    internal const string HostShapeUnsupported = "BK1019";

    /// <summary>A mapped type full name was not found in any searched assembly.</summary>
    internal const string TypeNotFound = "BK1020";

    /// <summary>A mapped type full name exists in more than one candidate assembly.</summary>
    internal const string TypeAmbiguous = "BK1021";

    /// <summary>A mapped type name uses an unsupported form (assembly-qualified, nested, generic, ...).</summary>
    internal const string TypeUnsupportedForm = "BK1022";

    /// <summary>A mapped type does not implement <c>IShapeable&lt;T&gt;</c> for itself.</summary>
    internal const string TypeNotShapeable = "BK1023";

    /// <summary>A mapped type is not visible to the generated host.</summary>
    internal const string TypeNotVisible = "BK1024";

    /// <summary>A TOML source is not covered by any <c>[[types]]</c> prefix.</summary>
    internal const string SourceUnmapped = "BK1025";

    /// <summary>Two source files map to the same canonical asset path.</summary>
    internal const string SourceDuplicate = "BK1026";

    /// <summary>A TOML source could not be read.</summary>
    internal const string SourceReadFailed = "BK1027";

    /// <summary>A TOML source is not valid UTF-8.</summary>
    internal const string SourceNotUtf8 = "BK1028";

    /// <summary>A reference path is not canonical.</summary>
    internal const string ReferenceNotCanonical = "BK3001";

    /// <summary>A reference targets an asset path this bake does not produce.</summary>
    internal const string ReferenceMissing = "BK3002";

    /// <summary>A reference's kind (MessagePack asset vs raw payload) does not match the target entry.</summary>
    internal const string ReferenceWrongKind = "BK3003";

    /// <summary>A typed reference's target type does not match the baked target's type.</summary>
    internal const string ReferenceWrongType = "BK3004";

    /// <summary>Two baked entries produce the same canonical path, or the same path in two kinds.</summary>
    internal const string AssetPathCollision = "BK3005";

    /// <summary>TOML syntax (including duplicate keys) in a source file.</summary>
    internal const string TomlSyntax = "BK2001";

    /// <summary>A source table contains a key no mapped member handles.</summary>
    internal const string BinderUnknownKey = "BK2002";

    /// <summary>A required member has no source key.</summary>
    internal const string BinderMissingRequired = "BK2003";

    /// <summary>A source value has the wrong TOML type for the member.</summary>
    internal const string BinderTypeMismatch = "BK2004";

    /// <summary>An integer source value is outside the member type's range.</summary>
    internal const string BinderNumericRange = "BK2005";

    /// <summary>An enum source value is not an exact camelCase member name.</summary>
    internal const string BinderEnumValue = "BK2006";

    /// <summary>A source value cannot be parsed for the member (Guid, date-time, vector, ...).</summary>
    internal const string BinderInvalidValue = "BK2007";

    /// <summary>A member's type is not a supported TOML leaf or shape kind.</summary>
    internal const string BinderUnsupportedLeaf = "BK2008";

    /// <summary>More than one source key matches a member case-insensitively.</summary>
    internal const string BinderAmbiguousKey = "BK2009";

    /// <summary>A date-time value has no UTC offset, which the TOML mapping rejects.</summary>
    internal const string BinderLocalDateTime = "BK2010";

    /// <summary>A collection or dictionary shape cannot be constructed from TOML.</summary>
    internal const string BinderUnsupportedCollection = "BK2011";

    /// <summary>
    /// The pack could not be composed because <c>PackBuilder</c> rejected an entry or reference. This
    /// is an internal baker error: reference validation runs first, so a correct bake never reaches it.
    /// </summary>
    internal const string PackComposeFailed = "BK5001";

    /// <summary>The pack could not be written or atomically replaced.</summary>
    internal const string PackWriteFailed = "BK5002";
}
