using System.Buffers.Binary;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Shaders;

/// <summary>
/// The renderer's default-shader store: it loads the built-in sprite fragment shader and its module
/// from the app's asset service at startup and owns the fragment shader module.
/// </summary>
/// <remarks>
/// <para>
/// The built-in pack serves the shader <see cref="DefaultShaderPath"/> as a <see cref="ShaderAsset"/>
/// and its module <see cref="DefaultSpirVPath"/> as raw data. Both are loaded synchronously before
/// the first frame, from the same <see cref="IAssets"/> the texture pages use, so the renderer no
/// longer embeds generated SPIR-V arrays. A missing pack/shader, a malformed asset (null SPIR-V
/// reference, wrong layout version, wrong entry point), or a module without the SPIR-V magic is a
/// fatal startup <see cref="Bork2dException"/> naming the asset path and the reason.
/// </para>
/// <para>
/// The store owns its fragment <see cref="ShaderModule"/> and destroys it once; the pipeline owns the
/// vertex module and the graphics pipelines. <see cref="Dispose"/> is idempotent and never throws.
/// Render-thread-only.
/// </para>
/// </remarks>
internal sealed class ShaderStore : IDisposable
{
    /// <summary>The canonical path of the built-in sprite fragment shader asset.</summary>
    internal const string DefaultShaderPath = "shader/sprite";

    /// <summary>The canonical path of the built-in sprite fragment SPIR-V payload.</summary>
    internal const string DefaultSpirVPath = "shader/sprite-spv";

    /// <summary>The canonical path of the built-in sprite vertex SPIR-V payload.</summary>
    internal const string VertexSpirVPath = "shader/vertex-spv";

    /// <summary>The first little-endian word of every SPIR-V module.</summary>
    internal const uint SpirVMagic = 0x07230203;

    private const string EntryPointName = "main";

    private readonly SpritePipeline _pipeline;
    private ShaderModule _fragmentModule;
    private int _disposed;

    private ShaderStore(SpritePipeline pipeline, ShaderRenderData shader, ShaderModule fragmentModule)
    {
        _pipeline = pipeline;
        Shader = shader;
        _fragmentModule = fragmentModule;
    }

    /// <summary>Gets the frozen default fragment shader declarations.</summary>
    internal ShaderRenderData Shader { get; }

    /// <summary>Gets the fragment shader module the store owns.</summary>
    internal ShaderModule FragmentModule => _fragmentModule;

    /// <summary>
    /// Loads and validates the built-in sprite shader and creates its fragment module.
    /// </summary>
    /// <param name="assets">The app's started asset service.</param>
    /// <param name="pipeline">The sprite pipeline that creates the module.</param>
    /// <returns>The created store; the caller owns it.</returns>
    /// <exception cref="Bork2dException">
    /// The shader asset or module is missing, malformed, or has the wrong layout version, entry
    /// point, or SPIR-V magic.
    /// </exception>
    internal static ShaderStore Create(IAssets assets, SpritePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(pipeline);

        ShaderRenderData shader = LoadDefaultShader(assets);
        string spirVPath = shader.SpirV.Path
            ?? throw new Bork2dException(
                $"The built-in sprite shader asset '{DefaultShaderPath}' has no SPIR-V data "
                    + "reference.");
        byte[] spirV = LoadSpirV(
            assets,
            spirVPath,
            $"the built-in sprite fragment module referenced by '{DefaultShaderPath}'");
        ShaderModule module = pipeline.CreateFragmentModule(spirV, spirVPath);
        return new ShaderStore(pipeline, shader, module);
    }

    /// <summary>
    /// Loads and validates the built-in sprite <see cref="ShaderAsset"/> without creating a Vulkan
    /// object.
    /// </summary>
    /// <param name="assets">The app's started asset service.</param>
    /// <returns>The frozen shader declarations.</returns>
    /// <exception cref="Bork2dException">
    /// The asset is missing or malformed, or has an unsupported layout version or entry point.
    /// </exception>
    internal static ShaderRenderData LoadDefaultShader(IAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        ShaderAsset? asset = LoadBlocking(
            assets.LoadAsset<ShaderAsset>(DefaultShaderPath),
            DefaultShaderPath,
            "the built-in sprite shader asset");
        if (asset is null)
        {
            throw new Bork2dException(
                $"The built-in sprite shader asset '{DefaultShaderPath}' was not found in the "
                    + "mounted asset packs.");
        }

        if (!asset.SpirV.HasValue)
        {
            throw new Bork2dException(
                $"The built-in sprite shader asset '{DefaultShaderPath}' has no SPIR-V data "
                    + "reference.");
        }

        if (asset.LayoutVersion != ShaderLayout.CurrentVersion)
        {
            throw new Bork2dException(
                $"The built-in sprite shader asset '{DefaultShaderPath}' declares layout version "
                    + $"{asset.LayoutVersion}, but this engine implements version "
                    + $"{ShaderLayout.CurrentVersion}.");
        }

        if (!string.Equals(asset.EntryPoint, EntryPointName, StringComparison.Ordinal))
        {
            throw new Bork2dException(
                $"The built-in sprite shader asset '{DefaultShaderPath}' declares entry point "
                    + $"'{asset.EntryPoint ?? "(null)"}', but the renderer requires "
                    + $"'{EntryPointName}'.");
        }

        return ShaderRenderData.Freeze(asset);
    }

    /// <summary>
    /// Loads raw SPIR-V bytes and validates the non-empty module's little-endian magic word.
    /// </summary>
    /// <param name="assets">The app's started asset service.</param>
    /// <param name="path">The canonical raw-data path.</param>
    /// <param name="description">The diagnostic description of the shader stage/module.</param>
    /// <returns>The validated SPIR-V bytes.</returns>
    /// <exception cref="Bork2dException">
    /// The path is missing, the load fails, or the payload is not a SPIR-V module.
    /// </exception>
    internal static byte[] LoadSpirV(IAssets assets, string path, string description)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(description);

        byte[] spirV = LoadBlocking(assets.LoadData(path), path, description);
        if (spirV.Length < sizeof(uint))
        {
            throw new Bork2dException(
                $"{description} asset '{path}' is not a valid SPIR-V module: the payload is "
                    + $"{spirV.Length} bytes, shorter than the {sizeof(uint)}-byte magic word.");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(spirV);
        if (magic != SpirVMagic)
        {
            throw new Bork2dException(
                $"{description} asset '{path}' is not a valid SPIR-V module: its first little-endian "
                    + $"word is 0x{magic:X8}, not 0x{SpirVMagic:X8}.");
        }

        return spirV;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pipeline.DestroyFragmentModule(_fragmentModule);
        _fragmentModule = default;
    }

    private static T LoadBlocking<T>(Task<T> task, string path, string description)
    {
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new Bork2dException(
                $"{description} asset '{path}' could not be loaded: {exception.Message}",
                exception);
        }
    }
}
