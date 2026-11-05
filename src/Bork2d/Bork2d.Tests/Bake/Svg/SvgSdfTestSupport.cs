using Bork2d.Bake.Svg;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Shared artwork, bake, and KTX2 sampling helpers for the SDF bake tests.
/// </summary>
internal static class SvgSdfTestSupport
{
    /// <summary>A 200x100 artboard: the longer axis is the texture width.</summary>
    internal const string WideViewBox = "0 0 200 100";

    /// <summary>A 100x200 artboard: the longer axis is the texture height.</summary>
    internal const string TallViewBox = "0 0 100 200";

    /// <summary>
    /// A filled annulus as one nonzero path: outer radius 40 and inner radius 20 around user
    /// <c>(100,50)</c>. At the wide-viewbox test layout the hole center is texel <c>(64,36)</c> and
    /// the ring samples sit at <c>(80,36)</c>.
    /// </summary>
    internal const string DonutPath =
        "<path d=\"M 60 50 a 40 40 0 1 0 80 0 a 40 40 0 1 0 -80 0 Z "
        + "M 80 50 a 20 20 0 1 1 40 0 a 20 20 0 1 1 -40 0 Z\"/>";

    /// <summary>The same annulus authored with the even-odd fill rule.</summary>
    internal const string DonutEvenOddPath =
        "<path fill-rule=\"evenodd\" d=\"M 60 50 a 40 40 0 1 0 80 0 a 40 40 0 1 0 -80 0 Z "
        + "M 80 50 a 20 20 0 1 1 40 0 a 20 20 0 1 1 -40 0 Z\"/>";

    /// <summary>
    /// A thin five-point star spanning user <c>(51,5)</c> to <c>(149,97)</c>; it is the high-frequency
    /// BC7 case that still must pass the base-level fidelity gate.
    /// </summary>
    internal const string StarPath =
        "<path d=\"M 100 5 L 111.8 40.2 L 148.9 40.2 L 118.9 62 L 130.6 97.2 L 100 75.4 "
        + "L 69.4 97.2 L 81.1 62 L 51.1 40.2 L 88.2 40.2 Z\"/>";

    internal static SvgArtwork Parse(string body, string viewBox = WideViewBox)
        => SvgTestSupport.Parse(SvgTestSupport.Document(body, $"viewBox=\"{viewBox}\""));

    internal static SvgSdfBakeOptions Options(
        int resolution = 128,
        int range = 8,
        int padding = 2,
        SdfCompression compression = SdfCompression.None)
        => new(resolution, range, padding, compression);

    internal static SvgSdfBakeResult Bake(
        string body,
        SvgSdfBakeOptions options,
        string viewBox = WideViewBox)
        => Bake(Parse(body, viewBox), options);

    internal static SvgSdfBakeResult Bake(SvgArtwork artwork, SvgSdfBakeOptions options)
    {
        var diagnostics = new SvgDiagnostics();
        SvgSdfBakeResult? result = SvgSdfBaker.Bake(artwork, options, diagnostics);
        Assert.NotNull(result);
        Assert.False(diagnostics.HasErrors);
        return result!;
    }

    internal static SvgDiagnostics BakeFailure(
        string body,
        SvgSdfBakeOptions options,
        string viewBox = WideViewBox)
        => BakeFailure(Parse(body, viewBox), options);

    internal static SvgDiagnostics BakeFailure(SvgArtwork artwork, SvgSdfBakeOptions options)
    {
        var diagnostics = new SvgDiagnostics();
        SvgSdfBakeResult? result = SvgSdfBaker.Bake(artwork, options, diagnostics);
        Assert.Null(result);
        Assert.True(diagnostics.HasErrors);
        return diagnostics;
    }

    internal static Ktx2TestDocument Document(SvgSdfBakeResult result)
        => Ktx2TestDocument.Parse(result.Texture.Payload.ToArray());

    internal static double Median(byte red, byte green, byte blue)
        => Math.Max(Math.Min(red, green), Math.Min(Math.Max(red, green), blue)) / 255.0;

    internal static double MedianAt(byte[] rgba, int width, int x, int y)
    {
        int offset = ((y * width) + x) * 4;
        return Median(rgba[offset], rgba[offset + 1], rgba[offset + 2]);
    }

    /// <summary>
    /// Maps an artboard user point to the texel that contains it under the bake's pixel mapping.
    /// </summary>
    /// <param name="userX">The X coordinate in artboard user units.</param>
    /// <param name="userY">The Y coordinate in artboard user units, measured from the bottom-left.</param>
    /// <param name="pixelsPerUnit">The artboard user-unit to pixel scale.</param>
    /// <param name="inset">The margin on every side in pixels.</param>
    /// <param name="textureHeight">The texture height in pixels.</param>
    /// <returns>The top-down texel column and row.</returns>
    internal static (int X, int Y) TexelForUserPoint(
        double userX,
        double userY,
        double pixelsPerUnit,
        int inset,
        int textureHeight)
    {
        int x = (int)Math.Floor(inset + (userX * pixelsPerUnit));
        int bottomUpY = (int)Math.Floor(inset + (userY * pixelsPerUnit));
        return (x, textureHeight - 1 - bottomUpY);
    }
}
