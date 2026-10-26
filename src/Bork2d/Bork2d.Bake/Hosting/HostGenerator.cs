using System.Text;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// Generates the reflection-free bake host: <c>BakeHost.csproj</c> plus <c>BakeManifest.g.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// The generator parses the descriptor, resolves every mapped type from <c>Project.Common</c>
/// metadata (never loading the game assemblies into the tooling process), validates the shape and
/// visibility contract, then writes the host project and manifest. It reports diagnostics and
/// returns <see langword="null"/> rather than throwing for expected failures.
/// </para>
/// <para>
/// Files are only rewritten when their content changes (or <c>--force</c> is passed), so an unchanged
/// descriptor and <c>Project.Common</c> leave timestamps and downstream incremental builds alone.
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

        if (!BakeTypeResolver.TryCreate(commonPath, diagnostics, out BakeTypeResolver? resolver))
        {
            return null;
        }

        using (resolver)
        {
            var mapped = new List<BakeMappedType>(descriptor.Types.Count);
            foreach (ProjectTypeMapping mapping in descriptor.Types)
            {
                if (!resolver.TryResolve(
                    mapping.TypeName,
                    descriptor.DescriptorPath,
                    diagnostics,
                    out Type? type,
                    out string? assemblyPath,
                    mapping.TypeLine,
                    mapping.TypeColumn))
                {
                    continue;
                }

                if (!BakeTypeResolver.TryGetCSharpName(
                    type!,
                    diagnostics,
                    descriptor.DescriptorPath,
                    out string csharpName,
                    mapping.TypeLine,
                    mapping.TypeColumn))
                {
                    continue;
                }

                mapped.Add(new BakeMappedType(
                    mapping.TypeName,
                    mapping.Prefix,
                    csharpName,
                    assemblyPath!,
                    BakeAssetKind.MessagePack));
            }

            if (mapped.Count != descriptor.Types.Count)
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

            string directory = Path.GetFullPath(options.OutputDirectory);
            string projectPath = Path.Combine(directory, GeneratedBakeHostProjectWriter.FileName);
            string manifestPath = Path.Combine(directory, GeneratedBakeManifestWriter.FileName);

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

            return new HostGenerationResult(
                created,
                projectPath,
                manifestPath,
                mapped,
                references.References);
        }
    }

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
