using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Toml;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Shaders;

/// <summary>
/// The built-in fragment shader processor: compiles one GLSL fragment source with the fixed engine
/// prologue and emits a <see cref="ShaderAsset"/> plus its raw SPIR-V payload.
/// </summary>
/// <remarks>
/// <para>
/// The processor is an ordinary asset processor and imposes no directory prefix: a source under any
/// canonical asset path is baked, so <c>materials/lit.frag</c> emits <c>materials/lit</c> and the raw
/// module <c>materials/lit-spv</c>. The source is read as strict UTF-8; the bake supplies
/// <c>#version 460</c>, the required extension, the fixed interface, and the <c>main</c> entry point,
/// so a source that declares its own <c>#version</c> or <c>#extension</c> is rejected as
/// <see cref="BakeDiagnosticCodes.ShaderSourceInvalid"/>.
/// </para>
/// <para>
/// The required sidecar declares the additional texture slots and the instance and global uniforms.
/// The processor validates the names, types, and defaults (<see
/// cref="BakeDiagnosticCodes.ShaderMetadataInvalid"/>), packs the instance declarations into the
/// 16-float block and the global declarations into eight <c>vec4</c> slots in ordinal name order
/// (<see cref="BakeDiagnosticCodes.ShaderUniformOverflow"/> for overflow), and emits the metadata in
/// that same order. A compilation failure is <see
/// cref="BakeDiagnosticCodes.ShaderCompileFailed"/> and emits no outputs.
/// </para>
/// <para>
/// The processor is synchronous, runs on the bake thread, and keeps no per-source state, matching the
/// processor contract.
/// </para>
/// </remarks>
internal sealed class ShaderProcessor : AssetProcessor<ShaderSidecar>
{
    private const string PayloadSuffix = "-spv";

    private const string EntryPointName = "main";

    private const int MaxTextureSlots = 4;

