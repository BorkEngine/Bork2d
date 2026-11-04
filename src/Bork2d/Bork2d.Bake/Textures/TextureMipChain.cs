namespace Bork2d.Bake.Textures;

/// <summary>
/// Generates the full mip chain of a texture as straight-alpha RGBA8 levels.
/// </summary>
/// <remarks>
/// <para>
/// Every level is produced by one deterministic box filter: the destination level has
/// <c>max(1, width >> 1)</c> by <c>max(1, height >> 1)</c> pixels and each destination pixel averages
/// the source rectangle it covers, so odd source dimensions are still fully represented. All data is
/// linear, so there is no gamma conversion, premultiplication, or resampling beyond the box average.
/// Channel rounding is half-up integer arithmetic.
/// </para>
/// <para>
/// The channel type controls which values the chain preserves. <see cref="TextureChannelType.Rgb"/>
/// forces alpha to <c>255</c> at the base level and therefore at every mip. The other types leave the
/// channels they do not consume unchanged; only the storage step for the final format reads them.
/// </para>
/// </remarks>
internal static class TextureMipChain
{
    /// <summary>Gets the number of levels in a full chain for the given base dimensions.</summary>
    /// <param name="width">The base-level pixel width.</param>
    /// <param name="height">The base-level pixel height.</param>
    /// <returns>The level count, at least one.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    internal static int LevelCount(int width, int height)
    {
        ValidateDimensions(width, height);

        int levels = 1;
        int levelWidth = width;
        int levelHeight = height;
        while (levelWidth > 1 || levelHeight > 1)
        {
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
            levels++;
        }

        return levels;
    }

    /// <summary>Gets one level's pixel width in a full chain.</summary>
    /// <param name="width">The base-level pixel width.</param>
    /// <param name="level">The zero-based level index.</param>
    /// <returns>The level width, at least one.</returns>
    internal static int LevelWidth(int width, int level)
    {
        ValidateLevel(level);
        return Math.Max(1, width >> level);
    }

    /// <summary>Gets one level's pixel height in a full chain.</summary>
    /// <param name="height">The base-level pixel height.</param>
    /// <param name="level">The zero-based level index.</param>
    /// <returns>The level height, at least one.</returns>
    internal static int LevelHeight(int height, int level)
    {
        ValidateLevel(level);
        return Math.Max(1, height >> level);
    }

    /// <summary>Generates the complete RGBA8 mip chain.</summary>
    /// <param name="rgba8Pixels">The base-level straight-alpha RGBA8 pixels.</param>
    /// <param name="width">The base-level pixel width.</param>
    /// <param name="height">The base-level pixel height.</param>
    /// <param name="channelType">The channel type whose values the chain preserves.</param>
    /// <returns>Every level of the chain, base level first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// The pixel length is not <c>width × height × 4</c>.
    /// </exception>
    internal static TextureLevel[] Generate(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType)
    {
        ValidateDimensions(width, height);

        if ((long)width * height > int.MaxValue / 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The RGBA8 pixel count does not fit in one array.");
        }

        long expectedLength = (long)width * height * 4;
        if (rgba8Pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"The RGBA8 pixel buffer is {rgba8Pixels.Length} bytes; expected {expectedLength} "
                    + "(width × height × 4).",
                nameof(rgba8Pixels));
        }

        byte[] basePixels = rgba8Pixels.ToArray();
        if (channelType == TextureChannelType.Rgb)
        {
            for (int alpha = 3; alpha < basePixels.Length; alpha += 4)
            {
                basePixels[alpha] = 255;
            }
        }

        int levelCount = LevelCount(width, height);
        var levels = new TextureLevel[levelCount];
        levels[0] = new TextureLevel(width, height, basePixels);

        int currentWidth = width;
        int currentHeight = height;
        for (int level = 1; level < levelCount; level++)
        {
            byte[] previous = levels[level - 1].Pixels;
            byte[] next = Downsample(
                previous,
                currentWidth,
                currentHeight,
                out int nextWidth,
                out int nextHeight);
            levels[level] = new TextureLevel(nextWidth, nextHeight, next);
            currentWidth = nextWidth;
            currentHeight = nextHeight;
        }

        return levels;
    }

    private static byte[] Downsample(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        out int nextWidth,
        out int nextHeight)
    {
        nextWidth = Math.Max(1, width / 2);
        nextHeight = Math.Max(1, height / 2);
        var destination = new byte[nextWidth * nextHeight * 4];

        for (int y = 0; y < nextHeight; y++)
        {
            int sourceY0 = y * height / nextHeight;
            int sourceY1 = (y + 1) * height / nextHeight;
            for (int x = 0; x < nextWidth; x++)
            {
                int sourceX0 = x * width / nextWidth;
                int sourceX1 = (x + 1) * width / nextWidth;
                int count = (sourceX1 - sourceX0) * (sourceY1 - sourceY0);
                int destinationOffset = ((y * nextWidth) + x) * 4;

                for (int channel = 0; channel < 4; channel++)
                {
                    int sum = 0;
                    for (int sourceY = sourceY0; sourceY < sourceY1; sourceY++)
                    {
                        int rowOffset = sourceY * width * 4;
                        for (int sourceX = sourceX0; sourceX < sourceX1; sourceX++)
                        {
                            sum += source[rowOffset + (sourceX * 4) + channel];
                        }
                    }

                    destination[destinationOffset + channel] = (byte)((sum + (count / 2)) / count);
                }
            }
        }

        return destination;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }
    }

    private static void ValidateLevel(int level)
    {
        if (level < 0 || level > 31)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "A mip level index is between 0 and 31.");
        }
    }
}
