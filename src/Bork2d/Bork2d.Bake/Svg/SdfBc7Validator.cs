using System.Globalization;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Svg;

/// <summary>
/// Validates the opt-in BC7 encoding of a generated signed distance field.
/// </summary>
/// <remarks>
/// <para>
/// Base-level median fidelity is the magnification gate: a texel's decoded median must stay within
/// <c>1.25</c> level pixels of the source median, and the RMS error over the level must stay within
/// <c>0.10</c> level pixels, where one level pixel of error is <c>range</c> distance units. The
/// thresholds apply to level 0 only; a low mip of high-frequency art cannot meet them because one 4x4
/// block covers much of the level, and BC7 remains an explicit authoring choice rather than a silent
/// fallback.
/// </para>
/// <para>
/// Decoded sign preservation is the minification gate: every level whose long axis is at least four
/// pixels is sampled on a deterministic 24x24 grid, and the decoded median sign must match the exact
/// signed distance outside a <c>1.5 * 2^L</c> base-pixel edge tolerance. A smaller level cannot
/// represent the sign of a general shape, so it is skipped.
/// </para>
/// </remarks>
internal static class SdfBc7Validator
{
    private const double MaxBaseLevelPixelError = 1.25;
    private const double MaxBaseLevelRmsError = 0.10;
    private const double EdgeToleranceTexels = 1.5;
    private const int SignSampleGrid = 24;
    private const int MinimumSignLevelExtent = 4;

    /// <summary>Decodes and validates every encoded BC7 level.</summary>
    /// <param name="field">The generated field that supplies the exact signed distance.</param>
    /// <param name="sourceLevels">The source RGBA8 mip chain, base level first.</param>
    /// <param name="encodedLevels">The encoded BC7 blocks, base level first.</param>
    /// <param name="diagnostics">The diagnostic bag to report <c>BK9207</c> to.</param>
    /// <returns><see langword="true"/> when every level passes the gate.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The level counts differ.</exception>
    internal static bool Validate(
        MsdfField field,
        IReadOnlyList<TextureLevel> sourceLevels,
        IReadOnlyList<byte[]> encodedLevels,
        SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(sourceLevels);
        ArgumentNullException.ThrowIfNull(encodedLevels);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (sourceLevels.Count != encodedLevels.Count)
        {
            throw new ArgumentException(
                $"The source chain has {sourceLevels.Count} levels but {encodedLevels.Count} were encoded.",
                nameof(encodedLevels));
        }

        var decoder = new BcDecoder();
        for (int level = 0; level < sourceLevels.Count; level++)
        {
            TextureLevel source = sourceLevels[level];
            ColorRgba32[] decoded = decoder.DecodeRaw(
                encodedLevels[level],
                source.Width,
                source.Height,
                CompressionFormat.Bc7);
            if (!ValidateLevel(field, source, ToRgba8(decoded), level, diagnostics))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Validates one already-decoded BC7 level.</summary>
    /// <param name="field">The generated field that supplies the exact signed distance.</param>
    /// <param name="source">The source RGBA8 level.</param>
    /// <param name="decoded">The decoded RGBA8 level; <c>width × height × 4</c> bytes.</param>
    /// <param name="level">The zero-based mip level index.</param>
    /// <param name="diagnostics">The diagnostic bag to report <c>BK9207</c> to.</param>
    /// <returns><see langword="true"/> when the level passes the gate.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The level is negative.</exception>
    /// <exception cref="ArgumentException">The decoded buffer does not match the source dimensions.</exception>
    /// <remarks>
    /// The method takes the decoded pixels instead of the blocks so a test can exercise the quality
    /// and sign failures without depending on a particular encoder result.
    /// </remarks>
    internal static bool ValidateLevel(
        MsdfField field,
        TextureLevel source,
        ReadOnlySpan<byte> decoded,
        int level,
        SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "The mip level must not be negative.");
        }

        long expectedLength = (long)source.Width * source.Height * 4;
        if (decoded.Length != expectedLength)
        {
            throw new ArgumentException(
                $"The decoded level is {decoded.Length} bytes; expected {expectedLength}.",
                nameof(decoded));
        }

        if (level == 0 && !ValidateBaseLevelFidelity(field, source, decoded, diagnostics))
        {
            return false;
        }

        return ValidateDecodedSign(field, source, decoded, level, diagnostics);
    }

