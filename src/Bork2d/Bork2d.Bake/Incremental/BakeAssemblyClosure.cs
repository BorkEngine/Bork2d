using System.Globalization;
using System.Reflection;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Processors;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Computes the shape-assembly closure hash recorded in the incremental manifest: the assemblies that
/// carry the registered data types and the registered processors, plus their non-tooling,
/// non-framework dependency closure.
/// </summary>
/// <remarks>
/// <para>
/// The direct set is every <c>RegisterAsset&lt;T&gt;()</c> type assembly and every registered
/// processor's CLR assembly (so the builder's built-in texture processor contributes
/// <c>Bork2d.Bake</c>, a game's processors contribute its assets executable assembly, and the data
/// types contribute the game's common assembly and <c>Bork2d.Engine</c>). The closure then
/// follows their references recursively.
/// </para>
/// <para>
/// Framework and tooling dependencies are skipped so a runtime/framework update does not invalidate
/// every game: names starting with <c>System</c>, <c>Microsoft</c>, or <c>Nerdbank</c>, plus
/// <c>mscorlib</c>, <c>netstandard</c>, <c>WindowsBase</c>, <c>Bork2d.Bake</c>,
/// <c>Bork2d.Engine.Assets</c>, <c>Tomlyn</c>, <c>StbImageSharp</c>, <c>PolyType</c>, and
/// <c>System.IO.Hashing</c>. Direct assemblies are always included even if their name matches a skip
/// rule.
/// </para>
/// <para>
/// Each assembly contributes its simple name and either its file hash (<see cref="Assembly.Location"/>
/// through <see cref="BakeHash.HashFile"/>) or, when it has no file (dynamic or in-memory), its full
/// name. Entries are sorted ordinal by simple name, then full name, so the result never depends on
/// assembly resolution order. A missing referenced assembly is tolerated and skipped.
/// </para>
/// </remarks>
internal static class BakeAssemblyClosure
{
    private const string HashShape = "bork2d.bake.closure.v1";

    private static readonly HashSet<string> _skippedExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mscorlib",
        "netstandard",
        "WindowsBase",
        "Bork2d.Bake",
        "Bork2d.Engine.Assets",
        "Tomlyn",
        "StbImageSharp",
        "PolyType",
        "System.IO.Hashing",
    };

    /// <summary>Computes the closure hash of a validated project.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string ComputeHash(BakeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        IReadOnlyList<Assembly> closure = Collect(project);
        var fields = new List<string>
        {
            HashShape,
            closure.Count.ToString(CultureInfo.InvariantCulture),
        };

        foreach (Assembly assembly in closure)
        {
            fields.Add(assembly.GetName().Name ?? string.Empty);
            fields.Add(HashAssembly(assembly));
        }

        return BakeHash.HashTextFields([.. fields]);
    }

    private static IReadOnlyList<Assembly> Collect(BakeProject project)
    {
        var direct = new List<Assembly>();
        foreach (BakeAssetTypeRegistration registration in project.AssetTypes.Values)
        {
            direct.Add(registration.Type.Assembly);
        }

        foreach (IBakeAssetProcessor processor in project.Processors)
        {
            direct.Add(processor.ProcessorType.Assembly);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closure = new List<Assembly>();
        var pending = new Stack<Assembly>(direct);

        while (pending.Count > 0)
        {
            Assembly assembly = pending.Pop();
            string simpleName = assembly.GetName().Name ?? string.Empty;
            if (!visited.Add(simpleName))
            {
                continue;
            }

            closure.Add(assembly);

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string referenceName = reference.Name ?? string.Empty;
                if (referenceName.Length == 0
                    || IsSkipped(referenceName)
                    || visited.Contains(referenceName))
                {
                    continue;
                }

                Assembly? referenced = TryLoad(reference);
                if (referenced is not null)
                {
                    pending.Push(referenced);
                }
            }
        }

        closure.Sort(static (left, right) =>
        {
            int result = string.CompareOrdinal(
                left.GetName().Name ?? string.Empty,
                right.GetName().Name ?? string.Empty);
            return result != 0
                ? result
                : string.CompareOrdinal(left.FullName ?? string.Empty, right.FullName ?? string.Empty);
        });

        return closure;
    }

    private static bool IsSkipped(string assemblyName)
    {
        if (_skippedExactNames.Contains(assemblyName))
        {
            return true;
        }

        return assemblyName.StartsWith("System", StringComparison.Ordinal)
            || assemblyName.StartsWith("Microsoft", StringComparison.Ordinal)
            || assemblyName.StartsWith("Nerdbank", StringComparison.Ordinal);
    }

    private static Assembly? TryLoad(AssemblyName name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or FileNotFoundException
            or FileLoadException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static string HashAssembly(Assembly assembly)
    {
        string? location = assembly.Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
        {
            try
            {
                return BakeHash.HashFile(location);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
            }
        }

        return assembly.FullName
            ?? assembly.GetName().Name
            ?? string.Empty;
    }
}
