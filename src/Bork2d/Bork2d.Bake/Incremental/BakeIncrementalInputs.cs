using Bork2d.Bake.Baking;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// The inputs recomputed before a bake so it can skip an unchanged project.
/// </summary>
/// <remarks>
/// <para>
/// The tracked inputs are every <c>.toml</c> source anywhere under the asset root (custom data and
/// texture sidecars) plus every accepted texture source anywhere under the asset root (D-M4-13:
/// there is no special <c>textures/</c> folder). Paths are project-relative with <c>/</c> separators
/// when the file is under the descriptor directory, otherwise absolute, and the list is sorted
/// ordinal, so the digest is independent of filesystem enumeration order. Non-asset files are
/// ignored, matching discovery (Q-13).
/// </para>
/// <para>
/// The "common" hash covers the game-side assembly closure copied next to the generated host
/// (<c>Project.Common</c> and any data library it references). The host output directory contains
/// exactly the assemblies the generated host references, and the host assembly covers the generated
/// host itself. Both are content hashes, so no timestamp is ever trusted.
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
    /// <param name="outputPath">The absolute pack target path.</param>
    internal BakeIncrementalInputs(
        string descriptorHash,
        IReadOnlyList<BakeInputHash> inputs,
        string commonAssemblyHash,
        string hostHash,
        string mode,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(descriptorHash);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(commonAssemblyHash);
        ArgumentNullException.ThrowIfNull(hostHash);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(outputPath);

        DescriptorHash = descriptorHash;
        Inputs = inputs;
        CommonAssemblyHash = commonAssemblyHash;
        HostHash = hostHash;
        Mode = mode;
        OutputPath = outputPath;
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

    /// <summary>Gets the absolute pack target path.</summary>
    internal string OutputPath { get; }

    /// <summary>Computes the current inputs, or <see langword="null"/> when they cannot be hashed.</summary>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="hostAssemblyPath">The generated host assembly passed to the bake.</param>
    /// <param name="mode">The requested compression mode.</param>
    /// <param name="packPath">The resolved absolute pack target.</param>
    /// <returns>The current inputs, or <see langword="null"/> when a file is missing or unreadable.</returns>
    internal static BakeIncrementalInputs? TryCollect(
        ProjectDescriptor descriptor,
        string hostAssemblyPath,
        BakeMode mode,
        string packPath)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);

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
                Path.GetFullPath(packPath));
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
        var inputs = new List<BakeInputHash>();

        foreach (string file in BakeSourcePath.EnumerateFilesSorted(descriptor.AssetsRoot))
        {
            if (!IsTrackedInput(file))
            {
                continue;
            }

            inputs.Add(new BakeInputHash(
                BakeDiagnosticPath.ForSource(descriptor.DescriptorDirectory, file),
                BakeHash.HashFile(file)));
        }

        inputs.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return inputs;
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
