using System.Buffers.Binary;
using System.IO.Hashing;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Shaders;

/// <summary>
/// The renderer's fragment-shader catalog: the three built-in default entries (sprite, line, and the
/// font SDF shader), the per-path custom entries, asynchronous SPIR-V loads, validation, the
/// engine-wide global-name registry, the content-hash module/pipeline caches, and catalog-remount
/// retirement.
/// </summary>
/// <remarks>
/// <para>
/// The built-in pack serves <see cref="DefaultShaderPath"/> (<c>shader/sprite</c>),
/// <see cref="LineShaderPath"/> (<c>shader/line</c>), and <see cref="SdfShaderPath"/>
/// (<c>shader/sdf</c>) as <see cref="ShaderAsset"/>s and their modules as raw data. All three are
/// loaded synchronously before the first frame, and a failure of any is a fatal startup
/// <see cref="Bork2dException"/>; the font default is required even when a game has no text. A
/// custom entry is keyed by its SPIR-V
/// <see cref="DataRef"/> path because a captured renderable already carries the frozen
/// <see cref="ShaderRenderData"/>; the renderer never reloads the <see cref="ShaderAsset"/> per draw.
/// </para>
/// <para>
/// A custom metadata set is validated once, when its path is first seen, and its SPIR-V payload is
/// validated when the asynchronous load completes. An invalid entry, a failed load, a module
/// creation failure, or a pipeline creation failure draws with the requesting kind's default shader
/// and logs one warning per shader path; the frame never crashes. The only fatal failures are the
/// built-in defaults. The engine-wide global-name registry tracks every live owner of a name: shaders
/// of the same type may share it, and the claim is released only when its last owner retires or
/// fails.
/// </para>
/// <para>
/// Loads started while planning a frame never complete that frame: <see cref="Pump"/> promotes
/// completed loads on the next frame. That keeps one frame's bindings and run grouping deterministic.
/// Modules are shared by shader content hash and pipelines by <c>(kind, content hash, output
/// format)</c>, so identical content shares its module while the sprite, line, and font graphics
/// pipelines stay distinct. A retired entry keeps its module and pipelines until
/// <see cref="SafetyAgeFrames"/> frame pumps have passed since the last frame that used them, matching
/// the two-frames-in-flight rule the texture pages use.
/// </para>
/// <para>
/// The catalog observes <see cref="IAssetCatalogVersion"/> in <see cref="Pump"/>, once per frame
/// before planning: a later mount retires every custom entry and starts a replacement load for all
/// three defaults. A previous ready default entry stays active until its replacement validates, so
/// the default path never loses its shader; the old entry is retired after the safety age once the
/// replacement is ready. A previously failed path is retried on its next sighting in the new
/// catalog. Render-thread-only.
/// </para>
/// </remarks>
internal sealed class ShaderStore : IShaderFrameSource, IDisposable
{
    /// <summary>The canonical path of the built-in sprite fragment shader asset.</summary>
    internal const string DefaultShaderPath = "shader/sprite";

    /// <summary>The canonical path of the built-in sprite fragment SPIR-V payload.</summary>
    internal const string DefaultSpirVPath = "shader/sprite-spv";

    /// <summary>The canonical path of the built-in sprite vertex SPIR-V payload.</summary>
    internal const string VertexSpirVPath = "shader/vertex-spv";

    /// <summary>The canonical path of the built-in line fragment shader asset.</summary>
    internal const string LineShaderPath = "shader/line";

    /// <summary>The canonical path of the built-in line fragment SPIR-V payload.</summary>
    internal const string LineSpirVPath = "shader/line-spv";

    /// <summary>The canonical path of the built-in line vertex SPIR-V payload.</summary>
    internal const string LineVertexSpirVPath = "shader/line-vertex-spv";

    /// <summary>The canonical path of the built-in font SDF fragment shader asset.</summary>
    internal const string SdfShaderPath = "shader/sdf";

    /// <summary>The canonical path of the built-in font SDF fragment SPIR-V payload.</summary>
    internal const string SdfSpirVPath = "shader/sdf-spv";

    /// <summary>The first little-endian word of every SPIR-V module.</summary>
    internal const uint SpirVMagic = 0x07230203;

    /// <summary>The entry id of the built-in sprite shader, used for sprite fallbacks.</summary>
    internal const int DefaultEntryId = 0;

    /// <summary>The entry id of the built-in line shader, used for line fallbacks.</summary>
    internal const int LineDefaultEntryId = 1;

    /// <summary>The entry id of the built-in font SDF shader, used for font fallbacks.</summary>
    internal const int FontDefaultEntryId = 2;

    /// <summary>The first entry id assigned to a custom shader.</summary>
    internal const int FirstCustomEntryId = 3;

