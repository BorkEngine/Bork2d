using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// The inputs recomputed before a bake so it can skip an unchanged multi-pack project.
/// </summary>
/// <remarks>
/// <para>
/// The tracked inputs are every <c>.toml</c> source under every <c>[[assets]]</c> root (custom data and
/// texture sidecars) plus every accepted texture source under every root (D-M4-13/D-M4-14). Paths are
/// descriptor-relative with <c>/</c> separators when the file is under the descriptor directory,
/// otherwise absolute, and the list is sorted ordinal, so the digest is independent of filesystem and
/// pack enumeration order. Non-asset files are ignored, matching discovery (Q-13).
/// </para>
/// <para>
/// <see cref="Outputs"/> is every resolved pack path in descriptor order. The skip decision requires
/// all of them to exist with their recorded hashes; a missing or changed one re-bakes the whole
/// project. The "common" hash covers the game-side assembly closure copied next to the generated host,
/// and the host hash covers the generated host assembly itself. Both are content hashes, so no
/// timestamp is ever trusted.
/// </para>
/// </remarks>
internal sealed class BakeIncrementalInputs
{
    private static readonly HashSet<string> _textureExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp", ".tga"],
        StringComparer.OrdinalIgnoreCase);

    private const string TomlSuffix = ".toml";

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeIncrementalInputs"/> class from already
    /// computed hashes.
    /// </summary>
    /// <param name="descriptorHash">The descriptor file hash.</param>
    /// <param name="inputs">The hashed tracked inputs, sorted ordinal by path.</param>
    /// <param name="commonAssemblyHash">The game-side assembly closure hash.</param>
    /// <param name="hostHash">The generated host assembly hash.</param>
    /// <param name="mode">The pack compression mode name.</param>
    /// <param name="outputs">The absolute pack targets in descriptor order.</param>
    internal BakeIncrementalInputs(
        string descriptorHash,
        IReadOnlyList<BakeInputHash> inputs,
        string commonAssemblyHash,
        string hostHash,
        string mode,
        IReadOnlyList<string> outputs)
    {
        ArgumentNullException.ThrowIfNull(descriptorHash);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(commonAssemblyHash);
        ArgumentNullException.ThrowIfNull(hostHash);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(outputs);
        if (outputs.Count == 0)
        {
            throw new ArgumentException("A bake must produce at least one pack.", nameof(outputs));
        }

        DescriptorHash = descriptorHash;
        Inputs = inputs;
        CommonAssemblyHash = commonAssemblyHash;
        HostHash = hostHash;
        Mode = mode;
        Outputs = [.. outputs];
    }

    /// <summary>Gets the descriptor file hash.</summary>
    internal string DescriptorHash { get; }

    /// <summary>Gets the hashed tracked inputs, sorted ordinal by path.</summary>
    internal IReadOnlyList<BakeInputHash> Inputs { get; }

    /// <summary>Gets the hash of the game-side assembly closure next to the generated host.</summary>
    internal string CommonAssemblyHash { get; }

    /// <summary>Gets the generated host assembly hash.</summary>
    internal string HostHash { get; }

    /// <summary>Gets the pack compression mode name.</summary>
    internal string Mode { get; }

    /// <summary>Gets the absolute pack targets in descriptor order; the first is the main pack.</summary>
    internal IReadOnlyList<string> Outputs { get; }

    /// <summary>Computes the current inputs, or <see langword="null"/> when they cannot be hashed.</summary>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="hostAssemblyPath">The generated host assembly passed to the bake.</param>
    /// <param name="mode">The requested compression mode.</param>
    /// <param name="outputRoot">The requested output root, or <see langword="null"/> for the descriptor directory.</param>
    /// <returns>The current inputs, or <see langword="null"/> when a file is missing or unreadable.</returns>
    internal static BakeIncrementalInputs? TryCollect(
        ProjectDescriptor descriptor,
        string hostAssemblyPath,
        BakeMode mode,
        string? outputRoot)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostAssemblyPath);

        if (!BakeOutputLayout.TryResolve(
            descriptor,
            outputRoot,
            new BakeDiagnostics(),
            out _,
            out IReadOnlyList<BakePackOutput> outputs))
        {
            return null;
        }

        try
        {
            string hostFullPath = Path.GetFullPath(hostAssemblyPath);
            if (!File.Exists(hostFullPath))
            {
                return null;
            }

            string descriptorHash = BakeHash.HashFile(descriptor.DescriptorPath);
            IReadOnlyList<BakeInputHash> inputs = CollectInputs(descriptor);
            string hostDirectory = Path.GetDirectoryName(hostFullPath) ?? AppContext.BaseDirectory;
            string commonHash = HashGameAssemblyClosure(hostDirectory);
            string hostHash = BakeHash.HashFile(hostFullPath);

            return new BakeIncrementalInputs(
                descriptorHash,
                inputs,
                commonHash,
                hostHash,
                ModeName(mode),
                [.. outputs.Select(output => output.Path)]);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return null;
        }
    }

    /// <summary>Gets the manifest mode name for a mode.</summary>
    /// <param name="mode">The bake mode.</param>
    /// <returns><c>debug</c> or <c>release</c>.</returns>
    internal static string ModeName(BakeMode mode)
        => mode == BakeMode.Release ? "release" : "debug";

    private static List<BakeInputHash> CollectInputs(ProjectDescriptor descriptor)
    {
        var byPath = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (ProjectPack pack in descriptor.Packs)
        {
            foreach (string file in BakeSourcePath.EnumerateFilesSorted(pack.Root))
            {
                if (!IsTrackedInput(file))
                {
                    continue;
                }

                byPath[BakeDiagnosticPath.ForSource(descriptor.DescriptorDirectory, file)]
                    = BakeHash.HashFile(file);
            }
        }

        return [.. byPath.Select(pair => new BakeInputHash(pair.Key, pair.Value))];
    }

    private static bool IsTrackedInput(string fullPath)
        => fullPath.EndsWith(TomlSuffix, StringComparison.OrdinalIgnoreCase)
            || _textureExtensions.Contains(Path.GetExtension(fullPath));

    private static string HashGameAssemblyClosure(string hostDirectory)
    {
        string[] files;
        try
        {
            files = Directory.Exists(hostDirectory)
                ? Directory.GetFiles(hostDirectory, "*.dll")
                : [];
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            files = [];
        }

        var entries = new List<string>(files.Length);
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.Length == 0
                || name.EndsWith(".resources", StringComparison.Ordinal)
                || string.Equals(name, BakeHostIdentity.AssemblyName, StringComparison.Ordinal)
                || BakeHostLoadContext.IsToolingAssembly(name))
            {
                continue;
            }

            entries.Add($"{name}={BakeHash.HashFile(file)}");
        }

        entries.Sort(StringComparer.Ordinal);
        return BakeHash.HashTextFields([.. entries]);
    }

    private static bool IsIoFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
}
