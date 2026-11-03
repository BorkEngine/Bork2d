using System.Buffers.Binary;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Shaders;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Render;

/// <summary>
/// GPU-free tests for the default-shader store's asset loading and validation: the built-in shader
/// must be present, declare the current layout version and the <c>main</c> entry point, reference
/// SPIR-V, and resolve to a module with the SPIR-V magic. Every failure names the asset path.
/// </summary>
public sealed class ShaderStoreTests
{
    private const uint SpirVMagic = 0x07230203;

    [Fact]
    public void ValidBuiltInShaderAndModuleLoadSuccessfully()
    {
        var assets = new FakeShaderAssets(CreateShader(), CreateSpirV());

        ShaderRenderData shader = ShaderStore.LoadDefaultShader(assets);
        byte[] spirV = ShaderStore.LoadSpirV(
            assets,
            ShaderStore.DefaultSpirVPath,
            "the built-in sprite fragment module");

        Assert.Equal(ShaderLayout.CurrentVersion, shader.LayoutVersion);
        Assert.Equal("main", shader.EntryPoint);
        Assert.Equal(ShaderStore.DefaultSpirVPath, shader.SpirV.Path);
        Assert.Equal(SpirVMagic, BinaryPrimitives.ReadUInt32LittleEndian(spirV));
    }

    [Fact]
    public void MissingShaderAssetIsAFatalStartupFailureNamingThePath()
    {
        var assets = new FakeShaderAssets(shader: null, spirV: CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullSpirVReferenceIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(
            CreateShader(spirV: DataRef.None),
            CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("SPIR-V", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedLayoutVersionIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(
            CreateShader(layoutVersion: ShaderLayout.CurrentVersion + 1),
            CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("layout version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonMainEntryPointIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(
            CreateShader(entryPoint: "fragment"),
            CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("entry point", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingModuleIsAFatalStartupFailureNamingThePath()
    {
        var assets = new FakeShaderAssets(CreateShader(), spirV: null);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadSpirV(
                assets,
                ShaderStore.DefaultSpirVPath,
                "the built-in sprite fragment module"));

        Assert.Contains(ShaderStore.DefaultSpirVPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModuleWithoutTheSpirVMagicIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(CreateShader(), [1, 2, 3, 4]);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadSpirV(
                assets,
                ShaderStore.DefaultSpirVPath,
                "the built-in sprite fragment module"));

        Assert.Contains(ShaderStore.DefaultSpirVPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("SPIR-V", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedModuleIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(CreateShader(), [0x03, 0x02, 0x23]);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadSpirV(
                assets,
                ShaderStore.DefaultSpirVPath,
                "the built-in sprite fragment module"));

        Assert.Contains(ShaderStore.DefaultSpirVPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("shorter", exception.Message, StringComparison.Ordinal);
    }

    private static ShaderAsset CreateShader(
        DataRef? spirV = null,
        uint layoutVersion = ShaderLayout.CurrentVersion,
        string entryPoint = "main")
        => new()
        {
            SpirV = spirV ?? ShaderStore.DefaultSpirVPath,
            LayoutVersion = layoutVersion,
            EntryPoint = entryPoint,
        };

    private static byte[] CreateSpirV()
    {
        var spirV = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(spirV, SpirVMagic);
        return spirV;
    }

    private sealed class FakeShaderAssets(ShaderAsset? shader, byte[]? spirV) : IAssets
    {
        public Task<T> LoadAsset<T>(string assetPath)
            where T : IShapeable<T>
        {
            if (typeof(T) != typeof(ShaderAsset))
            {
                throw new NotSupportedException($"Unsupported asset type {typeof(T)}.");
            }

            if (shader is null)
            {
                return Task.FromException<T>(
                    new AssetException(
                        AssetErrorKind.NotFound,
                        assetPath,
                        $"No asset at '{assetPath}'."));
            }

            return Task.FromResult((T)(object)shader);
        }

        public Task<T> LoadAsset<T>(AssetRef<T> reference)
            where T : IShapeable<T>
            => reference.HasValue
                ? LoadAsset<T>(reference.Path!)
                : throw new ArgumentException("The reference is absent.", nameof(reference));

        public Task<byte[]> LoadData(string assetPath)
            => spirV is not null
                ? Task.FromResult(spirV)
                : Task.FromException<byte[]>(
                    new AssetException(
                        AssetErrorKind.NotFound,
                        assetPath,
                        $"No data at '{assetPath}'."));

        public Task<byte[]> LoadData(DataRef reference)
            => reference.HasValue
                ? LoadData(reference.Path!)
                : throw new ArgumentException("The reference is absent.", nameof(reference));

        public void LoadPack(string packPath)
            => throw new NotSupportedException("Shader-store tests do not mount packs.");

        public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
            => throw new NotSupportedException("Shader-store tests do not browse assets.");

        public bool Exists(string assetPath)
            => throw new NotSupportedException("Shader-store tests do not query assets.");
    }
}