    /// <summary>
    /// The number of frame pumps a retired module and its pipelines outlive their last use.
    /// </summary>
    /// <remarks>
    /// Two GPU frames are in flight. A frame planned at sequence <c>S</c> can still reference a
    /// pipeline, so retiring is safe only once <c>_frameSequence - LastUsedFrame &gt; 2</c>.
    /// </remarks>
    internal const int SafetyAgeFrames = 2;

    private const string EntryPointName = "main";

    /// <summary>The number of built-in default shader entries: sprite, line, and font SDF.</summary>
    private const int DefaultEntryCount = FontDefaultEntryId + 1;

    private readonly IAssets _assets;
    private readonly IShaderModuleFactory _modules;
    private readonly ILogger _logger;
    private readonly IAssetCatalogVersion? _catalogVersion;
    private readonly Dictionary<string, StoreEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<int, StoreEntry> _entriesById = [];
    private readonly Dictionary<ulong, ModuleRecord> _modulesByHash = [];
    private readonly List<ModuleRecord> _unreferencedModules = [];
    private readonly Dictionary<string, GlobalNameClaim> _globalNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedPaths = new(StringComparer.Ordinal);
    private readonly StoreEntry?[] _defaultEntries = new StoreEntry?[DefaultEntryCount];
    private readonly DefaultReload?[] _defaultReloads = new DefaultReload?[DefaultEntryCount];
    private int _nextEntryId = FirstCustomEntryId;
    private long _catalogVersionSeen;
    private long _frameSequence;
    private uint _loggedMissingSpirV;
    private int _disposed;

    private ShaderStore(
        IAssets assets,
        IShaderModuleFactory modules,
        IAssetCatalogVersion? catalogVersion,
        ILogger logger)
    {
        _assets = assets;
        _modules = modules;
        _logger = logger;
        _catalogVersion = catalogVersion;
        _catalogVersionSeen = catalogVersion?.CatalogVersion ?? 0;
    }

    /// <summary>Gets the entry id of the built-in sprite shader.</summary>
    internal ShaderEntry DefaultEntry => DefaultEntryOrThrow(ShaderPipelineKind.Sprite).Entry;

    /// <summary>Gets the entry id of the built-in line shader.</summary>
    internal ShaderEntry LineDefaultEntry => DefaultEntryOrThrow(ShaderPipelineKind.Line).Entry;

    /// <summary>Gets the entry id of the built-in font SDF shader.</summary>
    internal ShaderEntry FontDefaultEntry => DefaultEntryOrThrow(ShaderPipelineKind.Font).Entry;

    /// <summary>
    /// Loads and validates the three built-in shaders, registers their global names, and creates their
    /// fragment modules.
    /// </summary>
    /// <param name="assets">The app's started asset service.</param>
    /// <param name="modules">The Vulkan module/pipeline factory.</param>
    /// <param name="catalogVersion">
    /// The asset catalog identity seam, or <see langword="null"/> when the catalog cannot change.
    /// </param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for one-time warnings.</param>
    /// <returns>The created catalog; the caller owns it.</returns>
    /// <exception cref="Bork2dException">
    /// A built-in shader asset or module is missing, malformed, unsupported, or has an invalid
    /// declaration set. A default-shader failure is fatal startup.
    /// </exception>
    internal static ShaderStore Create(
        IAssets assets,
        IShaderModuleFactory modules,
        IAssetCatalogVersion? catalogVersion,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);

