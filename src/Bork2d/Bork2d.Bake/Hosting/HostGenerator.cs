using System.Text;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Incremental;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// Generates the reflection-free bake host: <c>BakeHost.csproj</c> plus <c>BakeManifest.g.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// The generator parses the descriptor, scans the sources for their <c>[bork2d].type</c>
/// declarations (D-M4-13), resolves every distinct type from <c>Project.Common</c> metadata (never
/// loading the game assemblies into the tooling process), validates the shape and visibility
/// contract, then writes the host project and manifest. A source TOML syntax or metadata failure
/// fails generation before any host file is written. It reports diagnostics and returns
/// <see langword="null"/> rather than throwing for expected failures.
/// </para>
/// <para>
/// Files are only rewritten when their content changes (or <c>--force</c> is passed), so an unchanged
/// descriptor, <c>Project.Common</c>, and source-type set leave timestamps and downstream incremental
/// builds alone. The generator additionally records its inputs and the content hash of both generated
/// files in <c>obj/bork2d/bake/host-generation.json</c>. When that state, the descriptor, the
/// <c>Project.Common</c> assembly, the discovered source-type declarations, the generator version,
/// the configuration, and the output directory are unchanged and both files still match the recorded
/// content, generation returns <see cref="HostGenerationResult.Changed"/> false without reading
/// <c>Project.Common</c> metadata.
/// </para>
/// </remarks>
internal static class HostGenerator
{
    private static readonly string[] _requiredToolingAssemblies =
    [
        "Bork2d.Api",
        "Bork2d.Assets",
        "Bork2d.Bake",
        "Nerdbank.MessagePack",
        "PolyType",
        "System.IO.Hashing",
        "Tomlyn",
    ];

    /// <summary>Generates the bake host for a project descriptor.</summary>
    /// <param name="options">The generation inputs.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The generation result, or <see langword="null"/> when a diagnostic failed it.</returns>
    internal static HostGenerationResult? Generate(
        HostGenerationOptions options,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(options.ProjectPath, diagnostics);
        if (descriptor is null || diagnostics.HasErrors)
        {
            return null;
        }

        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            descriptor,
            diagnostics,
            validateSidecarSyntax: true);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        string sourceTypesHash = HashSourceTypes(sources);

        string commonPath;
        try
        {
            commonPath = Path.GetFullPath(options.CommonAssemblyPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostReferenceMissing,
                $"Invalid Project.Common path '{options.CommonAssemblyPath}': {exception.Message}",
                options.CommonAssemblyPath);
            return null;
        }

        string directory = Path.GetFullPath(options.OutputDirectory);
        string projectPath = Path.Combine(directory, GeneratedBakeHostProjectWriter.FileName);
        string manifestPath = Path.Combine(directory, GeneratedBakeManifestWriter.FileName);
        string statePath = BakeManifestPaths.HostGenerationStatePath(descriptor.DescriptorDirectory);
        string? descriptorHash = TryHashFile(descriptor.DescriptorPath);
        string? commonHash = TryHashFile(commonPath);

        if (!options.Force
            && descriptorHash is not null
            && commonHash is not null
            && CanSkipGeneration(
                options,
                descriptorHash,
                commonHash,
                sourceTypesHash,
                directory,
                projectPath,
                manifestPath,
                statePath))
        {
            return new HostGenerationResult(
                changed: false,
                projectPath,
                manifestPath,
                types: [],
                references: []);
        }

        if (!BakeTypeResolver.TryCreate(commonPath, diagnostics, out BakeTypeResolver? resolver))
        {
            return null;
        }

