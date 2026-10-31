namespace Bork2d.Bake.Diagnostics;

/// <summary>
/// The stable diagnostic-code ranges for the bake tooling.
/// </summary>
/// <remarks>
/// <para>
/// Codes are stable once a range is released. The final published table belongs to the documentation
/// work package; this class records the ranges and the codes the bake uses.
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Range</term>
/// <description>Area</description>
/// </listheader>
/// <item>
/// <term><c>BK1xxx</c></term>
/// <description>
/// Builder configuration and explicit data-type registration: project root, packs, registered asset
/// types, source discovery metadata, and source paths.
/// </description>
/// </item>
/// <item>
/// <term><c>BK2xxx</c></term>
/// <description>TOML parse/bind: syntax, type mismatch, required/unknown/missing member, range.</description>
/// </item>
/// <item>
/// <term><c>BK3xxx</c></term>
/// <description>Asset references and baked-set validation.</description>
/// </item>
/// <item>
/// <term><c>BK4xxx</c></term>
/// <description>Built-in texture decode/probe, dimensions, and payload; sidecars use the shared <c>BK2xxx</c> binder codes.</description>
/// </item>
/// <item>
/// <term><c>BK5xxx</c></term>
/// <description>Pack layout/write, pack copy, and incremental state.</description>
/// </item>
/// <item>
/// <term><c>BK6xxx</c></term>
/// <description>MSBuild/build integration.</description>
/// </item>
/// <item>
/// <term><c>BK7xxx</c></term>
/// <description>
/// Processor registration and processing: invalid registrations, extension overrides, sidecar
/// creation, source inputs, outputs, and processor exceptions.
/// </description>
/// </item>
/// </list>
/// </remarks>
internal static class BakeDiagnosticCodes
{
    /// <summary>The project root path is not a valid path.</summary>
    internal const string ProjectRootInvalid = "BK1001";

    /// <summary>The project root is missing or is not a directory.</summary>
    internal const string ProjectRootMissing = "BK1002";

    /// <summary>No pack is configured.</summary>
    internal const string PackRequired = "BK1003";

    /// <summary>A pack path is empty, rooted, absolute, or contains an empty/<c>.</c>/<c>..</c> segment.</summary>
    internal const string PackPathInvalid = "BK1004";

    /// <summary>A pack root is rooted, absolute, escapes the project root, or has an empty/<c>.</c>/<c>..</c> segment.</summary>
    internal const string PackRootInvalid = "BK1005";

    /// <summary>A pack root does not exist as a directory.</summary>
    internal const string PackRootMissing = "BK1006";

    /// <summary>The output root is not a valid path or a pack path escapes it.</summary>
    internal const string OutputRootInvalid = "BK1007";

    /// <summary>Two packs declare the same pack path.</summary>
    internal const string PackPathDuplicate = "BK1008";

    /// <summary>Two packs declare the same asset root.</summary>
    internal const string PackRootDuplicate = "BK1009";

    /// <summary>A data type is registered more than once.</summary>
    internal const string AssetTypeDuplicate = "BK1010";

    /// <summary>A data TOML selects a <c>[bork2d].type</c> that was not registered.</summary>
    internal const string AssetTypeNotRegistered = "BK1011";

    /// <summary>A TOML source path cannot be mapped to a canonical asset path (for example a dot).</summary>
    internal const string SourceInvalidAssetPath = "BK1025";

    /// <summary>Two source files map to the same canonical asset path.</summary>
    internal const string SourceDuplicate = "BK1026";

    /// <summary>A TOML source could not be read.</summary>
    internal const string SourceReadFailed = "BK1027";

    /// <summary>A TOML source is not valid UTF-8.</summary>
    internal const string SourceNotUtf8 = "BK1028";

    /// <summary>A non-sidecar data TOML has no <c>[bork2d].type</c> declaration.</summary>
    internal const string SourceTypeMissing = "BK1030";

    /// <summary>A source <c>[bork2d]</c> table is malformed or declares an unsupported key.</summary>
    internal const string SourceTypeInvalid = "BK1031";

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

    /// <summary>An unexpected exception escaped a registered type's binder.</summary>
    internal const string BinderUnexpected = "BK2012";

    /// <summary>A sidecar declares an <c>AssetRef</c>/<c>DataRef</c> member, which sidecars do not support.</summary>
    internal const string SidecarReferenceUnsupported = "BK2013";

    /// <summary>An orphan sidecar has no matching source file.</summary>
    internal const string SidecarOrphaned = "BK2014";

    /// <summary>More than one sidecar resolves to the same processor source.</summary>
    internal const string SidecarDuplicate = "BK2015";

    /// <summary>
    /// The pack could not be composed because <c>PackBuilder</c> rejected an entry or reference. This
    /// is an internal baker error: reference validation runs first, so a correct bake never reaches it.
    /// </summary>
    internal const string PackComposeFailed = "BK5001";

    /// <summary>The pack could not be written or atomically replaced.</summary>
    internal const string PackWriteFailed = "BK5002";

    /// <summary>
    /// The incremental manifest could not be written after a successful bake. The bake still
    /// succeeds; the next invocation bakes in full.
    /// </summary>
    internal const string IncrementalManifestWriteFailed = "BK5003";

    /// <summary>A configured pack could not be copied to the requested destination.</summary>
    internal const string PackCopyFailed = "BK5004";

    /// <summary>The incremental manifest matched and the bake was skipped as up to date.</summary>
    internal const string IncrementalSkipped = "BK5005";

    /// <summary>A processor source or sidecar could not be read or decoded as UTF-8.</summary>
    internal const string ProcessorSourceRead = "BK7001";

    /// <summary>A processor source relative path cannot be mapped to a canonical base asset path.</summary>
    internal const string ProcessorInputInvalid = "BK7002";

    /// <summary>A processor threw while processing one source; its outputs are discarded.</summary>
    internal const string ProcessorFailed = "BK7003";

    /// <summary>A processor output path is not canonical or is not base-path-derived.</summary>
    internal const string ProcessorOutputPathInvalid = "BK7004";

    /// <summary>A processor produced the same output path twice for one source.</summary>
    internal const string ProcessorOutputDuplicate = "BK7005";

    /// <summary>A required sidecar could not be created or written.</summary>
    internal const string ProcessorSidecarCreateFailed = "BK7006";

    /// <summary>A processor registration is invalid.</summary>
    internal const string ProcessorRegistrationInvalid = "BK7007";

    /// <summary>A later processor registration overrides an earlier one for one extension.</summary>
    internal const string ProcessorExtensionOverride = "BK7008";

    /// <summary>A required sidecar was created next to its source.</summary>
    internal const string ProcessorSidecarCreated = "BK7009";
}
