using Bork2d.Bake.Textures;
using Season.MSDF;
using MsdfBitmap = Season.MSDF.Bitmap;
using MsdfContour = Season.MSDF.Contour;
using MsdfShape = Season.MSDF.Shape;
using MsdfShapeDistanceFinder = Season.MSDF.ShapeDistanceFinder;
using MsdfVector2 = Season.MSDF.Vector2;

namespace Bork2d.Bake.Svg;

/// <summary>
/// One generated SVG signed distance field: the RGBA8 base level and the private SeasonMSDF shape it
/// was rasterized from.
/// </summary>
/// <remarks>
/// <para>
/// The shape is kept private so the exact signed distance stays available for validation without
/// exposing a SeasonMSDF type outside this wrapper. All coordinates are pixel-space and Y-up, and the
/// bitmap rows are top-down, so texel <c>(x,y)</c> corresponds to the shape point
/// <c>(x + 0.5, Height - (y + 0.5))</c>.
/// </para>
/// <para>
/// SeasonMSDF reports a positive signed distance inside a shape whose outer contour is clockwise in
/// Y-up space (its winding is <c>+1</c> there), while the task-1 parser normalizes every nonzero
/// root to a positive shoelace area. The builder therefore reverses each parsed contour before adding
/// its edges: all winding numbers negate, the fill region and hole topology are unchanged for both
/// fill rules, and the standard "median above 0.5 means inside" MSDF convention holds. The parser is
/// not modified and <c>Shape.OrientContours</c> is not called.
/// </para>
/// </remarks>
internal sealed class MsdfField
{
    private const int SilhouetteSampleGrid = 32;
    private const double EdgeTolerancePixels = 1.5;

    private readonly MsdfShapeDistanceFinder _distanceFinder;

    private MsdfField(
        MsdfShape shape,
        byte[] pixels,
        int width,
        int height,
        int range)
    {
        _distanceFinder = new MsdfShapeDistanceFinder(shape);
        Pixels = pixels;
        Width = width;
        Height = height;
        Range = range;
    }

    /// <summary>Gets the straight-alpha RGBA8 base-level pixels, row-major and top-down.</summary>
    internal byte[] Pixels { get; }

    /// <summary>Gets the base-level pixel width.</summary>
    internal int Width { get; }

    /// <summary>Gets the base-level pixel height.</summary>
    internal int Height { get; }

    /// <summary>Gets the distance spread in pixels used to generate the field.</summary>
    internal int Range { get; }

