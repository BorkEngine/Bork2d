using System.Globalization;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Shaders;
using Bork2d.Bake.Svg;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Computes the deterministic builder-configuration hash recorded in the incremental manifest.
/// </summary>
/// <remarks>
/// <para>
/// The hash covers everything that changes what a bake would produce without changing a tracked
/// source: the project root, every pack in builder order (index, pack path, relative root, absolute
/// root), the texture defaults, the sorted registered data-type names and their assembly simple
/// names, and the processor registration order with each processor's name, CLR type, sidecar policy,
/// and sorted normalized extensions.
/// </para>
/// <para>
/// The shader tool marker (<see cref="ShaderToolInfo.ConfigurationMarker"/>), the texture tool marker
/// (<see cref="TextureToolInfo.ConfigurationMarker"/>), and the SVG tool marker
/// (<see cref="SvgToolInfo.ConfigurationMarker"/>) are always part of the hash, so upgrading shaderc,
/// changing the fragment layout/prologue/packer, changing the KTX2 writer, upgrading the block
/// encoder, changing the SVG parser or MSDF generator, or changing the SDF mip handling invalidates a
/// manifest written by the earlier tool. Fields are framed by <see cref="BakeHash.HashTextFields"/>
/// and use a version marker, so a later hash-shape change invalidates older manifests instead of
/// colliding with them. Processor and type assembly contents are hashed separately by
/// <see cref="BakeAssemblyClosure"/>.
/// </para>
/// </remarks>
internal static class BakeConfigurationHash
{
    private const string HashShape = "bork2d.bake.configuration.v4";

    /// <summary>Computes the configuration hash of a validated project.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string Compute(BakeProject project)
        => Compute(project, TextureToolInfo.ConfigurationMarker, SvgToolInfo.ConfigurationMarker);

    /// <summary>
    /// Computes the configuration hash with an explicit texture tool marker.
    /// </summary>
    /// <remarks>
    /// The explicit-marker overload exists so a test can prove the marker participates in the hash;
    /// production always uses <see cref="TextureToolInfo.ConfigurationMarker"/>. The SVG marker is
    /// still <see cref="SvgToolInfo.ConfigurationMarker"/>.
    /// </remarks>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="textureToolMarker">The texture tool marker to hash.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string Compute(BakeProject project, string textureToolMarker)
        => Compute(project, textureToolMarker, SvgToolInfo.ConfigurationMarker);

    /// <summary>
    /// Computes the configuration hash with explicit texture and SVG tool markers.
    /// </summary>
    /// <remarks>
    /// The explicit-marker overload exists so a test can prove each marker participates in the hash;
    /// production always uses the marker constants.
    /// </remarks>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="textureToolMarker">The texture tool marker to hash.</param>
    /// <param name="svgToolMarker">The SVG tool marker to hash.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string Compute(BakeProject project, string textureToolMarker, string svgToolMarker)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureToolMarker);
        ArgumentException.ThrowIfNullOrWhiteSpace(svgToolMarker);

        var fields = new List<string>
        {
            HashShape,
            ShaderToolInfo.ConfigurationMarker,
            textureToolMarker,
            svgToolMarker,
            project.ProjectRoot,
            Format(project.Packs.Count),
        };

        foreach (BakePack pack in project.Packs)
        {
            fields.Add(Format(pack.Index));
            fields.Add(pack.PackPath);
            fields.Add(pack.RelativeRoot);
            fields.Add(pack.Root);
        }

        fields.Add(project.TextureFilter.ToString());
        fields.Add(project.TextureWrap.ToString());

        string[] typeNames = [.. project.AssetTypes.Keys.OrderBy(name => name, StringComparer.Ordinal)];
        fields.Add(Format(typeNames.Length));
        foreach (string typeName in typeNames)
        {
            BakeAssetTypeRegistration registration = project.AssetTypes[typeName];
            fields.Add(typeName);
            fields.Add(registration.Type.Assembly.GetName().Name ?? string.Empty);
        }

        fields.Add(Format(project.Processors.Count));
        foreach (BakeProcessorRegistration processor in project.Processors)
        {
            Type processorType = processor.ProcessorType;
            fields.Add(processor.Name);
            fields.Add(processorType.FullName ?? processorType.Name);
            fields.Add(processor.Policy.ToString());

            string[] extensions = [.. processor.Extensions
                .Select(NormalizeExtension)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(extension => extension, StringComparer.Ordinal)];
            fields.Add(Format(extensions.Length));
            fields.AddRange(extensions);
        }

        return BakeHash.HashTextFields([.. fields]);
    }

    private static string NormalizeExtension(string extension)
    {
        string normalized = extension.Trim().ToLowerInvariant();
        return normalized.StartsWith('.') ? normalized[1..] : normalized;
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
