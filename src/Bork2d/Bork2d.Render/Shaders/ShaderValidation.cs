using System.Buffers.Binary;
using Bork2d.Engine.Assets;

namespace Bork2d.Render.Shaders;

/// <summary>
/// Renderer-side validation of a captured <see cref="ShaderRenderData"/> declaration set and its
/// SPIR-V payload.
/// </summary>
/// <remarks>
/// <para>
/// The engine's asset loader and freeze path are deliberately tolerant of malformed declarations; the
/// renderer owns rejecting or falling back from them. This validator mirrors the bake-side layout
/// rules but reports a single diagnostic string instead of a bake error: the current layout version,
/// the <c>main</c> entry point, at most four texture slots, identifier and unique names, aligned and
/// non-overlapping instance-uniform lanes inside the 16-float block, aligned and unique global slots
/// inside the eight-<c>vec4</c> block, and no name declared in both blocks.
/// </para>
/// <para>
/// Validation never throws and never repairs: any reported problem makes the caller fall back to the
/// default shader with a one-time warning.
/// </para>
/// </remarks>
internal static class ShaderValidation
{
    /// <summary>The maximum number of additional texture slots one custom shader may declare.</summary>
    internal const int MaxTextureSlots = 4;

    /// <summary>The fragment entry point every custom shader must use.</summary>
    internal const string EntryPointName = "main";

