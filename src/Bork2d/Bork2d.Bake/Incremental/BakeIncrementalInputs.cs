using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// The current tracked inputs and output paths of one invocation, used by the incremental planner to
/// decide whether the existing manifest and packs are current.
/// </summary>
/// <remarks>
/// <para>
/// Tracked inputs are every regular file under every pack root whose name ends in <c>.toml</c>
/// (case-insensitive) or whose suffix matches the project's immutable processor registry (longest
/// compound suffix). Reparse points are skipped by the shared source enumeration, exactly as
/// discovery skips them. Inputs are sorted ordinal by their project-relative display path, so the
/// manifest is stable across enumeration order.
/// </para>
/// <para>
/// Collection is read-only. A missing root, an unreadable file, or an unresolvable output root
/// returns <see langword="null"/>; the planner then runs a full bake, which reports the concrete
/// configuration/source diagnostic.
/// </para>
/// </remarks>
internal sealed class BakeIncrementalInputs
{
    private const string TomlSuffix = ".toml";

    private BakeIncrementalInputs(
        string configurationHash,
        string shapeClosureHash,
        string mode,
        IReadOnlyList<BakeInputHash> inputs,
        IReadOnlyList<string> outputs)
    {
        ConfigurationHash = configurationHash;
        ShapeClosureHash = shapeClosureHash;
        Mode = mode;
        Inputs = [.. inputs];
        Outputs = [.. outputs];
    }

    /// <summary>Gets the builder-configuration hash.</summary>
    internal string ConfigurationHash { get; }

    /// <summary>Gets the registered type and processor assembly closure hash.</summary>
    internal string ShapeClosureHash { get; }

    /// <summary>Gets the bake mode name.</summary>
    internal string Mode { get; }

    /// <summary>Gets the tracked inputs in ordinal display-path order.</summary>
    internal IReadOnlyList<BakeInputHash> Inputs { get; }

    /// <summary>Gets the absolute pack output paths in configured pack order.</summary>
    internal IReadOnlyList<string> Outputs { get; }

    /// <summary>Collects the tracked inputs and output paths for one invocation.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="options">The resolved invocation inputs.</param>
    /// <returns>The collected inputs, or <see langword="null"/> when a path cannot be resolved or read.</returns>
    internal static BakeIncrementalInputs? TryCollect(BakeProject project, BakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var probe = new BakeDiagnostics();
        if (!BakeOutputLayout.TryResolve(
            project,
            options.OutputRoot,
            probe,
            out _,
            out IReadOnlyList<BakePackOutput> outputs))
        {
            return null;
        }

        var inputs = new List<BakeInputHash>();
        foreach (BakePack pack in project.Packs)
        {
            foreach (string file in BakeSourcePath.EnumerateFilesSorted(pack.Root))
            {
                if (!BakeSourcePath.TryMakeRelative(pack.Root, file, out string relativePath, out _))
                {
                    continue;
                }

                if (!IsTracked(project, file, relativePath))
                {
                    continue;
                }

                string hash;
                try
                {
                    hash = BakeHash.HashFile(file);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
                {
                    return null;
                }

                inputs.Add(new BakeInputHash(
                    BakeDiagnosticPath.ForSource(project.ProjectRoot, file),
                    hash));
            }
        }

        inputs.Sort(static (left, right) =>
            string.CompareOrdinal(left.Path, right.Path));

        return new BakeIncrementalInputs(
            BakeConfigurationHash.Compute(project),
            BakeAssemblyClosure.ComputeHash(project),
            options.Mode.ToName(),
            inputs,
            [.. outputs.Select(output => output.Path)]);
    }

    private static bool IsTracked(BakeProject project, string file, string relativePath)
        => file.EndsWith(TomlSuffix, StringComparison.OrdinalIgnoreCase)
            || project.ProcessorRegistry.TryMatch(relativePath, out _);
}
