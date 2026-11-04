using System.Buffers.Binary;
using System.Numerics;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Shaders;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.Shaders;

/// <summary>
/// The built-in fragment shader processor through the real builder and pack pipeline: arbitrary
/// output paths, the SPIR-V payload, sidecar packing and defaults, the fixed prologue macros,
/// metadata diagnostics, compilation failure, and sidecar creation.
/// </summary>
public sealed class ShaderProcessorTests
{
    private const string ShaderType = "Bork2d.Engine.Assets.ShaderAsset";

    private const string MinimalShaderSource = """
        void main()
        {
            outColor = vTint * texture(BORK2D_MAIN_TEXTURE, vUv);
        }
        """;

    private const string PackedShaderSource = """
        void main()
        {
            vec4 color = texture(BORK2D_MAIN_TEXTURE, vUv) * vTint;
            color += texture(BORK2D_TEXTURE0, vUv) * uPulse;
            color += texture(BORK2D_TEXTURE1, vUv) * uOffset.x;
            color += texture(BORK2D_TEXTURE2, vUv) * uDirection.x;
            color += texture(BORK2D_TEXTURE3, vUv) * uTint.x;
            color.rgb *= 0.5 + BORK2D_TIME + uTimeScale + uWind.x;
            outColor = color;
        }
        """;

    private const string PackedSidecarToml = """
        textureSlots = ["mask", "noise"]

        [uniforms]
        uPulse = { type = "float", default = 1.0 }
        uTint = { type = "color", default = [1.0, 0.5, 0.25, 1.0] }
        uOffset = { type = "vec2" }
        uDirection = { type = "vec3", default = [0.0, 1.0, 0.0] }

        [globals]
        uWind = { type = "vec2", default = [0.0, 2.0] }
        uTimeScale = { type = "float" }
        """;

    [Fact]
    public void IncrementalMarkerVersionsTheCompilerLayoutPrologueAndPacker()
    {
        Assert.Equal(
            "shaderc-2.23.0/layout-3/prologue-4/packer-1",
            ShaderToolInfo.ConfigurationMarker);
        Assert.Contains(
            $"layout-{ShaderLayout.CurrentVersion}",
            ShaderToolInfo.ConfigurationMarker,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BakesShaderAssetAndSpirVPayloadAtArbitraryPaths()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText("materials/lit.frag", PackedShaderSource);
        project.WriteText("materials/lit.frag.toml", PackedSidecarToml);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.AssetCount);
        Assert.Equal(
            ["materials/lit", "materials/lit-spv"],
            result.Entries.Select(entry => entry.Path));

        BakedAssetEntry shaderEntry = result.Entries[0];
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, shaderEntry.Kind);
        Assert.Equal(ShaderType, shaderEntry.TypeName);
        BakedAssetEntry payloadEntry = result.Entries[1];
        Assert.Equal(BakedAssetEntryKind.ProcessorData, payloadEntry.Kind);
        Assert.True(payloadEntry.IsRaw);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();

        byte[] spirV = await host.Service.LoadData("materials/lit-spv");
        Assert.True(spirV.Length > 4);
        Assert.Equal(0x07230203u, BinaryPrimitives.ReadUInt32LittleEndian(spirV));

        ShaderAsset shader = await host.Service.LoadAsset<ShaderAsset>("materials/lit");
        Assert.Equal("materials/lit-spv", shader.SpirV.Path);
        Assert.Equal(ShaderLayout.CurrentVersion, shader.LayoutVersion);
        Assert.Equal("main", shader.EntryPoint);

        Assert.Equal(
            new[]
            {
                new ShaderUniform("uDirection", ShaderUniformType.Vector3, 0, new Vector4(0f, 1f, 0f, 0f)),
                new ShaderUniform("uOffset", ShaderUniformType.Vector2, 4, Vector4.Zero),
                new ShaderUniform("uPulse", ShaderUniformType.Float, 6, new Vector4(1f, 0f, 0f, 0f)),
                new ShaderUniform("uTint", ShaderUniformType.Color, 8, new Vector4(1f, 0.5f, 0.25f, 1f)),
            },
            shader.InstanceUniforms);

        Assert.Equal(
            new[]
            {
                new ShaderUniform("uTimeScale", ShaderUniformType.Float, 0, Vector4.Zero),
                new ShaderUniform("uWind", ShaderUniformType.Vector2, 4, new Vector4(0f, 2f, 0f, 0f)),
            },
            shader.GlobalUniforms);