    private static bool ValidateBaseLevelFidelity(
        MsdfField field,
        TextureLevel source,
        ReadOnlySpan<byte> decoded,
        SvgDiagnostics diagnostics)
    {
        int pixelCount = source.Width * source.Height;
        double maximum = 0;
        double sumSquares = 0;
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            int offset = pixel * 4;
            double sourceMedian = MsdfField.Median(
                source.Pixels[offset],
                source.Pixels[offset + 1],
                source.Pixels[offset + 2]);
            double decodedMedian = MsdfField.Median(
                decoded[offset],
                decoded[offset + 1],
                decoded[offset + 2]);
            double error = Math.Abs(sourceMedian - decodedMedian) * field.Range;
            maximum = Math.Max(maximum, error);
            sumSquares += error * error;
        }

        double rms = Math.Sqrt(sumSquares / pixelCount);
        if (maximum <= MaxBaseLevelPixelError && rms <= MaxBaseLevelRmsError)
        {
            return true;
        }

        diagnostics.Error(
            SvgDiagnosticCodes.SdfCompressionFailed,
            string.Create(
                CultureInfo.InvariantCulture,
                $"BC7 changed mip level 0 (base) beyond the accepted fidelity: max {maximum:F3} level pixels (limit {MaxBaseLevelPixelError:F2}) and RMS {rms:F3} level pixels (limit {MaxBaseLevelRmsError:F2}) at range {field.Range}."));
        return false;
    }

    private static bool ValidateDecodedSign(
        MsdfField field,
        TextureLevel source,
        ReadOnlySpan<byte> decoded,
        int level,
        SvgDiagnostics diagnostics)
    {
        if (Math.Max(source.Width, source.Height) < MinimumSignLevelExtent)
        {
            return true;
        }

        int step = 1 << level;
        int mismatches = 0;
        int samples = 0;
        for (int gridY = 0; gridY < SignSampleGrid; gridY++)
        {
            int y = Math.Min((int)(((gridY + 0.5) * source.Height) / SignSampleGrid), source.Height - 1);
            for (int gridX = 0; gridX < SignSampleGrid; gridX++)
            {
                int x = Math.Min((int)(((gridX + 0.5) * source.Width) / SignSampleGrid), source.Width - 1);
                int offset = ((y * source.Width) + x) * 4;
                double baseX = (x + 0.5) * step;
                double baseY = (y + 0.5) * step;
                double distance = field.ComputeDistance(baseX, field.Height - baseY);
                if (Math.Abs(distance) < EdgeToleranceTexels * step)
                {
                    continue;
                }

                samples++;
                double median = MsdfField.Median(decoded[offset], decoded[offset + 1], decoded[offset + 2]);
                if ((median > 0.5) != distance > 0)
                {
                    mismatches++;
                }
            }
        }

        if (mismatches == 0)
        {
            return true;
        }

        diagnostics.Error(
            SvgDiagnosticCodes.SdfCompressionFailed,
            string.Create(
                CultureInfo.InvariantCulture,
                $"BC7 flipped the decoded sign of mip level {level} at {mismatches} of {samples} sampled texels (edge tolerance {EdgeToleranceTexels * step} base pixels)."));
        return false;
    }

    private static byte[] ToRgba8(IReadOnlyList<ColorRgba32> decoded)
    {
        var pixels = new byte[decoded.Count * 4];
        for (int pixel = 0; pixel < decoded.Count; pixel++)
        {
            ColorRgba32 color = decoded[pixel];
            int offset = pixel * 4;
            pixels[offset] = color.r;
            pixels[offset + 1] = color.g;
            pixels[offset + 2] = color.b;
            pixels[offset + 3] = color.a;
        }

        return pixels;
    }
}
