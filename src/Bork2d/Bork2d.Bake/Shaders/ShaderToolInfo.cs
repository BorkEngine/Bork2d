using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Shaders;

/// <summary>
/// Identifies the shader build tool and the fragment ABI/packing versions it implements.
/// </summary>
/// <remarks>
/// <para>
/// The marker participates in the incremental configuration hash, so upgrading shaderc, bumping the
/// fragment layout version, or changing the prologue or the uniform packer invalidates every cached
/// shader output instead of reusing a pack built by a different tool.
/// </para>
/// <para>
/// The layout segment is derived from <see cref="ShaderLayout.CurrentVersion"/>, so a layout bump
/// changes the marker without a second edit here. The prologue and packer segments are bumped
/// manually when their output changes; a purely internal refactor that produces identical shaders
/// does not need a bump.
/// </para>
/// </remarks>
internal static class ShaderToolInfo
{
    /// <summary>The shaderc package version the bake compiles with.</summary>
    internal const string ShadercVersion = "shaderc-2.23.0";

    /// <summary>The fixed-prologue version, bumped when the generated prologue changes.</summary>
    internal const string PrologueVersion = "prologue-2";

    /// <summary>The uniform packing version, bumped when offset assignment changes.</summary>
    internal const string PackerVersion = "packer-1";

    /// <summary>
    /// Gets the marker recorded in the incremental configuration hash:
    /// <c>shaderc-2.23.0/layout-2/prologue-2/packer-1</c>.
    /// </summary>
    internal static string ConfigurationMarker { get; } =
        $"{ShadercVersion}/layout-{ShaderLayout.CurrentVersion}/{PrologueVersion}/{PackerVersion}";
}