    /// <summary>
    /// Validates one frozen declaration set against the current shader layout.
    /// </summary>
    /// <param name="metadata">The frozen declarations.</param>
    /// <param name="error">The diagnostic reason when validation fails.</param>
    /// <returns><see langword="true"/> when the declarations are renderable.</returns>
    internal static bool TryValidateMetadata(ShaderRenderData metadata, out string error)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.LayoutVersion != ShaderLayout.CurrentVersion)
        {
            error = $"it declares layout version {metadata.LayoutVersion}, but this engine implements "
                + $"version {ShaderLayout.CurrentVersion}";
            return false;
        }

        if (!string.Equals(metadata.EntryPoint, EntryPointName, StringComparison.Ordinal))
        {
            error = $"it declares entry point '{metadata.EntryPoint}', but the renderer requires "
                + $"'{EntryPointName}'";
            return false;
        }

        if (metadata.TextureSlots.Length > MaxTextureSlots)
        {
            error = $"it declares {metadata.TextureSlots.Length} texture slots; at most "
                + $"{MaxTextureSlots} are supported";
            return false;
        }

        var textureSlotNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderTextureSlot slot in metadata.TextureSlots)
        {
            if (slot.Name is null || !IsGlslIdentifier(slot.Name))
            {
                error = $"its texture slot name '{slot.Name ?? "(null)"}' is not a valid GLSL "
                    + "identifier";
                return false;
            }

            if (!textureSlotNames.Add(slot.Name))
            {
                error = $"its texture slot name '{slot.Name}' is declared more than once";
                return false;
            }
        }

        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        uint instanceLanes = 0;
        foreach (ShaderUniform uniform in metadata.InstanceUniforms)
        {
            if (!TryValidateUniformName(uniform, "instance uniform", declaredNames, out error))
            {
                return false;
            }

            int componentCount = ShaderLayout.ComponentCount(uniform.Type);
            if (componentCount == 0)
            {
                error = $"its instance uniform '{uniform.Name}' declares an unknown type "
                    + $"{(int)uniform.Type}";
                return false;
            }

            int alignment = componentCount switch
            {
                1 => 1,
                2 => 2,
                _ => 4,
            };
            if (uniform.Offset < 0
                || uniform.Offset % alignment != 0
                || uniform.Offset > ShaderLayout.InstanceBlockFloatCount - componentCount)
            {
                error = $"its instance uniform '{uniform.Name}' at float lane {uniform.Offset} is "
                    + $"not an aligned {componentCount}-component slot inside the "
                    + $"{ShaderLayout.InstanceBlockFloatCount}-float instance block";
                return false;
            }

            uint lanes = ((1u << componentCount) - 1u) << uniform.Offset;
            if ((instanceLanes & lanes) != 0)
            {
                error = $"its instance uniform '{uniform.Name}' at float lane {uniform.Offset} "
                    + "overlaps another declaration";
                return false;
            }

            instanceLanes |= lanes;
        }

        uint globalSlots = 0;
        foreach (ShaderUniform uniform in metadata.GlobalUniforms)
        {
            if (!TryValidateUniformName(uniform, "global uniform", declaredNames, out error))
            {
                return false;
            }

            int componentCount = ShaderLayout.ComponentCount(uniform.Type);
            if (componentCount == 0)
            {
                error = $"its global uniform '{uniform.Name}' declares an unknown type "
                    + $"{(int)uniform.Type}";
                return false;
            }

            if (uniform.Offset < 0
                || uniform.Offset % 4 != 0
                || uniform.Offset > (ShaderLayout.GlobalBlockSlotCount * 4) - componentCount)
            {
                error = $"its global uniform '{uniform.Name}' at float lane {uniform.Offset} is not "
                    + $"an aligned slot inside the {ShaderLayout.GlobalBlockSlotCount}-slot global "
                    + "block";
                return false;
            }

            int slot = uniform.Offset / 4;
            uint slotBit = 1u << slot;
            if ((globalSlots & slotBit) != 0)
            {
                error = $"its global uniform '{uniform.Name}' at slot {slot} overlaps another "
                    + "declaration";
                return false;
            }

            globalSlots |= slotBit;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Validates a raw SPIR-V payload: non-empty and starting with the little-endian SPIR-V magic.
    /// </summary>
    /// <param name="spirV">The loaded module bytes.</param>
    /// <param name="error">The diagnostic reason when validation fails.</param>
    /// <returns><see langword="true"/> when the payload is a SPIR-V module.</returns>
    internal static bool TryValidateSpirV(byte[] spirV, out string error)
    {
        ArgumentNullException.ThrowIfNull(spirV);

        if (spirV.Length < sizeof(uint))
        {
            error = $"its SPIR-V payload is {spirV.Length} bytes, shorter than the "
                + $"{sizeof(uint)}-byte magic word";
            return false;
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(spirV);
        if (magic != ShaderStore.SpirVMagic)
        {
            error = $"its first little-endian word is 0x{magic:X8}, not "
                + $"0x{ShaderStore.SpirVMagic:X8}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Returns whether a name is a valid GLSL identifier.</summary>
    /// <param name="name">The declared name.</param>
    /// <returns><see langword="true"/> for a non-empty ASCII identifier.</returns>
    internal static bool IsGlslIdentifier(string name)
    {
        if (name.Length == 0 || !IsIdentifierStart(name[0]))
        {
            return false;
        }

        foreach (char character in name)
        {
            if (!IsIdentifierPart(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns whether a name is reserved by the engine or the GLSL compiler.</summary>
    /// <param name="name">The declared name.</param>
    /// <returns><see langword="true"/> for a reserved prefix.</returns>
    internal static bool IsReservedShaderName(string name)
        => name.StartsWith("gl_", StringComparison.Ordinal)
            || name.StartsWith("BORK2D_", StringComparison.Ordinal)
            || name.StartsWith("bork2d", StringComparison.Ordinal);

    private static bool TryValidateUniformName(
        in ShaderUniform uniform,
        string kind,
        HashSet<string> declaredNames,
        out string error)
    {
        if (uniform.Name is null || !IsGlslIdentifier(uniform.Name))
        {
            error = $"its {kind} name '{uniform.Name ?? "(null)"}' is not a valid GLSL identifier";
            return false;
        }

        if (IsReservedShaderName(uniform.Name))
        {
            error = $"its {kind} name '{uniform.Name}' is reserved; a declared name must not start "
                + "with 'gl_', 'BORK2D_', or 'bork2d'";
            return false;
        }

        if (!declaredNames.Add(uniform.Name))
        {
            error = $"its name '{uniform.Name}' is declared more than once across instance and "
                + "global uniforms";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsIdentifierStart(char character)
        => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or '_';

    private static bool IsIdentifierPart(char character)
        => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_';
}
