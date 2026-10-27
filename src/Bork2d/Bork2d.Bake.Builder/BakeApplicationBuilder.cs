using Bork2d.Bake.Baking;
using Bork2d.Bake.Builder.Processors;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Textures;
using PolyType;

namespace Bork2d.Bake.Builder;

/// <summary>
/// Configures the build-time asset pipeline for one assets executable: packs and roots, texture
/// defaults, explicit data-type registrations, and processors.
/// </summary>
/// <remarks>
/// <para>
/// The builder is the single source of truth for bake configuration; there is no project descriptor
/// and no generated host. The first <see cref="WithPack"/> call declares the main pack and every later
/// call declares a DLC/overlay pack. Relative roots and pack paths resolve against
/// <see cref="WithProjectRoot"/>; the build target passes <c>$(MSBuildProjectDirectory)</c>.
/// </para>
/// <para>
/// <see cref="RegisterAsset{T}"/> is the only data-type registration and closes over the compiled
/// <c>[GenerateShape]</c> metadata, so binding uses generic calls rather than reflection.
/// <see cref="UseDefaultAssets"/> registers the engine authoring types and
/// <see cref="UseDefaultProcessors"/> records the built-in processor registration.
/// </para>
/// <para>
/// Semantic configuration errors (a missing pack, a duplicate root or pack path, an escaping path, a
/// missing project root, or a duplicate registration) are <c>BK1xxx</c> diagnostics surfaced by
/// <see cref="BakeApplication.RunAsync(string[])"/> with exit code <c>2</c>; <see cref="Build"/> never
/// throws for them. Null or empty method arguments throw immediately because they are programmer errors.
/// </para>
/// </remarks>
public sealed class BakeApplicationBuilder
{
    private readonly BakeProjectDefinition _definition = new();

    /// <summary>Sets the base for relative pack roots, pack paths, and diagnostic display paths.</summary>
    /// <param name="path">The project root directory. The build target passes <c>$(MSBuildProjectDirectory)</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    public BakeApplicationBuilder WithProjectRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _definition.SetProjectRoot(path);
        return this;
    }

    /// <summary>Adds one ordered pack; the first added pack is the main pack.</summary>
    /// <param name="packPath">The pack output path relative to the output root, for example <c>assets.pck</c>.</param>
    /// <param name="root">The asset root relative to the project root, for example <c>Assets</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An argument is empty or whitespace.</exception>
    public BakeApplicationBuilder WithPack(string packPath, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _definition.AddPack(packPath, root);
        return this;
    }

    /// <summary>Registers the engine-provided authoring asset types; idempotent.</summary>
    /// <remarks>This currently registers <c>Bork2d.Api.Assets.SpriteAsset</c>.</remarks>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder UseDefaultAssets()
    {
        _definition.UseDefaultAssets();
        return this;
    }

    /// <summary>Registers one data type that a data TOML may select with <c>[bork2d].type</c>.</summary>
    /// <typeparam name="T">The data type, which must carry a compiled <c>[GenerateShape]</c>.</typeparam>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder RegisterAsset<T>()
        where T : IShapeable<T>
    {
        _definition.AddAsset<T>();
        return this;
    }

    /// <summary>Records that the built-in processor registration was requested; idempotent.</summary>
    /// <remarks>
    /// In WP2 this only records the request; there is no built-in processor yet. WP3 registers the
    /// built-in texture processor here. Until then the legacy texture pipeline remains the producer
    /// for image sources that no custom processor claims.
    /// </remarks>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder UseDefaultProcessors()
    {
        _definition.RequestDefaultProcessors();
        return this;
    }

    /// <summary>Sets the sampler defaults used by textures that have no sidecar.</summary>
    /// <param name="filter">The default sampler filter.</param>
    /// <param name="wrap">The default sampler wrap mode.</param>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder WithTextureDefaults(TextureFilter filter, TextureWrap wrap)
    {
        BakeTextureFilter internalFilter = filter == TextureFilter.Nearest
            ? BakeTextureFilter.Nearest
            : BakeTextureFilter.Linear;
        BakeTextureWrap internalWrap = wrap == TextureWrap.Repeat
            ? BakeTextureWrap.Repeat
            : BakeTextureWrap.Clamp;

        _definition.WithTextureDefaults(internalFilter, internalWrap);
        return this;
    }

    /// <summary>Registers a custom processor.</summary>
    /// <remarks>
    /// Registrations are stored in order. A later registration overrides an earlier one for the same
    /// normalized extension and reports an informational diagnostic. Invalid registrations (an empty
    /// name, no extensions, an invalid extension, or a sidecar policy/shape mismatch) are
    /// configuration diagnostics with exit code <c>2</c>.
    /// </remarks>
    /// <param name="processor">The processor instance.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="processor"/> is <see langword="null"/>.</exception>
    public BakeApplicationBuilder AddProcessor(IAssetProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _definition.AddProcessor(new BakeProcessorAdapter(processor));
        return this;
    }

    /// <summary>Builds the application. Configuration is validated when a command runs.</summary>
    /// <returns>The assets application that runs the CLI commands.</returns>
    public BakeApplication Build() => new(_definition);

    /// <summary>Validates this configuration into the immutable project used by the bake pipeline.</summary>
    /// <param name="diagnostics">The diagnostics bag to append configuration issues to.</param>
    /// <returns>The created project, or <see langword="null"/> when the project root is unusable.</returns>
    internal BakeProject? CreateProject(BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _ = BakeProjectFactory.TryCreate(
            _definition,
            projectRootOverride: null,
            diagnostics,
            out BakeProject? project);
        return project;
    }
}
