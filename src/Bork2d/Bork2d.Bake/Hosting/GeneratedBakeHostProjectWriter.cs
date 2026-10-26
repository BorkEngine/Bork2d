using System.Text;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// Writes the deterministic <c>BakeHost.csproj</c> of the generated bake host.
/// </summary>
/// <remarks>
/// The generated project is a normal SDK library named <c>Bork2d.BakeHost</c> (the fixed identity
/// <c>InternalsVisibleTo</c> grants and the CLI's load context use). It compiles exactly one explicit
/// source file and references every required assembly by absolute <c>HintPath</c> with
/// <c>Private=true</c>; there are no <c>ProjectReference</c> items, so generation works for any game
/// layout. References are emitted in ordinal assembly-name order for deterministic output.
/// </remarks>
internal static class GeneratedBakeHostProjectWriter
{
    /// <summary>Gets the generated project file name.</summary>
    internal const string FileName = "BakeHost.csproj";

    /// <summary>Writes the generated project text.</summary>
    /// <param name="references">The de-duplicated assembly references.</param>
    /// <param name="configuration">The default build configuration recorded in the project.</param>
    /// <returns>The generated MSBuild project.</returns>
    internal static string Write(IReadOnlyList<BakeHostReference> references, string configuration)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        var lines = new List<string>
        {
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            string.Empty,
            "    <PropertyGroup>",
            "        <TargetFramework>net11.0</TargetFramework>",
            "        <AssemblyName>Bork2d.BakeHost</AssemblyName>",
            "        <RootNamespace>Bork2d.BakeHost</RootNamespace>",
            "        <Nullable>enable</Nullable>",
            "        <ImplicitUsings>enable</ImplicitUsings>",
            "        <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
            "        <Deterministic>true</Deterministic>",
            "        <IsPackable>false</IsPackable>",
            string.Empty,
            "        <!-- The CLI passes a configuration; a bare 'dotnet build' uses this default. -->",
            $"        <Configuration Condition=\"'$(Configuration)' == ''\">{Escape(configuration)}</Configuration>",
            "    </PropertyGroup>",
            string.Empty,
            "    <ItemGroup>",
            $"        <Compile Include=\"{GeneratedBakeManifestWriter.FileName}\" />",
            "    </ItemGroup>",
            string.Empty,
            "    <ItemGroup>",
        };

        foreach (BakeHostReference reference in references)
        {
            lines.Add($"        <Reference Include=\"{Escape(reference.AssemblyName)}\">");
            lines.Add($"            <HintPath>{Escape(reference.Path)}</HintPath>");
            lines.Add("            <Private>true</Private>");
            lines.Add("        </Reference>");
        }

        lines.Add("    </ItemGroup>");
        lines.Add(string.Empty);
        lines.Add("</Project>");

        var builder = new StringBuilder();
        foreach (string line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string Escape(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