    /// <summary>Generates the MSDF field of one artwork at the given pixel layout.</summary>
    /// <param name="artwork">The normalized artwork.</param>
    /// <param name="scale">The artboard user-unit to pixel scale; finite and greater than zero.</param>
    /// <param name="inset">The margin on every side in pixels; at least zero.</param>
    /// <param name="textureWidth">The base-level texture width; positive.</param>
    /// <param name="textureHeight">The base-level texture height; positive.</param>
    /// <param name="range">The distance spread in pixels; positive.</param>
    /// <param name="diagnostics">The diagnostic bag to report a content failure to.</param>
    /// <returns>
    /// The generated field, or <see langword="null"/> after reporting <c>BK9206</c> when the shape is
    /// invalid, a pixel-mapped point is not finite, or the generated values are not finite.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A layout value is out of range.</exception>
    internal static MsdfField? Create(
        SvgArtwork artwork,
        double scale,
        int inset,
        int textureWidth,
        int textureHeight,
        int range,
        SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                scale,
                "The pixel scale must be finite and greater than zero.");
        }

        if (inset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inset), inset, "The inset must not be negative.");
        }

        if (textureWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(textureWidth),
                textureWidth,
                "The texture width must be positive.");
        }

        if (textureHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(textureHeight),
                textureHeight,
                "The texture height must be positive.");
        }

        if (range <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(range), range, "The range must be positive.");
        }

        var shape = new MsdfShape();
        for (int shapeIndex = 0; shapeIndex < artwork.Shapes.Count; shapeIndex++)
        {
            SvgShape svgShape = artwork.Shapes[shapeIndex];
            for (int contourIndex = 0; contourIndex < svgShape.Contours.Count; contourIndex++)
            {
                if (!TryAddContour(
                    shape,
                    svgShape.Contours[contourIndex],
                    scale,
                    inset,
                    shapeIndex,
                    contourIndex,
                    diagnostics))
                {
                    return null;
                }
            }
        }

        if (!shape.Validate())
        {
            diagnostics.Error(
                SvgDiagnosticCodes.SdfBakeFailed,
                "The MSDF shape failed SeasonMSDF validation; no texture was emitted.");
            return null;
        }

        EdgeColoring.EdgeColoringSimple(shape, Math.PI / 60, seed: 0);

        var bitmap = new MsdfBitmap(
            textureWidth,
            textureHeight,
            channels: 3,
            YAxisOrientation.Y_DOWNWARD);
        Msdfgen.GenerateMSDF(
            bitmap.Section(),
            shape,
            new Projection(new MsdfVector2(1, 1), new MsdfVector2(0, 0)),
            new Season.MSDF.Range(range),
            new MSDFGeneratorConfig(true, new ErrorCorrectionConfig()));

        var pixels = new byte[textureWidth * textureHeight * 4];
        for (int y = 0; y < textureHeight; y++)
        {
            for (int x = 0; x < textureWidth; x++)
            {
                int offset = ((y * textureWidth) + x) * 4;
                if (!TryConvertPixel(bitmap.GetPixel(x, y), x, y, pixels.AsSpan(offset, 4), diagnostics))
                {
                    return null;
                }
            }
        }

        var field = new MsdfField(shape, pixels, textureWidth, textureHeight, range);
        return field;
    }

    /// <summary>Converts one clamped MSDF channel to RGBA8.</summary>
    /// <param name="value">The generated channel value; may be outside <c>[0,1]</c>.</param>
    /// <param name="converted">The clamped, rounded byte value.</param>
    /// <returns><see langword="false"/> when <paramref name="value"/> is not finite.</returns>
    /// <remarks>
    /// SeasonMSDF does not clamp its output. Rounding is half away from zero, which is deterministic
    /// and avoids the banker's rounding of <see cref="Math.Round(double)"/>.
    /// </remarks>
    internal static bool TryConvertChannel(float value, out byte converted)
    {
        if (!float.IsFinite(value))
        {
            converted = 0;
            return false;
        }

        double clamped = Math.Clamp((double)value, 0, 1);
        converted = (byte)Math.Round(clamped * 255, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Converts one texel's clamped MSDF channels to RGBA8.</summary>
    /// <param name="channels">The three generated channels.</param>
    /// <param name="x">The texel column, used in the diagnostic.</param>
    /// <param name="y">The texel row, used in the diagnostic.</param>
    /// <param name="destination">The four RGBA8 bytes to fill; alpha is set to <c>255</c>.</param>
    /// <param name="diagnostics">The diagnostic bag to report a non-finite channel to.</param>
    /// <returns>
    /// <see langword="false"/> after reporting <c>BK9206</c> when a channel is not finite.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A span is too short.</exception>
    internal static bool TryConvertPixel(
        ReadOnlySpan<float> channels,
        int x,
        int y,
        Span<byte> destination,
        SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (channels.Length < 3)
        {
            throw new ArgumentException("A texel requires three channels.", nameof(channels));
        }

        if (destination.Length < 4)
        {
            throw new ArgumentException("A texel requires four RGBA8 bytes.", nameof(destination));
        }

        if (!TryConvertChannel(channels[0], out destination[0])
            || !TryConvertChannel(channels[1], out destination[1])
            || !TryConvertChannel(channels[2], out destination[2]))
        {
            diagnostics.Error(
                SvgDiagnosticCodes.SdfBakeFailed,
                $"The MSDF field contains a non-finite channel at texel ({x}, {y}).");
            return false;
        }

        destination[3] = 255;
        return true;
    }

    /// <summary>Computes the median of the three MSDF channels as a normalized value.</summary>
    /// <param name="red">The first channel.</param>
    /// <param name="green">The second channel.</param>
    /// <param name="blue">The third channel.</param>
    /// <returns>Median of the three channels in <c>[0,1]</c>.</returns>
    internal static double Median(byte red, byte green, byte blue)
        => Math.Max(Math.Min(red, green), Math.Min(Math.Max(red, green), blue)) / 255.0;

    /// <summary>Computes the exact signed distance at a pixel-space Y-up point.</summary>
    /// <param name="x">The shape X coordinate in pixels.</param>
    /// <param name="y">The shape Y coordinate in pixels, with the origin at the bottom-left.</param>
    /// <returns>The signed distance in pixels; positive inside and negative outside.</returns>
    /// <remarks>
    /// The distance finder is not thread-safe, so all validation of one field stays on the bake
    /// thread.
    /// </remarks>
    internal double ComputeDistance(double x, double y)
        => _distanceFinder.ComputeDistance(
            new MsdfVector2(x, y),
            MsdfShapeDistanceFinder.DistanceMode.TrueDistance,
            true);

    /// <summary>Validates the base level against the exact shape distance.</summary>
    /// <param name="diagnostics">The diagnostic bag to report a failure to.</param>
    /// <returns><see langword="true"/> when the field can represent the shape.</returns>
    /// <remarks>
    /// The check rejects an empty, all-inside, or all-outside field, then samples a deterministic
    /// 32x32 grid of texel centers and compares the median channel sign with the exact signed
    /// distance, skipping samples closer than 1.5 pixels to the edge where quantization dominates.
    /// </remarks>
    internal bool ValidateBase(SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        bool hasInside = false;
        bool hasOutside = false;
        for (int offset = 0; offset < Pixels.Length; offset += 4)
        {
            bool inside = Median(Pixels[offset], Pixels[offset + 1], Pixels[offset + 2]) > 0.5;
            hasInside |= inside;
            hasOutside |= !inside;
        }

        if (!hasInside || !hasOutside)
        {
            diagnostics.Error(
                SvgDiagnosticCodes.SdfBakeFailed,
                hasInside
                    ? "The MSDF field is inside at every texel; the shape cannot be represented at "
                        + "this resolution and margin."
                    : "The MSDF field is outside at every texel; the shape cannot be represented at "
                        + "this resolution and margin.");
            return false;
        }

        int mismatches = 0;
        int samples = 0;
        for (int gridY = 0; gridY < SilhouetteSampleGrid; gridY++)
        {
            int y = SampleIndex(gridY, Height);
            for (int gridX = 0; gridX < SilhouetteSampleGrid; gridX++)
            {
                int x = SampleIndex(gridX, Width);
                int offset = ((y * Width) + x) * 4;
                double distance = ComputeDistance(x + 0.5, Height - (y + 0.5));
                if (Math.Abs(distance) < EdgeTolerancePixels)
                {
                    continue;
                }

                samples++;
                bool fieldInside = Median(Pixels[offset], Pixels[offset + 1], Pixels[offset + 2]) > 0.5;
                if (fieldInside != distance > 0)
                {
                    mismatches++;
                }
            }
        }

        if (mismatches > 0)
        {
            diagnostics.Error(
                SvgDiagnosticCodes.SdfBakeFailed,
                $"The MSDF silhouette disagrees with the exact signed distance at {mismatches} of "
                    + $"{samples} sampled texels (edge tolerance {EdgeTolerancePixels} pixels).");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that mip levels 1 and 2 still agree with the exact field sign.
    /// </summary>
    /// <param name="levels">The full RGBA8 mip chain, base level first.</param>
    /// <param name="diagnostics">The diagnostic bag to report a failure to.</param>
    /// <returns><see langword="true"/> when the sampled mip signs match the exact distance.</returns>
    /// <remarks>
    /// A level is checked only when it exists and its long axis is at least four pixels; a smaller
    /// level cannot represent the sign of a general shape. Each sampled level texel center maps to
    /// base pixel center <c>((x + 0.5) * 2^L, (y + 0.5) * 2^L)</c>, which maps to the Y-up shape
    /// point <c>(baseX, Height - baseY)</c>. Samples within 1.5 level texels of the edge are skipped.
    /// </remarks>
    internal bool ValidateMipChain(IReadOnlyList<TextureLevel> levels, SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(diagnostics);

        for (int level = 1; level <= 2 && level < levels.Count; level++)
        {
            TextureLevel current = levels[level];
            if (Math.Max(current.Width, current.Height) < 4)
            {
                continue;
            }

            int step = 1 << level;
            int mismatches = 0;
            int samples = 0;
            for (int gridY = 0; gridY < SilhouetteSampleGrid; gridY++)
            {
                int y = SampleIndex(gridY, current.Height);
                for (int gridX = 0; gridX < SilhouetteSampleGrid; gridX++)
                {
                    int x = SampleIndex(gridX, current.Width);
                    double median = MedianBilinear(current, x + 0.5, y + 0.5);
                    double baseX = (x + 0.5) * step;
                    double baseY = (y + 0.5) * step;
                    double distance = ComputeDistance(baseX, Height - baseY);
                    if (Math.Abs(distance) < EdgeTolerancePixels * step)
                    {
                        continue;
                    }

                    samples++;
                    if ((median > 0.5) != distance > 0)
                    {
                        mismatches++;
                    }
                }
            }

            if (mismatches > 0)
            {
                diagnostics.Error(
                    SvgDiagnosticCodes.SdfBakeFailed,
                    $"The distance-preserving mip level {level} disagrees with the exact signed "
                        + $"distance at {mismatches} of {samples} sampled texels (edge tolerance "
                        + $"{EdgeTolerancePixels * step} base pixels).");
                return false;
            }
        }

        return true;
    }

    private static bool TryAddContour(
        MsdfShape shape,
        SvgContour contour,
        double scale,
        int inset,
        int shapeIndex,
        int contourIndex,
        SvgDiagnostics diagnostics)
    {
        MsdfContour target = shape.AddContour();
        foreach (SvgSegment segment in contour.Reverse().Segments)
        {
            switch (segment)
            {
                case SvgLineSegment line:
                    if (!TryToPixel(line.Start, scale, inset, out MsdfVector2 lineStart)
                        || !TryToPixel(line.End, scale, inset, out MsdfVector2 lineEnd))
                    {
                        return ReportNonFinitePoint(diagnostics, shapeIndex, contourIndex);
                    }

                    target.AddEdge(new EdgeHolder(lineStart, lineEnd, EdgeColor.WHITE));
                    break;

                case SvgQuadraticSegment quadratic:
                    if (!TryToPixel(quadratic.Start, scale, inset, out MsdfVector2 quadraticStart)
                        || !TryToPixel(quadratic.Control, scale, inset, out MsdfVector2 quadraticControl)
                        || !TryToPixel(quadratic.End, scale, inset, out MsdfVector2 quadraticEnd))
                    {
                        return ReportNonFinitePoint(diagnostics, shapeIndex, contourIndex);
                    }

                    target.AddEdge(new EdgeHolder(
                        quadraticStart,
                        quadraticControl,
                        quadraticEnd,
                        EdgeColor.WHITE));
                    break;

                case SvgCubicSegment cubic:
                    if (!TryToPixel(cubic.Start, scale, inset, out MsdfVector2 cubicStart)
                        || !TryToPixel(cubic.Control1, scale, inset, out MsdfVector2 cubicControl1)
                        || !TryToPixel(cubic.Control2, scale, inset, out MsdfVector2 cubicControl2)
                        || !TryToPixel(cubic.End, scale, inset, out MsdfVector2 cubicEnd))
                    {
                        return ReportNonFinitePoint(diagnostics, shapeIndex, contourIndex);
                    }

                    target.AddEdge(new EdgeHolder(
                        cubicStart,
                        cubicControl1,
                        cubicControl2,
                        cubicEnd,
                        EdgeColor.WHITE));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"The segment type '{segment.GetType().Name}' is not supported.");
            }
        }

        return true;
    }

    private static bool ReportNonFinitePoint(
        SvgDiagnostics diagnostics,
        int shapeIndex,
        int contourIndex)
    {
        diagnostics.Error(
            SvgDiagnosticCodes.SdfBakeFailed,
            $"The pixel mapping of artwork shape {shapeIndex} contour {contourIndex} produced a non-finite point; the shape cannot be represented at this resolution and margin.");
        return false;
    }

    private static bool TryToPixel(
        SvgPoint point,
        double scale,
        int inset,
        out MsdfVector2 pixel)
    {
        double x = inset + (point.X * scale);
        double y = inset + (point.Y * scale);
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            pixel = default;
            return false;
        }

        pixel = new MsdfVector2(x, y);
        return true;
    }

    private static int SampleIndex(int gridIndex, int size)
        => Math.Min((int)(((gridIndex + 0.5) * size) / SilhouetteSampleGrid), size - 1);

    private static double MedianBilinear(TextureLevel level, double textureX, double textureY)
    {
        double sampleX = textureX - 0.5;
        double sampleY = textureY - 0.5;
        int left = (int)Math.Floor(sampleX);
        int top = (int)Math.Floor(sampleY);
        double fractionX = sampleX - left;
        double fractionY = sampleY - top;
        int right = Math.Clamp(left + 1, 0, level.Width - 1);
        int bottom = Math.Clamp(top + 1, 0, level.Height - 1);
        left = Math.Clamp(left, 0, level.Width - 1);
        top = Math.Clamp(top, 0, level.Height - 1);

        int topLeft = ((top * level.Width) + left) * 4;
        int topRight = ((top * level.Width) + right) * 4;
        int bottomLeft = ((bottom * level.Width) + left) * 4;
        int bottomRight = ((bottom * level.Width) + right) * 4;

        double red = Bilerp(
            level.Pixels[topLeft],
            level.Pixels[topRight],
            level.Pixels[bottomLeft],
            level.Pixels[bottomRight],
            fractionX,
            fractionY);
        double green = Bilerp(
            level.Pixels[topLeft + 1],
            level.Pixels[topRight + 1],
            level.Pixels[bottomLeft + 1],
            level.Pixels[bottomRight + 1],
            fractionX,
            fractionY);
        double blue = Bilerp(
            level.Pixels[topLeft + 2],
            level.Pixels[topRight + 2],
            level.Pixels[bottomLeft + 2],
            level.Pixels[bottomRight + 2],
            fractionX,
            fractionY);
        return Math.Max(Math.Min(red, green), Math.Min(Math.Max(red, green), blue)) / 255.0;
    }

    private static double Bilerp(
        double topLeft,
        double topRight,
        double bottomLeft,
        double bottomRight,
        double fractionX,
        double fractionY)
    {
        double top = topLeft + ((topRight - topLeft) * fractionX);
        double bottom = bottomLeft + ((bottomRight - bottomLeft) * fractionX);
        return top + ((bottom - top) * fractionY);
    }
}
