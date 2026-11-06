using Season.MSDF;
using MsdfBitmap = Season.MSDF.Bitmap;
using MsdfShape = Season.MSDF.Shape;
using MsdfShapeDistanceFinder = Season.MSDF.ShapeDistanceFinder;
using MsdfVector2 = Season.MSDF.Vector2;

namespace Bork2d.Bake.Fonts;

/// <summary>
/// One glyph's rasterized MSDF cell, validated against the exact glyph outline.
/// </summary>
/// <remarks>
/// <para>
/// The field is generated into a cell-sized bitmap with a Y-downward row order, so row zero is the
/// top of the cell and the bitmap can be copied straight into the top-down atlas. The projection
/// scales em units by the derived em size and translates the shape so the cell's left and bottom
/// edges land on the field margin.
/// </para>
/// <para>
/// Validation rejects a field that is inside or outside at every texel, a NaN channel, and a
/// deterministic 16x16 sample grid whose median sign disagrees with
/// <see cref="MsdfShapeDistanceFinder"/>'s exact true distance. Samples within 1.5 pixels of the
/// exact edge are skipped because quantization dominates there. Mip sign quality is not validated
/// here; it is covered by the renderer GPU test.
/// </para>
/// <para>
/// An infinite channel is not an error: edge coloring intentionally leaves the unused channels of a
/// smooth contour at a saturated far distance, and the median of the three channels recovers the
/// finite distance. Infinity therefore clamps to <c>0</c> (outside) or <c>255</c> (inside) exactly
/// like any other out-of-range value; only a NaN channel is rejected.
/// </para>
/// <para>
/// The outer guard border (the sidecar's <c>padding</c>, or at least one pixel when it is zero) is
/// cleared to black in every channel after generation. Without that guard, adjacent cells can hold
/// different saturated channels in their margins and linear filtering at the shared edge blends them
/// into a median that crosses into the glyph, which appears as straight lines at cell boundaries.
/// </para>
/// </remarks>
internal sealed class FontMsdfCell
{
    private const int SilhouetteSampleGrid = 16;
    private const double EdgeTolerancePixels = 1.5;

    private FontMsdfCell(byte[] pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the straight-alpha RGBA8 cell pixels, row-major and top-down.</summary>
    internal byte[] Pixels { get; }

    /// <summary>Gets the cell width in pixels.</summary>
    internal int Width { get; }

    /// <summary>Gets the cell height in pixels.</summary>
    internal int Height { get; }

    /// <summary>Rasterizes and validates one glyph's MSDF cell.</summary>
    /// <param name="shape">The glyph outline in em units.</param>
    /// <param name="em">The derived integer em size in pixels; positive.</param>
    /// <param name="border">The field margin (<c>range + padding</c>) in pixels; at least zero.</param>
    /// <param name="range">The distance spread in pixels; positive.</param>
    /// <param name="cellWidth">The cell width in pixels, already including both margins.</param>
    /// <param name="cellHeight">The cell height in pixels, already including both margins.</param>
    /// <param name="glyphLabel">The glyph description used in diagnostics.</param>
    /// <param name="diagnostics">The diagnostic bag to report a content failure to.</param>
    /// <returns>The rasterized cell, or <see langword="null"/> after reporting <c>BK9304</c>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or range value is not positive.</exception>
    internal static FontMsdfCell? Create(
        MsdfShape shape,
        int em,
        int border,
        int range,
        int cellWidth,
        int cellHeight,
        string glyphLabel,
        FontDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(glyphLabel);
        if (em <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(em), em, "The em size must be positive.");
        }

        if (border < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(border), border, "The border must not be negative.");
        }