        var store = new ShaderStore(assets, modules, catalogVersion, logger);
        StoreEntry sprite = CreateDefaultEntry(
            store,
            assets,
            ShaderPipelineKind.Sprite,
            DefaultEntryId,
            DefaultShaderPath,
            "the built-in sprite shader asset");
        StoreEntry line = CreateDefaultEntry(
            store,
            assets,
            ShaderPipelineKind.Line,
            LineDefaultEntryId,
            LineShaderPath,
            "the built-in line shader asset");
        StoreEntry font = CreateDefaultEntry(
            store,
            assets,
            ShaderPipelineKind.Font,
            FontDefaultEntryId,
            SdfShaderPath,
            "the built-in font SDF shader asset");
        store._defaultEntries[(int)ShaderPipelineKind.Sprite] = sprite;
        store._defaultEntries[(int)ShaderPipelineKind.Line] = line;
        store._defaultEntries[(int)ShaderPipelineKind.Font] = font;
        store.AddGlobalNames(sprite);
        store.AddGlobalNames(line);
        store.AddGlobalNames(font);
        return store;
    }

    /// <summary>
    /// Loads and validates one built-in <see cref="ShaderAsset"/> without creating a Vulkan object.
    /// </summary>
    /// <param name="assets">The app's started asset service.</param>
    /// <param name="path">The canonical shader asset path.</param>
    /// <param name="description">The diagnostic description of the built-in shader asset.</param>
    /// <returns>The frozen shader declarations.</returns>
    /// <exception cref="Bork2dException">
    /// The asset is missing or malformed, or has an unsupported layout version or entry point.
    /// </exception>
    internal static ShaderRenderData LoadDefaultShader(
        IAssets assets,
        string path,
        string description)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(description);

        ShaderAsset? asset = LoadBlocking(
            assets.LoadAsset<ShaderAsset>(path),
            path,
            description);
        if (asset is null)
        {
            throw new Bork2dException(
                $"{char.ToUpperInvariant(description[0])}{description[1..]} '{path}' was not found in "
                    + "the mounted asset packs.");
        }

        if (!asset.SpirV.HasValue)
        {
            throw new Bork2dException(
                $"{char.ToUpperInvariant(description[0])}{description[1..]} '{path}' has no SPIR-V "
                    + "data reference.");
        }

        if (asset.LayoutVersion != ShaderLayout.CurrentVersion)
        {
            throw new Bork2dException(
                $"{char.ToUpperInvariant(description[0])}{description[1..]} '{path}' declares layout "
                    + $"version {asset.LayoutVersion}, but this engine implements version "
                    + $"{ShaderLayout.CurrentVersion}.");
        }

        if (!string.Equals(asset.EntryPoint, EntryPointName, StringComparison.Ordinal))
        {
            throw new Bork2dException(
                $"{char.ToUpperInvariant(description[0])}{description[1..]} '{path}' declares entry "
                    + $"point '{asset.EntryPoint ?? "(null)"}', but the renderer requires "
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
    public int GetDefaultEntryId(ShaderPipelineKind kind)
        => kind switch
        {
            ShaderPipelineKind.Sprite => DefaultEntryId,
            ShaderPipelineKind.Line => LineDefaultEntryId,
            _ => FontDefaultEntryId,
        };

    /// <inheritdoc/>
    public ShaderBinding Resolve(
        ShaderRenderData? shader,
        ShaderPipelineKind kind,
        Format outputFormat)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (shader is null)
        {
            return ResolveDefault(kind, outputFormat);
        }

        if (!shader.SpirV.HasValue)
        {
            WarnMissingSpirV(kind);
            return new ShaderBinding(GetDefaultEntryId(kind), ShaderBindingState.Failed);
        }

        string path = shader.SpirV.Path!;
        if (!_entries.TryGetValue(path, out StoreEntry? entry))
        {
            entry = CreateCustomEntry(path, shader);
            _entries.Add(path, entry);
            _entriesById.Add(entry.Id, entry);
        }

        return ResolveCustom(entry, kind, outputFormat);
    }

    /// <inheritdoc/>
    public ShaderEntry GetEntry(int entryId)
    {
        if (entryId == DefaultEntryId)
        {
            return DefaultEntryOrThrow(ShaderPipelineKind.Sprite).Entry;
        }

        if (entryId == LineDefaultEntryId)
        {
            return DefaultEntryOrThrow(ShaderPipelineKind.Line).Entry;
        }

        if (entryId == FontDefaultEntryId)
        {
            return DefaultEntryOrThrow(ShaderPipelineKind.Font).Entry;
        }

        if (_entriesById.TryGetValue(entryId, out StoreEntry? entry))
        {
            return entry.Entry;
        }

        throw new Bork2dException(
            $"Shader entry {entryId} is not in the current asset catalog.");
    }

    /// <summary>Gets the graphics pipeline for one ready entry, kind, and target output format.</summary>
    /// <param name="entryId">A ready entry id from <see cref="Resolve"/>.</param>
    /// <param name="kind">The vertex-stage kind the run draws with.</param>
    /// <param name="outputFormat">The target's sRGB output color format.</param>
    /// <returns>The cached pipeline.</returns>
    /// <exception cref="Bork2dException">The entry or its pipeline is not in the current catalog.</exception>
    internal Pipeline GetPipeline(
        int entryId,
        ShaderPipelineKind kind,
        Format outputFormat)
    {
        StoreEntry entry = FindEntry(entryId);
        if (entry.Module is not { } module
            || !_modules.TryGetPipeline(kind, module.ContentHash, outputFormat, out Pipeline pipeline))
        {
            throw new Bork2dException(
                $"Shader entry '{entry.Entry.Path}' has no {Describe(kind)} pipeline for output "
                    + $"format {outputFormat}.");
        }

        return pipeline;
    }

    /// <summary>Ensures all three built-in default shaders' pipelines exist for an output format.</summary>
    /// <param name="outputFormat">The target's sRGB output color format.</param>
    /// <exception cref="Bork2dException">A default pipeline could not be created.</exception>
    internal void EnsureDefaultPipelines(Format outputFormat)
    {
        EnsurePipeline(
            DefaultEntryOrThrow(ShaderPipelineKind.Sprite),
            ShaderPipelineKind.Sprite,
            outputFormat,
            fatal: true);
        EnsurePipeline(
            DefaultEntryOrThrow(ShaderPipelineKind.Line),
            ShaderPipelineKind.Line,
            outputFormat,
            fatal: true);
        EnsurePipeline(
            DefaultEntryOrThrow(ShaderPipelineKind.Font),
            ShaderPipelineKind.Font,
            outputFormat,
            fatal: true);
    }

    /// <summary>
    /// Advances the catalog one frame: observes a catalog mount, promotes completed loads, and
    /// retires modules and pipelines past the safety age.
    /// </summary>
    /// <remarks>
    /// The renderer calls this once per consumed snapshot before planning. Loads completed during a
    /// frame are promoted here on the next frame, so a frame's bindings and runs are deterministic.
    /// </remarks>
    internal void Pump()
    {
        if (_disposed != 0)
        {
            return;
        }

        _frameSequence++;
        ObserveCatalogVersion();
        CompleteLoadingEntries();
        CompleteDefaultReload(ShaderPipelineKind.Sprite);
        CompleteDefaultReload(ShaderPipelineKind.Line);
        CompleteDefaultReload(ShaderPipelineKind.Font);
        RetireUnreferencedModules();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // The loader thread may fault a still-pending load when the asset service stops; observe any
        // current fault so it never surfaces as an unobserved task exception, matching the texture
        // page store's teardown rule.
        foreach (StoreEntry entry in _entries.Values)
        {
            if (entry.Load is { IsFaulted: true } load)
            {
                _ = load.Exception;
            }
        }

        for (var kindIndex = 0; kindIndex < _defaultReloads.Length; kindIndex++)
        {
            DefaultReload? reload = _defaultReloads[kindIndex];
            if (reload is { AssetLoad.IsFaulted: true } assetReload)
            {
                _ = assetReload.AssetLoad.Exception;
            }

            if (reload is { SpirVLoad.IsFaulted: true } spirVReload)
            {
                _ = spirVReload.SpirVLoad.Exception;
            }
        }

        foreach (ModuleRecord module in _modulesByHash.Values)
        {
            _modules.DestroyPipelines(ShaderPipelineKind.Sprite, module.ContentHash);
            _modules.DestroyPipelines(ShaderPipelineKind.Line, module.ContentHash);
            _modules.DestroyPipelines(ShaderPipelineKind.Font, module.ContentHash);
            _modules.DestroyModule(module.Kind, module.Module);
        }

        _modulesByHash.Clear();
        _unreferencedModules.Clear();
        _entries.Clear();
        _entriesById.Clear();
        _globalNames.Clear();
        Array.Clear(_defaultReloads);
        Array.Clear(_defaultEntries);
    }

    private static StoreEntry CreateDefaultEntry(
        ShaderStore store,
        IAssets assets,
        ShaderPipelineKind kind,
        int id,
        string shaderPath,
        string description)
    {
        ShaderRenderData metadata = LoadDefaultShader(assets, shaderPath, description);
        string spirVPath = metadata.SpirV.Path
            ?? throw new Bork2dException(
                $"{char.ToUpperInvariant(description[0])}{description[1..]} '{shaderPath}' has no "
                    + "SPIR-V data reference.");
        if (!ShaderValidation.TryValidateMetadata(metadata, out string metadataError))
        {
            throw new Bork2dException(
                $"{char.ToUpperInvariant(description[0])}{description[1..]} '{shaderPath}' is not "
                    + $"usable: it {metadataError}.");
        }

        byte[] spirV = LoadSpirV(
            assets,
            spirVPath,
            $"the built-in {Describe(kind)} fragment module referenced by '{shaderPath}'");
        var entry = new StoreEntry(id, shaderPath, metadata, isDefault: true);
        entry.Module = store.GetOrCreateModule(spirV, spirVPath, kind);
        entry.State = EntryState.Ready;
        return entry;
    }

    private static string Describe(ShaderPipelineKind kind)
        => kind switch
        {
            ShaderPipelineKind.Sprite => "sprite",
            ShaderPipelineKind.Line => "line",
            _ => "font",
        };

    private static string ShaderPathFor(ShaderPipelineKind kind)
        => kind switch
        {
            ShaderPipelineKind.Sprite => DefaultShaderPath,
            ShaderPipelineKind.Line => LineShaderPath,
            _ => SdfShaderPath,
        };

    private StoreEntry FindEntry(int entryId)
    {
        if (entryId == DefaultEntryId)
        {
            return DefaultEntryOrThrow(ShaderPipelineKind.Sprite);
        }

        if (entryId == LineDefaultEntryId)
        {
            return DefaultEntryOrThrow(ShaderPipelineKind.Line);
        }

        if (entryId == FontDefaultEntryId)
        {
            return DefaultEntryOrThrow(ShaderPipelineKind.Font);
        }

        return _entriesById.TryGetValue(entryId, out StoreEntry? found)
            ? found
            : throw new Bork2dException(
                $"Shader entry {entryId} is not in the current asset catalog.");
    }

    private ShaderBinding ResolveDefault(ShaderPipelineKind kind, Format outputFormat)
    {
        StoreEntry entry = DefaultEntryOrThrow(kind);
        EnsurePipeline(entry, kind, outputFormat, fatal: true);
        MarkUsed(entry);
        return new ShaderBinding(GetDefaultEntryId(kind), ShaderBindingState.Ready);
    }

    private ShaderBinding ResolveCustom(
        StoreEntry entry,
        ShaderPipelineKind kind,
        Format outputFormat)
    {
        switch (entry.State)
        {
            case EntryState.Loading:
                entry.LastRequestedKind = kind;
                return new ShaderBinding(entry.Id, ShaderBindingState.Pending);

            case EntryState.Failed:
                return new ShaderBinding(GetDefaultEntryId(kind), ShaderBindingState.Failed);

            default:
                entry.LastRequestedKind = kind;
                if (!EnsurePipeline(entry, kind, outputFormat, fatal: false))
                {
                    return new ShaderBinding(GetDefaultEntryId(kind), ShaderBindingState.Failed);
                }

                MarkUsed(entry);
                return new ShaderBinding(entry.Id, ShaderBindingState.Ready);
        }
    }

    private StoreEntry CreateCustomEntry(string path, ShaderRenderData metadata)
    {
        var entry = new StoreEntry(_nextEntryId++, path, metadata, isDefault: false);
        if (!ShaderValidation.TryValidateMetadata(metadata, out string error))
        {
            entry.State = EntryState.Failed;
            WarnOnce(
                path,
                $"Shader '{path}' is not usable and its renderables draw with the kind's default "
                    + $"shader: it {error}.");
            return entry;
        }

        try
        {
            entry.Load = StartLoad(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            entry.State = EntryState.Failed;
            WarnOnce(
                path,
                $"Shader '{path}' could not start loading and its renderables draw with the kind's "
                    + $"default shader: {exception.Message}.");
        }

        return entry;
    }

    private Task<byte[]> StartLoad(string path)
    {
        Task<byte[]> load = _assets.LoadData(path);
        ObserveFault(load);
        return load;
    }

    private bool EnsurePipeline(
        StoreEntry entry,
        ShaderPipelineKind kind,
        Format outputFormat,
        bool fatal)
    {
        if (entry.Module is not { } module)
        {
            entry.State = EntryState.Failed;
            return false;
        }

        if (_modules.TryGetPipeline(kind, module.ContentHash, outputFormat, out _))
        {
            return true;
        }

        try
        {
            _ = _modules.GetOrCreatePipeline(
                kind,
                outputFormat,
                module.ContentHash,
                module.Module);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (fatal)
            {
                throw new Bork2dException(
                    $"The built-in {Describe(kind)} shader pipeline could not be created for output "
                        + $"format {outputFormat}: {exception.Message}",
                    exception);
            }

            WarnOnce(
                entry.Entry.Path,
                $"Shader '{entry.Entry.Path}' could not create its {Describe(kind)} pipeline for "
                    + $"output format {outputFormat} and its renderables draw with the kind's default "
                    + $"shader: {exception.Message}.");
            entry.State = EntryState.Failed;
            UnregisterGlobalNames(entry);
            ReleaseModule(entry);
            return false;
        }
    }

    private void CompleteLoadingEntries()
    {
        foreach (StoreEntry entry in _entries.Values)
        {
            if (entry.State == EntryState.Loading && entry.Load is { IsCompleted: true } load)
            {
                CompleteCustomEntry(entry, load);
            }
        }
    }

    private void CompleteCustomEntry(StoreEntry entry, Task<byte[]> load)
    {
        if (load.IsFaulted || load.IsCanceled)
        {
            _ = load.Exception;
            WarnOnce(
                entry.Entry.Path,
                $"Shader '{entry.Entry.Path}' failed to load and its renderables draw with the "
                    + $"kind's default shader: {DescribeLoadFailure(load)}.");
            entry.State = EntryState.Failed;
            return;
        }

        byte[] spirV = load.Result;
        if (!ShaderValidation.TryValidateSpirV(spirV, out string error))
        {
            WarnOnce(
                entry.Entry.Path,
                $"Shader '{entry.Entry.Path}' is not usable and its renderables draw with the kind's "
                    + $"default shader: it {error}.");
            entry.State = EntryState.Failed;
            return;
        }

        if (!TryValidateGlobalNames(entry.Entry.Metadata, ignoreOwner: null, out string nameError))
        {
            WarnOnce(
                entry.Entry.Path,
                $"Shader '{entry.Entry.Path}' is not usable and its renderables draw with the kind's "
                    + $"default shader: {nameError}.");
            entry.State = EntryState.Failed;
            return;
        }

        try
        {
            entry.Module = GetOrCreateModule(spirV, entry.Entry.Path, entry.LastRequestedKind);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WarnOnce(
                entry.Entry.Path,
                $"Shader '{entry.Entry.Path}' could not create its module and its renderables draw "
                    + $"with the kind's default shader: {exception.Message}.");
            entry.State = EntryState.Failed;
            return;
        }

        AddGlobalNames(entry);
        entry.State = EntryState.Ready;
    }

    private void CompleteDefaultReload(ShaderPipelineKind kind)
    {
        DefaultReload? reload = _defaultReloads[(int)kind];
        if (reload is null)
        {
            return;
        }

        if (reload.SpirVLoad is null)
        {
            if (!reload.AssetLoad.IsCompleted)
            {
                return;
            }

            if (reload.AssetLoad.IsFaulted || reload.AssetLoad.IsCanceled)
            {
                _ = reload.AssetLoad.Exception;
                _defaultReloads[(int)kind] = null;
                WarnDefaultReload(kind, DescribeLoadFailure(reload.AssetLoad));
                return;
            }

            ShaderAsset asset = reload.AssetLoad.Result;
            if (!asset.SpirV.HasValue)
            {
                _defaultReloads[(int)kind] = null;
                WarnDefaultReload(kind, "the replacement has no SPIR-V data reference");
                return;
            }

            try
            {
                Task<byte[]> load = _assets.LoadData(asset.SpirV.Path!);
                ObserveFault(load);
                reload.SpirVLoad = load;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _defaultReloads[(int)kind] = null;
                WarnDefaultReload(
                    kind,
                    $"the replacement SPIR-V could not start loading: {exception.Message}");
                return;
            }
        }

        Task<byte[]> spirVLoad = reload.SpirVLoad;
        if (!spirVLoad.IsCompleted)
        {
            return;
        }

        _defaultReloads[(int)kind] = null;
        if (spirVLoad.IsFaulted || spirVLoad.IsCanceled)
        {
            _ = spirVLoad.Exception;
            WarnDefaultReload(kind, DescribeLoadFailure(spirVLoad));
            return;
        }

        ShaderAsset replacement = reload.AssetLoad.Result;
        byte[] spirV = spirVLoad.Result;
        ShaderRenderData metadata = ShaderRenderData.Freeze(replacement);
        if (!ShaderValidation.TryValidateMetadata(metadata, out string error))
        {
            WarnDefaultReload(kind, $"it {error}");
            return;
        }

        if (!ShaderValidation.TryValidateSpirV(spirV, out error))
        {
            WarnDefaultReload(kind, $"it {error}");
            return;
        }

        StoreEntry? previous = _defaultEntries[(int)kind];
        if (!TryValidateGlobalNames(metadata, previous, out error))
        {
            WarnDefaultReload(kind, error);
            return;
        }

        ModuleRecord module;
        try
        {
            module = GetOrCreateModule(spirV, replacement.SpirV.Path!, kind);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WarnDefaultReload(kind, $"its module could not be created: {exception.Message}");
            return;
        }

        var entry = new StoreEntry(
            GetDefaultEntryId(kind),
            ShaderPathFor(kind),
            metadata,
            isDefault: true)
        {
            Module = module,
            State = EntryState.Ready,
        };

        // The replacement is complete and validated: swap it in, then let the previous entry's
        // module and pipelines retire after the safety age.
        if (previous is not null)
        {
            UnregisterGlobalNames(previous);
        }

        AddGlobalNames(entry);
        if (previous is not null)
        {
            ReleaseModule(previous);
        }

        _defaultEntries[(int)kind] = entry;
    }

    private void StartDefaultReload(ShaderPipelineKind kind)
    {
        DefaultReload? current = _defaultReloads[(int)kind];
        if (current is { AssetLoad.IsFaulted: true } faultedAsset)
        {
            _ = faultedAsset.AssetLoad.Exception;
        }

        if (current is { SpirVLoad.IsFaulted: true } faultedSpirV)
        {
            _ = faultedSpirV.SpirVLoad.Exception;
        }

        try
        {
            Task<ShaderAsset> assetLoad = _assets.LoadAsset<ShaderAsset>(ShaderPathFor(kind));
            ObserveFault(assetLoad);
            _defaultReloads[(int)kind] = new DefaultReload(assetLoad);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _defaultReloads[(int)kind] = null;
            WarnDefaultReload(
                kind,
                $"the replacement asset could not start loading: {exception.Message}");
        }
    }

    private void ObserveCatalogVersion()
    {
        if (_catalogVersion is null)
        {
            return;
        }

        long version = _catalogVersion.CatalogVersion;
        if (version == _catalogVersionSeen)
        {
            return;
        }

        _catalogVersionSeen = version;
        RetireAllCustomEntries();
        _globalNames.Clear();
        for (var kindIndex = 0; kindIndex < _defaultEntries.Length; kindIndex++)
        {
            if (_defaultEntries[kindIndex] is { } entry)
            {
                AddGlobalNames(entry);
            }
        }

        StartDefaultReload(ShaderPipelineKind.Sprite);
        StartDefaultReload(ShaderPipelineKind.Line);
        StartDefaultReload(ShaderPipelineKind.Font);
    }

    private void RetireAllCustomEntries()
    {
        foreach (StoreEntry entry in _entries.Values)
        {
            if (entry.State == EntryState.Loading && entry.Load is { IsFaulted: true } load)
            {
                _ = load.Exception;
            }

            UnregisterGlobalNames(entry);
            ReleaseModule(entry);
        }

        _entries.Clear();
        _entriesById.Clear();
    }

    private void MarkUsed(StoreEntry entry)
    {
        entry.LastUsedFrame = _frameSequence;
        if (entry.Module is { } module)
        {
            module.LastUsedFrame = _frameSequence;
        }
    }

    private ModuleRecord GetOrCreateModule(
        byte[] spirV,
        string name,
        ShaderPipelineKind kind)
    {
        ulong contentHash = XxHash64.HashToUInt64(spirV);
        if (_modulesByHash.TryGetValue(contentHash, out ModuleRecord? existing))
        {
            existing.ReferenceCount++;
            if (existing.Unreferenced)
            {
                existing.Unreferenced = false;
                _ = _unreferencedModules.Remove(existing);
            }

            existing.LastUsedFrame = _frameSequence;
            return existing;
        }

        ShaderModule module = _modules.CreateModule(kind, spirV, name);
        var record = new ModuleRecord(contentHash, kind, module)
        {
            ReferenceCount = 1,
            LastUsedFrame = _frameSequence,
        };
        _modulesByHash.Add(contentHash, record);
        return record;
    }

    private void ReleaseModule(StoreEntry entry)
    {
        if (entry.Module is not { } module)
        {
            return;
        }

        entry.Module = null;
        module.ReferenceCount = Math.Max(module.ReferenceCount - 1, 0);
        if (entry.LastUsedFrame > module.LastUsedFrame)
        {
            module.LastUsedFrame = entry.LastUsedFrame;
        }

        if (module.ReferenceCount == 0 && !module.Unreferenced)
        {
            module.Unreferenced = true;
            _unreferencedModules.Add(module);
        }
    }

    private void RetireUnreferencedModules()
    {
        for (int index = _unreferencedModules.Count - 1; index >= 0; index--)
        {
            ModuleRecord module = _unreferencedModules[index];
            if (module.ReferenceCount > 0)
            {
                _unreferencedModules.RemoveAt(index);
                continue;
            }

            if (_frameSequence - module.LastUsedFrame <= SafetyAgeFrames)
            {
                continue;
            }

            _unreferencedModules.RemoveAt(index);
            _ = _modulesByHash.Remove(module.ContentHash);
            _modules.DestroyPipelines(ShaderPipelineKind.Sprite, module.ContentHash);
            _modules.DestroyPipelines(ShaderPipelineKind.Line, module.ContentHash);
            _modules.DestroyPipelines(ShaderPipelineKind.Font, module.ContentHash);
            _modules.DestroyModule(module.Kind, module.Module);
        }
    }

    private bool TryValidateGlobalNames(
        ShaderRenderData metadata,
        StoreEntry? ignoreOwner,
        out string error)
    {
        foreach (ShaderUniform declaration in metadata.GlobalUniforms)
        {
            if (!_globalNames.TryGetValue(declaration.Name, out GlobalNameClaim? claim)
                || claim.Type == declaration.Type)
            {
                continue;
            }

            // The name is claimed with a different type. The ignored owner is the version being
            // replaced, so only another live owner makes this declaration a conflict.
            StoreEntry? conflicting = null;
            foreach (StoreEntry owner in claim.Owners)
            {
                if (!ReferenceEquals(owner, ignoreOwner))
                {
                    conflicting = owner;
                    break;
                }
            }

            if (conflicting is null)
            {
                continue;
            }

            error = $"its global uniform '{declaration.Name}' is declared as {declaration.Type}, "
                + $"but the engine-wide name is already registered as {claim.Type} by shader "
                + $"'{conflicting.Entry.Path}'";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void AddGlobalNames(StoreEntry entry)
    {
        foreach (ShaderUniform declaration in entry.Entry.Metadata.GlobalUniforms)
        {
            if (!_globalNames.TryGetValue(declaration.Name, out GlobalNameClaim? claim))
            {
                claim = new GlobalNameClaim(declaration.Type);
                _globalNames.Add(declaration.Name, claim);
            }

            // A name may be shared by several live shaders of the same type; every owner is tracked
            // so retiring one never drops the name while another still declares it.
            if (!claim.Owners.Contains(entry))
            {
                claim.Owners.Add(entry);
            }
        }
    }

    private void UnregisterGlobalNames(StoreEntry entry)
    {
        foreach (ShaderUniform declaration in entry.Entry.Metadata.GlobalUniforms)
        {
            if (!_globalNames.TryGetValue(declaration.Name, out GlobalNameClaim? claim))
            {
                continue;
            }

            _ = claim.Owners.Remove(entry);
            if (claim.Owners.Count == 0)
            {
                _ = _globalNames.Remove(declaration.Name);
            }
        }
    }

    private void WarnOnce(string path, string message)
    {
        if (!_warnedPaths.Add(path))
        {
            return;
        }

        _logger.LogWarning(
            "{Message} This diagnostic is logged once per shader path.",
            message);
    }

    private void WarnDefaultReload(ShaderPipelineKind kind, string reason)
    {
        WarnOnce(
            ShaderPathFor(kind),
            $"Reloading the built-in {Describe(kind)} shader failed and the previous shader remains "
                + $"active: {reason}.");
    }

    private void WarnMissingSpirV(ShaderPipelineKind kind)
    {
        uint bit = 1u << (int)kind;
        if ((_loggedMissingSpirV & bit) != 0)
        {
            return;
        }

        _loggedMissingSpirV |= bit;
        _logger.LogWarning(
            "A {Kind} assigns a custom shader with no SPIR-V data reference; its renderables draw "
                + "with the kind's default shader. This diagnostic is logged once per renderer.",
            Describe(kind));
    }

    private StoreEntry DefaultEntryOrThrow(ShaderPipelineKind kind)
        => _defaultEntries[(int)kind]
            ?? throw new Bork2dException(
                $"The built-in {Describe(kind)} shader catalog is not initialized.");

    private static string DescribeLoadFailure(Task load)
    {
        Exception? exception = load.Exception?.InnerException ?? load.Exception;
        if (load.IsCanceled)
        {
            return "the load was canceled";
        }

        return exception?.Message ?? "the load faulted";
    }

    /// <summary>
    /// Observes a load's eventual fault so a task retired or disposed before completion cannot
    /// surface as an unobserved task exception.
    /// </summary>
    /// <remarks>
    /// The continuation is best-effort and runs only for a faulted antecedent; it keeps the
    /// observation independent of whether the catalog ever pumps again (shutdown, a mount that
    /// retires the entry, or a renderable that never returns).
    /// </remarks>
    /// <param name="load">The load task to observe.</param>
    private static void ObserveFault(Task load)
    {
        _ = load.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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

    private enum EntryState
    {
        Loading,
        Ready,
        Failed,
    }

    private sealed class StoreEntry
    {
        internal StoreEntry(int id, string path, ShaderRenderData metadata, bool isDefault)
        {
            Id = id;
            Entry = new ShaderEntry(id, path, metadata, isDefault);
        }

        internal int Id { get; }

        internal ShaderEntry Entry { get; }

        internal EntryState State { get; set; } = EntryState.Loading;

        internal Task<byte[]>? Load { get; set; }

        internal ModuleRecord? Module { get; set; }

        internal long LastUsedFrame { get; set; }

        /// <summary>Gets or sets the vertex-stage kind of the most recent resolution.</summary>
        internal ShaderPipelineKind LastRequestedKind { get; set; }
    }

    private sealed class ModuleRecord
    {
        internal ModuleRecord(ulong contentHash, ShaderPipelineKind kind, ShaderModule module)
        {
            ContentHash = contentHash;
            Kind = kind;
            Module = module;
        }

        internal ulong ContentHash { get; }

        internal ShaderPipelineKind Kind { get; }

        internal ShaderModule Module { get; }

        internal int ReferenceCount { get; set; }

        internal long LastUsedFrame { get; set; }

        internal bool Unreferenced { get; set; }
    }

    private sealed class DefaultReload
    {
        internal DefaultReload(Task<ShaderAsset> assetLoad) => AssetLoad = assetLoad;

        internal Task<ShaderAsset> AssetLoad { get; }

        internal Task<byte[]>? SpirVLoad { get; set; }
    }

    private sealed class GlobalNameClaim
    {
        internal GlobalNameClaim(ShaderUniformType type) => Type = type;

        internal ShaderUniformType Type { get; }

        internal List<StoreEntry> Owners { get; } = [];
    }
}
