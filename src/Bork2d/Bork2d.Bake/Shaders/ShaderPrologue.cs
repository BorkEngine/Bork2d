using System.Text;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Shaders;

/// <summary>
/// Builds the fixed GLSL prologue the shader processor prepends to every fragment source, together
/// with one direct-use macro per declared uniform.
/// </summary>
/// <remarks>
/// <para>
/// The header is versioned by <see cref="ShaderLayout.CurrentVersion"/> and must stay in lockstep
/// with the engine's fragment interface: the varying names, locations, and meanings are the contract
/// the renderer's vertex stage writes. The bindless sampler array is set <c>0</c> binding <c>1</c>;
/// the global block is set <c>1</c> binding <c>2</c> because Vulkan forbids a dynamic uniform buffer
/// in the same set as an update-after-bind binding. Changing a location, a set, or a meaning requires
/// a new layout version, not an edit here alone.
/// </para>
/// <para>
/// Every macro expansion is parenthesized so a uniform can be used anywhere an expression is
/// allowed. An instance uniform expands to the packed lane of <c>vInstanceParams</c>, a global one to
/// the packed slot of <c>bork2dGlobals.globals</c>; the packer keeps every declaration inside one
/// <c>vec4</c> lane, so a swizzle never crosses a lane boundary and never truncates a component.
/// </para>
/// </remarks>
internal static class ShaderPrologue
{
    private const string Header = """
        #version 460
        #extension GL_EXT_nonuniform_qualifier : require

        layout(location = 0) in vec2 vUv;
        layout(location = 1) in vec4 vTint;
        layout(location = 2) flat in uint vTextureIndex;
        layout(location = 3) in vec2 vWorldPosition;
        layout(location = 4) in vec2 vLocalPosition;
        layout(location = 5) flat in uint vExtraTextureIndices[4];
        layout(location = 9) in vec4 vInstanceParams[4];

        layout(set = 0, binding = 1) uniform sampler2D bork2dTextures[];
        layout(set = 1, binding = 2) uniform Bork2dGlobalBlock
        {
            vec4 time;
            vec4 globals[8];
        } bork2dGlobals;

        layout(location = 0) out vec4 outColor;

        #define BORK2D_MAIN_TEXTURE (bork2dTextures[nonuniformEXT(vTextureIndex)])
        #define BORK2D_TEXTURE0 (bork2dTextures[nonuniformEXT(vExtraTextureIndices[0])])
        #define BORK2D_TEXTURE1 (bork2dTextures[nonuniformEXT(vExtraTextureIndices[1])])
        #define BORK2D_TEXTURE2 (bork2dTextures[nonuniformEXT(vExtraTextureIndices[2])])
        #define BORK2D_TEXTURE3 (bork2dTextures[nonuniformEXT(vExtraTextureIndices[3])])
        #define BORK2D_TIME (bork2dGlobals.time.x)

        #define main bork2d_user_main
        """;

    /// <summary>
    /// The fixed wrapper appended after the user source: it restores the engine <c>main</c>, calls
    /// the user body, and consumes every fixed-interface input in a branch that cannot be reached
    /// for valid engine data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A custom shader that does not reference every prologue input can have those inputs optimized
    /// out of its SPIR-V, which makes the engine-owned vertex stage's outputs appear unconsumed and
    /// produces a Vulkan <c>WARNING-Shader-OutputNotConsumed</c>. The wrapper guarantees the module
    /// keeps a use of every declared input without changing any valid frame's pixels.
    /// </para>
    /// <para>
    /// The guard is the same one the built-in default shader used before the wrapper existed: every
    /// bindless index is below the engine's 1,048,576-entry cap (<c>0x00100000</c>), so an OR of the
    /// four extra indices above <c>0x003FFFFF</c> is impossible and the branch body never runs.
    /// </para>
    /// </remarks>
    internal const string Epilogue = """
        #undef main

        void main()
        {
            bork2d_user_main();

            uint extra = vExtraTextureIndices[0] | vExtraTextureIndices[1]
                | vExtraTextureIndices[2] | vExtraTextureIndices[3];
            if (extra > 0x003FFFFFu)
            {
                vec4 keepAlive = texture(
                    bork2dTextures[nonuniformEXT(vExtraTextureIndices[0])], vUv) * vTint;
                keepAlive += vec4(vWorldPosition.x, vLocalPosition.x, float(vTextureIndex), 0.0);
                keepAlive += vInstanceParams[0] + vInstanceParams[1]
                    + vInstanceParams[2] + vInstanceParams[3];
                outColor += keepAlive;
            }
        }
        """;

    /// <summary>Builds the prologue for the given packed uniform declarations.</summary>
    /// <param name="instanceUniforms">The packed instance declarations, sorted by name.</param>
    /// <param name="globalUniforms">The packed global declarations, sorted by name.</param>
    /// <returns>The prologue text, without a trailing newline.</returns>
    internal static string Build(
        IReadOnlyList<ShaderUniform> instanceUniforms,
        IReadOnlyList<ShaderUniform> globalUniforms)
    {
        ArgumentNullException.ThrowIfNull(instanceUniforms);
        ArgumentNullException.ThrowIfNull(globalUniforms);

        var builder = new StringBuilder(Header.Length + ((instanceUniforms.Count + globalUniforms.Count) * 64));
        builder.Append(Header);

        foreach (ShaderUniform uniform in instanceUniforms)
        {
            builder.Append('\n').Append(FormatMacro(uniform, "vInstanceParams"));
        }

        foreach (ShaderUniform uniform in globalUniforms)
        {
            builder.Append('\n').Append(FormatMacro(uniform, "bork2dGlobals.globals"));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Composes the complete fragment source the compiler sees: the fixed prologue, the user source,
    /// and the fixed wrapper.
    /// </summary>
    /// <param name="prologue">The prologue built for the declared uniforms.</param>
    /// <param name="source">The user fragment source, which defines <c>void main()</c>.</param>
    /// <returns>The combined source to compile.</returns>
    internal static string Compose(string prologue, string source)
    {
        ArgumentNullException.ThrowIfNull(prologue);
        ArgumentNullException.ThrowIfNull(source);
        return prologue + "\n" + source + "\n" + Epilogue;
    }

    /// <summary>Counts the prologue lines so a compiler line can be mapped back to the source.</summary>
    /// <param name="prologue">The prologue text.</param>
    /// <returns>The number of lines; a scalar prologue is one line.</returns>
    internal static int CountLines(string prologue)
    {
        ArgumentNullException.ThrowIfNull(prologue);
        return prologue.Length == 0 ? 0 : prologue.Count(character => character == '\n') + 1;
    }

    private static string FormatMacro(ShaderUniform uniform, string block)
    {
        int componentCount = ShaderLayout.ComponentCount(uniform.Type);
        int lane = uniform.Offset & 3;
        string swizzle = "xyzw".Substring(lane, componentCount);
        return $"#define {uniform.Name} ({block}[{uniform.Offset / 4}].{swizzle})";
    }
}