        if (range <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(range), range, "The range must be positive.");
        }

        if (cellWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellWidth),
                cellWidth,
                "The cell width must be positive.");
        }

        if (cellHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellHeight),
                cellHeight,
                "The cell height must be positive.");
        }

        Season.MSDF.Shape.Bounds bounds = shape.GetBounds(0, 1, 0);
        double translateX = ((double)border / em) - bounds.L;
        double translateY = ((double)border / em) - bounds.B;

        var bitmap = new MsdfBitmap(cellWidth, cellHeight, 3, YAxisOrientation.Y_DOWNWARD);
        try
        {
            Msdfgen.GenerateMSDF(
                bitmap.Section(),
                shape,
                new Projection(
                    new MsdfVector2(em, em),
                    new MsdfVector2(translateX, translateY)),
                new Season.MSDF.Range((double)range / em),
                new MSDFGeneratorConfig(true, new ErrorCorrectionConfig()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Error(
                FontDiagnosticCodes.FieldInvalid,
                $"The MSDF field of {glyphLabel} could not be generated: {exception.Message}");
            return null;
        }

        var pixels = new byte[cellWidth * cellHeight * 4];
        for (int y = 0; y < cellHeight; y++)
        {
            for (int x = 0; x < cellWidth; x++)
            {
                ReadOnlySpan<float> channels = bitmap.GetPixel(x, y);
                int offset = ((y * cellWidth) + x) * 4;
                if (!TryConvertChannel(channels[0], out pixels[offset])
                    || !TryConvertChannel(channels[1], out pixels[offset + 1])
                    || !TryConvertChannel(channels[2], out pixels[offset + 2]))
                {
                    diagnostics.Error(
                        FontDiagnosticCodes.FieldInvalid,
                        $"The MSDF field of {glyphLabel} contains a NaN channel at texel "
                            + $"({x}, {y}).");
                    return null;
                }

                pixels[offset + 3] = 255;
            }
        }

        // The generated field saturates its three channels independently, so the field margin can
        // hold different saturated channels on adjacent cells. Linear filtering at the shared cell
        // boundary then blends two such margins and the median can cross into the glyph, which shows
        // up as straight lines at glyph-cell edges. Make the guard border black in every channel:
        // it is outside the distance range and the SDF shader only needs the median there. The
        // padding is the extra margin beyond the range; a zero-padding sidecar still gets one guard
        // pixel so adjacent cells can never blend their saturated channels.
        int guard = Math.Max(border - range, 1);
        for (int y = 0; y < cellHeight; y++)
        {
            for (int x = 0; x < cellWidth; x++)
            {
                if (x >= guard && x < cellWidth - guard
                    && y >= guard && y < cellHeight - guard)
                {
                    continue;
                }

                int offset = ((y * cellWidth) + x) * 4;
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
            }
        }

        var cell = new FontMsdfCell(pixels, cellWidth, cellHeight);
        return cell.Validate(shape, em, translateX, translateY, glyphLabel, diagnostics)
            ? cell
            : null;
    }

    private bool Validate(
        MsdfShape shape,
        int em,
        double translateX,
        double translateY,
        string glyphLabel,
        FontDiagnostics diagnostics)
    {
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
                FontDiagnosticCodes.FieldInvalid,
                hasInside
                    ? $"The MSDF field of {glyphLabel} is inside at every texel; the glyph cannot be "
                        + "represented at the derived em size."
                    : $"The MSDF field of {glyphLabel} is outside at every texel; the glyph cannot be "
                        + "represented at the derived em size.");
            return false;
        }

        double edgeTolerance = EdgeTolerancePixels / em;
        int mismatches = 0;
        int samples = 0;
        try
        {
            var distanceFinder = new MsdfShapeDistanceFinder(shape);
            for (int gridY = 0; gridY < SilhouetteSampleGrid; gridY++)
            {
                int y = SampleIndex(gridY, Height);
                for (int gridX = 0; gridX < SilhouetteSampleGrid; gridX++)
                {
                    int x = SampleIndex(gridX, Width);
                    double shapeX = ((x + 0.5) / em) - translateX;
                    double shapeY = ((Height - (y + 0.5)) / em) - translateY;
                    double distance = distanceFinder.ComputeDistance(
                        new MsdfVector2(shapeX, shapeY),
                        MsdfShapeDistanceFinder.DistanceMode.TrueDistance,
                        true);
                    if (!double.IsFinite(distance))
                    {
                        diagnostics.Error(
                            FontDiagnosticCodes.FieldInvalid,
                            $"The exact outline distance of {glyphLabel} is not finite at sample "
                                + $"({x}, {y}).");
                        return false;
                    }

                    if (Math.Abs(distance) < edgeTolerance)
                    {
                        continue;
                    }

                    samples++;
                    int offset = ((y * Width) + x) * 4;
                    bool fieldInside =
                        Median(Pixels[offset], Pixels[offset + 1], Pixels[offset + 2]) > 0.5;
                    if (fieldInside != distance > 0)
                    {
                        mismatches++;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Error(
                FontDiagnosticCodes.FieldInvalid,
                $"The MSDF field of {glyphLabel} could not be validated: {exception.Message}");
            return false;
        }

        if (mismatches > 0)
        {
            diagnostics.Error(
                FontDiagnosticCodes.FieldInvalid,
                $"The MSDF silhouette of {glyphLabel} disagrees with the exact outline distance at "
                    + $"{mismatches} of {samples} sampled texels.");
            return false;
        }

        return true;
    }

    private static int SampleIndex(int gridIndex, int size)
        => Math.Min((int)(((gridIndex + 0.5) * size) / SilhouetteSampleGrid), size - 1);

    /// <summary>Converts one clamped MSDF channel to RGBA8.</summary>
    /// <param name="value">The generated channel value; may be an infinity.</param>
    /// <param name="converted">The clamped, rounded byte value.</param>
    /// <returns><see langword="false"/> when <paramref name="value"/> is NaN.</returns>
    /// <remarks>
    /// SeasonMSDF does not clamp its output, and a smooth contour deliberately leaves its unused
    /// channels infinite: clamping an infinity to a saturated value is the documented MSDF encoding.
    /// Rounding is half away from zero, which is deterministic and avoids the banker's rounding of
    /// <see cref="Math.Round(double)"/>.
    /// </remarks>
    private static bool TryConvertChannel(float value, out byte converted)
    {
        if (float.IsNaN(value))
        {
            converted = 0;
            return false;
        }

        double clamped = Math.Clamp((double)value, 0, 1);
        converted = (byte)Math.Round(clamped * 255, MidpointRounding.AwayFromZero);
        return true;
    }

    private static double Median(byte red, byte green, byte blue)
        => Math.Max(Math.Min(red, green), Math.Min(Math.Max(red, green), blue)) / 255.0;
}
