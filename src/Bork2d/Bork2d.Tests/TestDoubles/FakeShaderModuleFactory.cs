using Bork2d.Render.Shaders;
using Silk.NET.Vulkan;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A recording <see cref="IShaderModuleFactory"/> for GPU-free catalog tests: it counts module and
/// pipeline creation/destruction and can fail on demand.
/// </summary>
/// <remarks>
/// Module names are recorded in call order. Pipeline keys carry the vertex-stage kind so a test can
/// assert that one fragment content caches a separate sprite and line pipeline per output format.
/// </remarks>
internal sealed class FakeShaderModuleFactory : IShaderModuleFactory
{
    private readonly Dictionary<(ShaderPipelineKind Kind, ulong ContentHash, Format Format), Pipeline>
        _pipelines = [];
    private readonly List<(ShaderPipelineKind Kind, ulong ContentHash, Format Format)>
        _destroyedPipelines = [];

    /// <summary>Gets the names of the modules created, in call order.</summary>
    internal List<string> ModuleNames { get; } = [];

    /// <summary>Gets the kinds of the modules created, in call order.</summary>
    internal List<ShaderPipelineKind> ModuleKinds { get; } = [];

    /// <summary>Gets the number of modules created.</summary>
    internal int CreateModuleCount => ModuleNames.Count;

    /// <summary>Gets the number of modules destroyed.</summary>
    internal int DestroyModuleCount { get; private set; }

    /// <summary>Gets the pipeline keys created, in call order.</summary>
    internal List<(ShaderPipelineKind Kind, ulong ContentHash, Format Format)> CreatedPipelines
    {
        get;
    } = [];

    /// <summary>Gets the pipeline keys whose pipelines were destroyed, in call order.</summary>
    internal IReadOnlyList<(ShaderPipelineKind Kind, ulong ContentHash, Format Format)>
        DestroyedPipelines => _destroyedPipelines;

    /// <summary>Gets or sets an exception thrown by module creation.</summary>
    internal Exception? ModuleFailure { get; set; }

    /// <summary>Gets or sets an exception thrown by pipeline creation.</summary>
    internal Exception? PipelineFailure { get; set; }

    /// <inheritdoc/>
    public ShaderModule CreateModule(ShaderPipelineKind kind, byte[] spirV, string name)
    {
        if (ModuleFailure is { } failure)
        {
            throw failure;
        }

        ModuleNames.Add(name);
        ModuleKinds.Add(kind);
        return default;
    }

    /// <inheritdoc/>
    public void DestroyModule(ShaderPipelineKind kind, ShaderModule module)
        => DestroyModuleCount++;

    /// <inheritdoc/>
    public Pipeline GetOrCreatePipeline(
        ShaderPipelineKind kind,
        Format colorFormat,
        ulong contentHash,
        ShaderModule module)
    {
        if (PipelineFailure is { } failure)
        {
            throw failure;
        }

        if (!_pipelines.TryGetValue((kind, contentHash, colorFormat), out Pipeline pipeline))
        {
            pipeline = default;
            _pipelines.Add((kind, contentHash, colorFormat), pipeline);
            CreatedPipelines.Add((kind, contentHash, colorFormat));
        }

        return pipeline;
    }

    /// <inheritdoc/>
    public bool TryGetPipeline(
        ShaderPipelineKind kind,
        ulong contentHash,
        Format colorFormat,
        out Pipeline pipeline)
        => _pipelines.TryGetValue((kind, contentHash, colorFormat), out pipeline);

    /// <inheritdoc/>
    public void DestroyPipelines(ShaderPipelineKind kind, ulong contentHash)
    {
        foreach ((ShaderPipelineKind Kind, ulong ContentHash, Format Format) key in _pipelines.Keys
            .Where(key => key.Kind == kind && key.ContentHash == contentHash)
            .ToArray())
        {
            _ = _pipelines.Remove(key);
            _destroyedPipelines.Add(key);
        }
    }
}
