using Bork2d.Render.Shaders;
using Silk.NET.Vulkan;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A recording <see cref="IShaderModuleFactory"/> for GPU-free catalog tests: it counts module and
/// pipeline creation/destruction and can fail on demand.
/// </summary>
internal sealed class FakeShaderModuleFactory : IShaderModuleFactory
{
    private readonly Dictionary<(ulong ContentHash, Format Format), Pipeline> _pipelines = [];
    private readonly List<(ulong ContentHash, Format Format)> _destroyedPipelines = [];

    /// <summary>Gets the names of the modules created, in call order.</summary>
    internal List<string> ModuleNames { get; } = [];

    /// <summary>Gets the number of modules created.</summary>
    internal int CreateModuleCount => ModuleNames.Count;

    /// <summary>Gets the number of modules destroyed.</summary>
    internal int DestroyModuleCount { get; private set; }

    /// <summary>Gets the pipeline keys created, in call order.</summary>
    internal List<(ulong ContentHash, Format Format)> CreatedPipelines { get; } = [];

    /// <summary>Gets the content hashes whose pipelines were destroyed, in call order.</summary>
    internal IReadOnlyList<(ulong ContentHash, Format Format)> DestroyedPipelines => _destroyedPipelines;

    /// <summary>Gets or sets an exception thrown by module creation.</summary>
    internal Exception? ModuleFailure { get; set; }

    /// <summary>Gets or sets an exception thrown by pipeline creation.</summary>
    internal Exception? PipelineFailure { get; set; }

    /// <inheritdoc/>
    public ShaderModule CreateModule(byte[] spirV, string name)
    {
        if (ModuleFailure is { } failure)
        {
            throw failure;
        }

        ModuleNames.Add(name);
        return default;
    }

    /// <inheritdoc/>
    public void DestroyModule(ShaderModule module) => DestroyModuleCount++;

    /// <inheritdoc/>
    public Pipeline GetOrCreatePipeline(
        Format colorFormat,
        ulong contentHash,
        ShaderModule module)
    {
        if (PipelineFailure is { } failure)
        {
            throw failure;
        }

        if (!_pipelines.TryGetValue((contentHash, colorFormat), out Pipeline pipeline))
        {
            pipeline = default;
            _pipelines.Add((contentHash, colorFormat), pipeline);
            CreatedPipelines.Add((contentHash, colorFormat));
        }

        return pipeline;
    }

    /// <inheritdoc/>
    public bool TryGetPipeline(ulong contentHash, Format colorFormat, out Pipeline pipeline)
        => _pipelines.TryGetValue((contentHash, colorFormat), out pipeline);

    /// <inheritdoc/>
    public void DestroyPipelines(ulong contentHash)
    {
        foreach ((ulong ContentHash, Format Format) key in _pipelines.Keys
            .Where(key => key.ContentHash == contentHash)
            .ToArray())
        {
            _ = _pipelines.Remove(key);
            _destroyedPipelines.Add(key);
        }
    }
}