    private static readonly string[] _extensions = ["frag"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <inheritdoc/>
    public override string Name => "shader";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    protected override ShaderSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A missing sidecar means no texture slots and no uniforms. The framework writes the empty
        // sidecar and binds it back, so an absent and an empty sidecar describe the same shader.
        return new ShaderSidecar();
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, ShaderSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        if (!TryReadSource(context, out string source))
        {
            return;
        }

        if (TryFindForbiddenDirective(source, out string directive, out int directiveLine))
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderSourceInvalid,
                $"The shader source '{context.RelativePath}' contains the forbidden "
                    + $"'{directive}' directive at line {directiveLine}. The bake supplies "
                    + "'#version 460', its required extension, the fixed interface, and the 'main' "
                    + "entry point; remove the directive from the source.");
            return;
        }

        PackedLayout? layout = Pack(context, sidecar);
        if (layout is null)
        {
            return;
        }

        string prologue = ShaderPrologue.Build(
            layout.InstanceUniforms,
            layout.GlobalUniforms);
        string combined = ShaderPrologue.Compose(prologue, source);
        string fileName = context.SourceFileName;
        if (!GlslCompiler.TryCompileFragment(
                combined,
                fileName,
                out byte[] spirV,
                out string message))
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderCompileFailed,
                FormatCompileFailure(
                    fileName,
                    ShaderPrologue.CountLines(prologue),
                    message));
            return;
        }

        string payloadPath = context.BaseAssetPath + PayloadSuffix;
        var shader = new ShaderAsset
        {
            SpirV = payloadPath,
            LayoutVersion = ShaderLayout.CurrentVersion,
            EntryPoint = EntryPointName,
            InstanceUniforms = layout.InstanceUniforms,
            GlobalUniforms = layout.GlobalUniforms,
            TextureSlots = layout.TextureSlots,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, shader);
        context.Outputs.AddData(payloadPath, spirV);
    }

    private static bool TryReadSource(AssetProcessorContext context, out string source)
    {
        source = string.Empty;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderSourceInvalid,
                $"Failed to read the shader source '{context.RelativePath}': {exception.Message}");
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        try
        {
            source = _strictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderSourceInvalid,
                $"The shader source '{context.RelativePath}' is not valid UTF-8.");
            return false;
        }
    }

    private static bool TryFindForbiddenDirective(
        string source,
        out string directive,
        out int line)
    {
        directive = string.Empty;
        line = 0;

        string[] lines = source.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string trimmed = lines[index].TrimStart();
            if (StartsWithDirective(trimmed, "#version"))
            {
                directive = "#version";
                line = index + 1;
                return true;
            }

            if (StartsWithDirective(trimmed, "#extension"))
            {
                directive = "#extension";
                line = index + 1;
                return true;
            }
        }

        return false;
    }

    private static bool StartsWithDirective(string line, string name)
        => line.StartsWith(name, StringComparison.Ordinal)
            && (line.Length == name.Length || char.IsWhiteSpace(line[name.Length]));

    private static PackedLayout? Pack(AssetProcessorContext context, ShaderSidecar sidecar)
    {
        bool valid = true;

        if (!TryPackTextureSlots(context, sidecar.TextureSlots, out ShaderTextureSlot[] textureSlots))
        {
            valid = false;
            textureSlots = [];
        }

        var declaredNames = new HashSet<string>(StringComparer.Ordinal);

        if (!TryPackInstanceUniforms(
                context,
                sidecar.Uniforms,
                declaredNames,
                out ShaderUniform[] instanceUniforms))
        {
            valid = false;
            instanceUniforms = [];
        }

        if (!TryPackGlobalUniforms(
                context,
                sidecar.Globals,
                declaredNames,
                out ShaderUniform[] globalUniforms))
        {
            valid = false;
            globalUniforms = [];
        }

        return valid
            ? new PackedLayout(textureSlots, instanceUniforms, globalUniforms)
            : null;
    }

    private static bool TryPackTextureSlots(
        AssetProcessorContext context,
        string[]? declared,
        out ShaderTextureSlot[] textureSlots)
    {
        textureSlots = [];
        if (declared is null)
        {
            return true;
        }

        if (declared.Length > MaxTextureSlots)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderMetadataInvalid,
                $"The sidecar declares {declared.Length} texture slots; at most "
                    + $"{MaxTextureSlots} are supported.");
            return false;
        }

        bool valid = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>(declared.Length);
        foreach (string? name in declared)
        {
            if (name is null || !IsGlslIdentifier(name))
            {
                context.Diagnostics.Error(
                    BakeDiagnosticCodes.ShaderMetadataInvalid,
                    $"The texture slot name '{name ?? "(null)"}' is not a valid GLSL identifier.");
                valid = false;
                continue;
            }

            if (!seen.Add(name))
            {
                context.Diagnostics.Error(
                    BakeDiagnosticCodes.ShaderMetadataInvalid,
                    $"The texture slot name '{name}' is declared more than once.");
                valid = false;
                continue;
            }

            names.Add(name);
        }

        if (!valid)
        {
            return false;
        }

        textureSlots = [.. names.Select(name => new ShaderTextureSlot(name))];
        return true;
    }

    private static bool TryPackInstanceUniforms(
        AssetProcessorContext context,
        Dictionary<string, ShaderUniformSidecar>? declared,
        HashSet<string> declaredNames,
        out ShaderUniform[] uniforms)
    {
        uniforms = [];
        if (declared is null || declared.Count == 0)
        {
            return true;
        }

        var packed = new List<ShaderUniform>(declared.Count);
        bool valid = true;
        int nextLane = 0;

        foreach (string name in declared.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            ShaderUniformSidecar declaration = declared[name];
            if (!TryValidateName(context, name, "uniform", declaredNames)
                || !TryParseDeclaredType(context, name, declaration.Type, out ShaderUniformType type)
                || !TryParseDefault(
                    context,
                    name,
                    declaration.Type!,
                    type,
                    declaration.Default,
                    out Vector4 defaultValue))
            {
                valid = false;
                continue;
            }

            int componentCount = ShaderLayout.ComponentCount(type);
            int alignment = componentCount switch
            {
                1 => 1,
                2 => 2,
                _ => 4,
            };
            int offset = RoundUp(nextLane, alignment);
            if (offset > ShaderLayout.InstanceBlockFloatCount - componentCount)
            {
                context.Diagnostics.Error(
                    BakeDiagnosticCodes.ShaderUniformOverflow,
                    $"The instance uniform '{name}' at float lane {offset} does not fit the "
                        + $"{ShaderLayout.InstanceBlockFloatCount}-float instance block.");
                valid = false;
                continue;
            }

            packed.Add(new ShaderUniform(name, type, offset, defaultValue));
            nextLane = offset + componentCount;
        }

        if (!valid)
        {
            return false;
        }

        uniforms = [.. packed];
        return true;
    }

    private static bool TryPackGlobalUniforms(
        AssetProcessorContext context,
        Dictionary<string, ShaderUniformSidecar>? declared,
        HashSet<string> declaredNames,
        out ShaderUniform[] uniforms)
    {
        uniforms = [];
        if (declared is null || declared.Count == 0)
        {
            return true;
        }

        string[] names = [.. declared.Keys.OrderBy(key => key, StringComparer.Ordinal)];
        if (names.Length > ShaderLayout.GlobalBlockSlotCount)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderUniformOverflow,
                $"The sidecar declares {names.Length} global uniforms; at most "
                    + $"{ShaderLayout.GlobalBlockSlotCount} are supported.");
            return false;
        }

        var packed = new List<ShaderUniform>(names.Length);
        bool valid = true;
        for (int slot = 0; slot < names.Length; slot++)
        {
            string name = names[slot];
            ShaderUniformSidecar declaration = declared[name];
            if (!TryValidateName(context, name, "global uniform", declaredNames)
                || !TryParseDeclaredType(context, name, declaration.Type, out ShaderUniformType type)
                || !TryParseDefault(
                    context,
                    name,
                    declaration.Type!,
                    type,
                    declaration.Default,
                    out Vector4 defaultValue))
            {
                valid = false;
                continue;
            }

            packed.Add(new ShaderUniform(name, type, slot * 4, defaultValue));
        }

        if (!valid)
        {
            return false;
        }

        uniforms = [.. packed];
        return true;
    }

    private static bool TryValidateName(
        AssetProcessorContext context,
        string name,
        string kind,
        HashSet<string> declaredNames)
    {
        if (!IsGlslIdentifier(name))
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderMetadataInvalid,
                $"The {kind} name '{name}' is not a valid GLSL identifier.");
            return false;
        }

        if (IsReservedShaderName(name))
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderMetadataInvalid,
                $"The {kind} name '{name}' is reserved; a declared name must not start with "
                    + "'gl_', 'BORK2D_', or 'bork2d'.");
            return false;
        }

        if (!declaredNames.Add(name))
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderMetadataInvalid,
                $"The {kind} name '{name}' is declared more than once across [uniforms] and "
                    + "[globals].");
            return false;
        }

        return true;
    }

    private static bool TryParseDeclaredType(
        AssetProcessorContext context,
        string name,
        string? declaredType,
        out ShaderUniformType type)
    {
        switch (declaredType)
        {
            case "float":
                type = ShaderUniformType.Float;
                return true;

            case "vec2":
                type = ShaderUniformType.Vector2;
                return true;

            case "vec3":
                type = ShaderUniformType.Vector3;
                return true;

            case "vec4":
                type = ShaderUniformType.Vector4;
                return true;

            case "color":
                type = ShaderUniformType.Color;
                return true;

            default:
                context.Diagnostics.Error(
                    BakeDiagnosticCodes.ShaderMetadataInvalid,
                    $"The uniform '{name}' declares "
                        + (declaredType is null ? "no 'type' key" : $"the type '{declaredType}'")
                        + "; expected one of float, vec2, vec3, vec4, color (case-sensitive).");
                type = default;
                return false;
        }
    }

    private static bool TryParseDefault(
        AssetProcessorContext context,
        string name,
        string declaredType,
        ShaderUniformType type,
        TomlNumberOrArray? declared,
        out Vector4 value)
    {
        value = Vector4.Zero;
        if (declared is null)
        {
            return true;
        }

        int expected = ShaderLayout.ComponentCount(type);
        IReadOnlyList<double> components = declared.Values;
        if (components.Count != expected)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderMetadataInvalid,
                $"The uniform '{name}' default has {components.Count} component(s); type "
                    + $"'{declaredType}' requires {expected}.");
            return false;
        }

        var parsed = new float[4];
        for (int index = 0; index < expected; index++)
        {
            float component = (float)components[index];
            if (!float.IsFinite(component))
            {
                context.Diagnostics.Error(
                    BakeDiagnosticCodes.ShaderMetadataInvalid,
                    $"The uniform '{name}' default component {index} is not finite.");
                return false;
            }

            parsed[index] = component;
        }

        value = new Vector4(parsed[0], parsed[1], parsed[2], parsed[3]);
        return true;
    }

    private static int RoundUp(int value, int alignment)
        => ((value + alignment - 1) / alignment) * alignment;

    private static bool IsGlslIdentifier(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        if (!IsIdentifierStart(name[0]))
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

    private static bool IsIdentifierStart(char character)
        => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or '_';

    private static bool IsIdentifierPart(char character)
        => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_';

    private static bool IsReservedShaderName(string name)
        => name.StartsWith("gl_", StringComparison.Ordinal)
            || name.StartsWith("BORK2D_", StringComparison.Ordinal)
            || name.StartsWith("bork2d", StringComparison.Ordinal);

    private static string FormatCompileFailure(
        string fileName,
        int prologueLineCount,
        string message)
    {
        string diagnostics = AdjustLineNumbers(message, fileName, prologueLineCount);
        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            diagnostics = "shaderc reported no diagnostic text.";
        }

        return $"Failed to compile the shader '{fileName}'. {diagnostics.Trim()} "
            + "Line numbers refer to the shader source, not the fixed prologue.";
    }

    /// <summary>
    /// Maps the combined source's line numbers back to the shader source by subtracting the fixed
    /// prologue line count.
    /// </summary>
    /// <remarks>
    /// Shaderc reports <c>file:line: message</c>. A line that falls inside the prologue keeps its
    /// number rather than becoming negative; a broken prologue is an engine defect, not user input.
    /// </remarks>
    private static string AdjustLineNumbers(string message, string fileName, int lineOffset)
    {
        if (lineOffset <= 0 || message.Length == 0)
        {
            return message;
        }

        string pattern = Regex.Escape(fileName) + @":(\d+):";
        return Regex.Replace(
            message,
            pattern,
            match =>
            {
                if (!int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int line))
                {
                    return match.Value;
                }

                int adjusted = line > lineOffset ? line - lineOffset : line;
                return fileName + ":" + adjusted.ToString(CultureInfo.InvariantCulture) + ":";
            });
    }

    private sealed class PackedLayout
    {
        internal PackedLayout(
            ShaderTextureSlot[] textureSlots,
            ShaderUniform[] instanceUniforms,
            ShaderUniform[] globalUniforms)
        {
            TextureSlots = textureSlots;
            InstanceUniforms = instanceUniforms;
            GlobalUniforms = globalUniforms;
        }

        internal ShaderTextureSlot[] TextureSlots { get; }

        internal ShaderUniform[] InstanceUniforms { get; }

        internal ShaderUniform[] GlobalUniforms { get; }
    }
}