        Assert.Equal(["mask", "noise"], shader.TextureSlots.Select(slot => slot.Name));
    }

    [Fact]
    public async Task BuiltInLineShaderBakesAsAVersionThreeAssetWithASpirVPayload()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText(
            "shader/line.frag",
            File.ReadAllText(
                Path.Combine(
                    RepositoryPaths.Root,
                    "src",
                    "Bork2d",
                    "BuiltInAssets",
                    "Assets",
                    "shader",
                    "line.frag")));
        project.WriteText("shader/line.frag.toml", string.Empty);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(
            ["shader/line", "shader/line-spv"],
            result.Entries.Select(entry => entry.Path));

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();

        byte[] spirV = await host.Service.LoadData("shader/line-spv");
        Assert.True(spirV.Length > sizeof(uint));
        Assert.Equal(0x07230203u, BinaryPrimitives.ReadUInt32LittleEndian(spirV));

        ShaderAsset shader = await host.Service.LoadAsset<ShaderAsset>("shader/line");
        Assert.Equal("shader/line-spv", shader.SpirV.Path);
        Assert.Equal(3u, shader.LayoutVersion);
        Assert.Equal(ShaderLayout.CurrentVersion, shader.LayoutVersion);
        Assert.Equal("main", shader.EntryPoint);
        Assert.Empty(shader.InstanceUniforms);
        Assert.Empty(shader.GlobalUniforms);
        Assert.Empty(shader.TextureSlots);
    }

    [Fact]
    public async Task PackingAlignsWideTypesToVec4LanesAndSortsByOrdinalName()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText("materials/order.frag", MinimalShaderSource);
        project.WriteText(
            "materials/order.frag.toml",
            """
            [uniforms]
            bVector = { type = "vec3" }
            aFloat = { type = "float" }
            cVector = { type = "vec2" }
            """);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        ShaderAsset shader = await host.Service.LoadAsset<ShaderAsset>("materials/order");

        Assert.Equal(
            new[]
            {
                new ShaderUniform("aFloat", ShaderUniformType.Float, 0, Vector4.Zero),
                new ShaderUniform("bVector", ShaderUniformType.Vector3, 4, Vector4.Zero),
                new ShaderUniform("cVector", ShaderUniformType.Vector2, 8, Vector4.Zero),
            },
            shader.InstanceUniforms);
    }

    [Fact]
    public void PrologueMacrosUseTheDeclaredOffsetsAndParenthesizeEveryExpansion()
    {
        ShaderUniform[] instanceUniforms =
        [
            new("uPulse", ShaderUniformType.Float, 0, Vector4.UnitX),
            new("uOffset", ShaderUniformType.Vector2, 2, Vector4.Zero),
            new("uDirection", ShaderUniformType.Vector3, 4, Vector4.Zero),
            new("uTint", ShaderUniformType.Color, 8, Vector4.One),
        ];
        ShaderUniform[] globalUniforms =
        [
            new("uWind", ShaderUniformType.Vector2, 4, Vector4.Zero),
        ];

        string prologue = ShaderPrologue.Build(instanceUniforms, globalUniforms);

        Assert.Contains("#version 460", prologue, StringComparison.Ordinal);
        Assert.Contains(
            "#extension GL_EXT_nonuniform_qualifier : require",
            prologue,
            StringComparison.Ordinal);
        Assert.Contains("layout(location = 5) flat in uint vExtraTextureIndices[4];", prologue, StringComparison.Ordinal);
        Assert.Contains("layout(location = 9) in vec4 vInstanceParams[4];", prologue, StringComparison.Ordinal);
        Assert.Contains("layout(location = 13) in vec2 vLineUv;", prologue, StringComparison.Ordinal);
        Assert.Contains("layout(location = 14) in vec4 vColor;", prologue, StringComparison.Ordinal);
        Assert.Contains("layout(location = 15) flat in float vLineFlags;", prologue, StringComparison.Ordinal);
        Assert.Contains("layout(location = 0) out vec4 outColor;", prologue, StringComparison.Ordinal);
        Assert.Contains(
            "layout(set = 0, binding = 1) uniform sampler2D bork2dTextures[];",
            prologue,
            StringComparison.Ordinal);
        Assert.Contains(
            "layout(set = 1, binding = 2) uniform Bork2dGlobalBlock",
            prologue,
            StringComparison.Ordinal);
        Assert.Contains("#define uPulse (vInstanceParams[0].x)", prologue, StringComparison.Ordinal);
        Assert.Contains("#define uOffset (vInstanceParams[0].zw)", prologue, StringComparison.Ordinal);
        Assert.Contains("#define uDirection (vInstanceParams[1].xyz)", prologue, StringComparison.Ordinal);
        Assert.Contains("#define uTint (vInstanceParams[2].xyzw)", prologue, StringComparison.Ordinal);
        Assert.Contains("#define uWind (bork2dGlobals.globals[1].xy)", prologue, StringComparison.Ordinal);
        Assert.Equal(31 + instanceUniforms.Length + globalUniforms.Length, ShaderPrologue.CountLines(prologue));
    }

    [Fact]
    public void EpilogueConsumesTheSharedSpriteAndLineInputs()
    {
        Assert.Contains("bork2d_user_main();", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("if (extra > 0x003FFFFFu)", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vUv", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vTint", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vWorldPosition", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vLocalPosition", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vTextureIndex", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vExtraTextureIndices[3]", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vInstanceParams[3]", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vLineUv", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vColor", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("vLineFlags", ShaderPrologue.Epilogue, StringComparison.Ordinal);
        Assert.Contains("outColor", ShaderPrologue.Epilogue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSidecarCreatesAnEmptyDefaultAndBakesWithoutUniforms()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText("materials/lit.frag", MinimalShaderSource);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        BakeDiagnostic created = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(BakeSeverity.Info, created.Severity);
        Assert.Equal(ShaderTestProject.DisplayPath("materials/lit.frag.toml"), created.FilePath);

        string sidecarPath = project.FullPath("materials/lit.frag.toml");
        Assert.True(File.Exists(sidecarPath));
        Assert.Equal(string.Empty, File.ReadAllText(sidecarPath));

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        ShaderAsset shader = await host.Service.LoadAsset<ShaderAsset>("materials/lit");
        Assert.Empty(shader.InstanceUniforms);
        Assert.Empty(shader.GlobalUniforms);
        Assert.Empty(shader.TextureSlots);
    }

    [Fact]
    public async Task EmptyExistingSidecarBakesWithoutUniforms()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText("materials/lit.frag", MinimalShaderSource);
        project.WriteText("materials/lit.frag.toml", string.Empty);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        ShaderAsset shader = await host.Service.LoadAsset<ShaderAsset>("materials/lit");
        Assert.Empty(shader.InstanceUniforms);
        Assert.Empty(shader.GlobalUniforms);
        Assert.Empty(shader.TextureSlots);
    }

    [Fact]
    public void CompileFailureIsBK9002WithTheSourceLineAndWritesNoPack()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText(
            "materials/lit.frag",
            """
            void main()
            {
                outColor = missingUniform;
            }
            """);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ShaderCompileFailed);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Equal(ShaderTestProject.DisplayPath("materials/lit.frag"), diagnostic.FilePath);
        Assert.Contains("lit.frag:3", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(project.PackPath));
        Assert.DoesNotContain(
            result.Entries,
            entry => entry.Path == "materials/lit" || entry.Path == "materials/lit-spv");
    }

    [Fact]
    public void CompileFailureLineNumbersIgnoreTheFixedPrologue()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText(
            "materials/lit.frag",
            """
            void main()
            {
                outColor = (;
            }
            """);
        project.WriteText(
            "materials/lit.frag.toml",
            """
            [uniforms]
            uPulse = { type = "float" }
            """);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ShaderCompileFailed);
        Assert.Contains("lit.frag:3", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(project.PackPath));
    }

    [Theory]
    [InlineData("  #version 460\nvoid main() { outColor = vec4(1.0); }\n", "#version")]
    [InlineData(
        "#extension GL_EXT_debug_printf : enable\nvoid main() { outColor = vec4(1.0); }\n",
        "#extension")]
    public void ForbiddenDirectiveIsBK9001(string source, string directive)
    {
        using var project = ShaderTestProject.Create();
        project.WriteText("materials/lit.frag", source);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ShaderSourceInvalid);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Contains(directive, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("line 1", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("bake supplies", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(project.PackPath));
    }

    [Fact]
    public void InvalidUtf8SourceIsBK9001()
    {
        using var project = ShaderTestProject.Create();
        project.WriteBytes("materials/lit.frag", [0x76, 0x6F, 0x69, 0x64, 0x20, 0xFF, 0xFE]);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ShaderSourceInvalid);
        Assert.Contains("not valid UTF-8", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(project.PackPath));
    }

    [Fact]
    public void UnknownUniformTypeIsBK9003()
        => AssertSidecarError(
            """
            [uniforms]
            uFoo = { type = "vec5" }
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "uFoo");

    [Fact]
    public void MissingUniformTypeIsBK9003()
        => AssertSidecarError(
            """
            [uniforms]
            uFoo = { default = 1.0 }
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "no 'type' key");

    [Fact]
    public void WrongDefaultComponentCountIsBK9003()
        => AssertSidecarError(
            """
            [uniforms]
            uFoo = { type = "vec2", default = 1.0 }
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "requires 2");

    [Fact]
    public void NonFiniteDefaultIsBK9003()
        => AssertSidecarError(
            """
            [uniforms]
            uFoo = { type = "float", default = nan }
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "not finite");

    [Fact]
    public void InvalidUniformIdentifierIsBK9003()
        => AssertSidecarError(
            """
            [uniforms]
            "u cost" = { type = "float" }
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "not a valid GLSL identifier");

    [Theory]
    [InlineData("gl_Value")]
    [InlineData("BORK2D_Value")]
    [InlineData("bork2dValue")]
    public void ReservedUniformNameIsBK9003(string name)
        => AssertSidecarError(
            "[uniforms]\n" + name + " = { type = \"float\" }\n",
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "is reserved");

    [Fact]
    public void DuplicateNameAcrossInstanceAndGlobalIsBK9003()
        => AssertSidecarError(
            """
            [uniforms]
            uShared = { type = "float" }

            [globals]
            uShared = { type = "float" }
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "more than once");

    [Fact]
    public void TooManyTextureSlotsIsBK9003()
        => AssertSidecarError(
            """
            textureSlots = ["a", "b", "c", "d", "e"]
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "at most 4");

    [Fact]
    public void DuplicateTextureSlotIsBK9003()
        => AssertSidecarError(
            """
            textureSlots = ["mask", "mask"]
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "more than once");

    [Fact]
    public void InvalidTextureSlotNameIsBK9003()
        => AssertSidecarError(
            """
            textureSlots = ["bad slot"]
            """,
            BakeDiagnosticCodes.ShaderMetadataInvalid,
            "not a valid GLSL identifier");

    [Fact]
    public void InstanceUniformOverflowIsBK9004()
        => AssertSidecarError(
            """
            [uniforms]
            uA = { type = "vec4" }
            uB = { type = "vec4" }
            uC = { type = "vec4" }
            uD = { type = "vec4" }
            uE = { type = "vec4" }
            """,
            BakeDiagnosticCodes.ShaderUniformOverflow,
            "instance block");

    [Fact]
    public void GlobalUniformOverflowIsBK9004()
        => AssertSidecarError(
            """
            [globals]
            uA = { type = "float" }
            uB = { type = "float" }
            uC = { type = "float" }
            uD = { type = "float" }
            uE = { type = "float" }
            uF = { type = "float" }
            uG = { type = "float" }
            uH = { type = "float" }
            uI = { type = "float" }
            """,
            BakeDiagnosticCodes.ShaderUniformOverflow,
            "at most 8");

    [Fact]
    public void CheckBakeCompilesWithoutWritingSidecarOrPack()
    {
        using var project = ShaderTestProject.Create();
        project.WriteText("materials/lit.frag", MinimalShaderSource);

        BakeResult result = project.Bake(check: true);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.False(result.PackWritten);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.False(File.Exists(project.FullPath("materials/lit.frag.toml")));
        Assert.False(File.Exists(project.PackPath));
    }

    private static BakeDiagnostic AssertSidecarError(
        string sidecarToml,
        string expectedCode,
        string expectedMessagePart)
    {
        using var project = ShaderTestProject.Create();
        project.WriteText("materials/lit.frag", MinimalShaderSource);
        project.WriteText("materials/lit.frag.toml", sidecarToml);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(File.Exists(project.PackPath));
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == expectedCode);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Contains(expectedMessagePart, diagnostic.Message, StringComparison.Ordinal);
        return diagnostic;
    }
}