        using (resolver)
        {
            var mapped = new List<BakeMappedType>();
            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (BakeSource source in sources)
            {
                if (!seenTypes.Add(source.TypeName))
                {
                    continue;
                }

                string diagnosticPath = BakeDiagnosticPath.ForSource(
                    descriptor.DescriptorDirectory,
                    source.FullPath);

                if (!resolver.TryResolve(
                    source.TypeName,
                    diagnosticPath,
                    diagnostics,
                    out Type? type,
                    out string? assemblyPath,
                    source.TypeLine,
                    source.TypeColumn))
                {
                    continue;
                }

                if (!BakeTypeResolver.TryGetCSharpName(
                    type!,
                    diagnostics,
                    diagnosticPath,
                    out string csharpName,
                    source.TypeLine,
                    source.TypeColumn))
                {
                    continue;
                }

                mapped.Add(new BakeMappedType(
                    source.TypeName,
                    csharpName,
                    assemblyPath!,
                    BakeAssetKind.MessagePack));
            }

            if (mapped.Count != seenTypes.Count)
            {
                return null;
            }

            BakeHostReferenceSet references = CollectReferences(
                resolver,
                mapped,
                commonPath,
                diagnostics);

            if (diagnostics.HasErrors)
            {
                return null;
            }

            string manifestSource = GeneratedBakeManifestWriter.Write(mapped);
            string projectSource = GeneratedBakeHostProjectWriter.Write(
                references.References,
                options.Configuration);

            bool created;
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostWriteFailed,
                    $"Failed to create the host output directory '{directory}': {exception.Message}",
                    directory);
                return null;
            }

            bool projectChanged = WriteIfChanged(
                projectPath,
                projectSource,
                options.Force,
                diagnostics);
            bool manifestChanged = WriteIfChanged(
                manifestPath,
                manifestSource,
                options.Force,
                diagnostics);
            created = projectChanged || manifestChanged;

            if (diagnostics.HasErrors)
            {
                return null;
            }

            if (descriptorHash is not null && commonHash is not null)
            {
                WriteGenerationState(
                    statePath,
                    descriptorHash,
                    commonHash,
                    sourceTypesHash,
                    options.Configuration,
                    directory,
                    projectPath,
                    manifestPath,
                    projectSource,
                    manifestSource,
                    diagnostics);
            }

            return new HostGenerationResult(
                created,
                projectPath,
                manifestPath,
                mapped,
                references.References);
        }
    }

    private static bool CanSkipGeneration(
        HostGenerationOptions options,
        string descriptorHash,
        string commonAssemblyHash,
        string sourceTypesHash,
        string directory,
        string projectPath,
        string manifestPath,
        string statePath)
    {
        BakeHostGenerationState? state = BakeHostGenerationStore.TryRead(statePath);
        if (state is null
            || state.Version != BakeHostGenerationState.CurrentVersion
            || !string.Equals(
                state.GeneratorVersion,
                BakeToolInfo.Version,
                StringComparison.Ordinal)
            || !string.Equals(state.DescriptorHash, descriptorHash, StringComparison.Ordinal)
            || !string.Equals(
                state.CommonAssemblyHash,
                commonAssemblyHash,
                StringComparison.Ordinal)
            || !string.Equals(state.SourceTypesHash, sourceTypesHash, StringComparison.Ordinal)
            || !string.Equals(
                state.Configuration,
                options.Configuration,
                StringComparison.Ordinal)
            || !PathEquals(state.OutputDirectory, directory)
            || !PathEquals(state.ProjectPath, projectPath)
            || !PathEquals(state.ManifestPath, manifestPath)
            || !File.Exists(projectPath)
            || !File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            string generatedHash = BakeHash.HashFields(
                File.ReadAllBytes(projectPath),
                File.ReadAllBytes(manifestPath));
            return string.Equals(state.GeneratedHash, generatedHash, StringComparison.Ordinal);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return false;
        }
    }

    private static void WriteGenerationState(
        string statePath,
        string descriptorHash,
        string commonAssemblyHash,
        string sourceTypesHash,
        string configuration,
        string directory,
        string projectPath,
        string manifestPath,
        string projectSource,
        string manifestSource,
        BakeDiagnostics diagnostics)
    {
        string generatedHash = BakeHash.HashFields(
            Encoding.UTF8.GetBytes(projectSource),
            Encoding.UTF8.GetBytes(manifestSource));

        var state = new BakeHostGenerationState(
            BakeHostGenerationState.CurrentVersion,
            BakeToolInfo.Version,
            descriptorHash,
            commonAssemblyHash,
            sourceTypesHash,
            configuration,
            directory,
            projectPath,
            manifestPath,
            generatedHash);

        BakeHostGenerationStore.TryWrite(statePath, state, diagnostics);
    }

    private static string HashSourceTypes(IReadOnlyList<BakeSource> sources)
    {
        // The pack identity and root are part of the declaration identity: moving a source between
        // packs, adding a DLC root, or renaming/removing one must regenerate the host even when the
        // asset paths and type names look unchanged (D-M4-14).
        string[] declarations = new string[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            BakeSource source = sources[i];
            declarations[i] = $"{source.Pack.PackPath}={source.Pack.RelativeRoot}="
                + $"{source.RelativePath}={source.TypeName}";
        }

        return BakeHash.HashTextFields(declarations);
    }

    private static string? TryHashFile(string path)
    {
        try
        {
            return File.Exists(path) ? BakeHash.HashFile(path) : null;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return null;
        }
    }

    private static bool PathEquals(string left, string right)
        => string.Equals(
            left,
            right,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static BakeHostReferenceSet CollectReferences(
        BakeTypeResolver resolver,
        IReadOnlyList<BakeMappedType> mapped,
        string commonPath,
        BakeDiagnostics diagnostics)
    {
        var references = new BakeHostReferenceSet();

        foreach (BakeHostReference reference in resolver.ClosureAssemblies)
        {
            references.TryAdd(reference.AssemblyName, reference.Path);
        }

        references.TryAdd(resolver.CommonAssemblyName, commonPath);

        string commonDirectory = Path.GetDirectoryName(commonPath) ?? AppContext.BaseDirectory;
        string toolingDirectory = AppContext.BaseDirectory;
        string[] directories = [commonDirectory, toolingDirectory];

        foreach (string name in _requiredToolingAssemblies)
        {
            if (references.Contains(name))
            {
                continue;
            }

            string? path = FindAssembly(name, directories);
            if (path is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostReferenceMissing,
                    $"The tooling could not find '{name}.dll'. It must be copied next to the bake "
                        + "tooling or next to Project.Common.",
                    commonPath);
                continue;
            }

            references.TryAdd(name, path);
        }

        foreach (BakeMappedType mapping in mapped)
        {
            string name = Path.GetFileNameWithoutExtension(mapping.AssemblyPath);
            if (name.Length > 0 && File.Exists(mapping.AssemblyPath))
            {
                references.TryAdd(name, mapping.AssemblyPath);
            }
        }

        return references;
    }

    private static string? FindAssembly(string name, IReadOnlyList<string> directories)
    {
        foreach (string directory in directories)
        {
            string candidate = Path.Combine(directory, name + ".dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool WriteIfChanged(
        string path,
        string content,
        bool force,
        BakeDiagnostics diagnostics)
    {
        try
        {
            if (!force && File.Exists(path))
            {
                string existing = File.ReadAllText(path, Encoding.UTF8);
                if (string.Equals(existing, content, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostWriteFailed,
                $"Failed to write the generated host file '{path}': {exception.Message}",
                path);
            return false;
        }
    }

    private static bool IsIoFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
}
