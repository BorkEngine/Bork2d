using Bork2d.Engine;

namespace Bork2d.Render.Textures;

/// <summary>
/// The pure bindless texture-page capacity policy (M8 §6.5).
/// </summary>
/// <remarks>
/// <para>
/// The table starts at 256 entries: index <c>0</c> is the placeholder and <c>1..255</c> are real
/// pages. When a new real page needs an index and the table is full, the capacity doubles, up to the
/// effective device limit. The effective limit is the smaller of
/// <c>maxPerStageDescriptorUpdateAfterBindSampledImages</c> and
/// <c>maxDescriptorSetUpdateAfterBindSampledImages</c>, because the bindless binding must satisfy
/// both and the layout maximum is that same value.
/// </para>
/// <para>
/// Reaching the limit is an explicit <see cref="Bork2dException"/> naming the limit and the
/// requested capacity; no page is silently dropped.
/// </para>
/// </remarks>
internal static class TexturePageCapacity
{
    /// <summary>The placeholder's bindless index.</summary>
    internal const int PlaceholderIndex = 0;

    /// <summary>The initial total capacity, including the placeholder.</summary>
    internal const int Initial = 256;

    /// <summary>The number of real (non-placeholder) pages the initial capacity holds.</summary>
    internal const int InitialRealPages = Initial - 1;

    /// <summary>
    /// The documented implementation cap for the page capacity and the bindless layout maximum.
    /// </summary>
    /// <remarks>
    /// A driver may report the update-after-bind sampled-image limits as <c>UINT32_MAX</c>
    /// ("unbounded"). The renderer's capacity is a signed 32-bit count and no descriptor set that
    /// large is allocatable, so an unbounded device limit saturates to this documented cap instead
    /// of overflowing. The value matches the per-stage update-after-bind sampled-image count real
    /// Vulkan devices commonly report (1,048,576) and is far above any realistic page count; growth
    /// beyond it fails explicitly, and an allocation failure below it is a fatal
    /// <see cref="Bork2dException"/>.
    /// </remarks>
    internal const int DocumentedMaximum = 1 << 20;

    /// <summary>
    /// Computes the effective page limit from the two update-after-bind sampled-image limits.
    /// </summary>
    /// <param name="maxPerStageUpdateAfterBindSampledImages">
    /// The device's per-stage update-after-bind sampled-image limit.
    /// </param>
    /// <param name="maxDescriptorSetUpdateAfterBindSampledImages">
    /// The device's per-set update-after-bind sampled-image limit.
    /// </param>
    /// <returns>The smaller of the two limits, saturated to <see cref="DocumentedMaximum"/>.</returns>
    internal static int EffectiveLimit(
        uint maxPerStageUpdateAfterBindSampledImages,
        uint maxDescriptorSetUpdateAfterBindSampledImages)
    {
        uint smaller = Math.Min(
            maxPerStageUpdateAfterBindSampledImages,
            maxDescriptorSetUpdateAfterBindSampledImages);
        return smaller > DocumentedMaximum ? DocumentedMaximum : (int)smaller;
    }

    /// <summary>
    /// Validates that the effective limit can hold the initial capacity.
    /// </summary>
    /// <param name="effectiveLimit">The effective device limit, including the placeholder.</param>
    /// <exception cref="Bork2dException">The limit is below <see cref="Initial"/>.</exception>
    internal static void EnsureSupportsInitialCapacity(int effectiveLimit)
    {
        if (effectiveLimit >= Initial)
        {
            return;
        }

        throw new Bork2dException(
            $"The selected Vulkan device cannot support the sprite bindless texture pages: the "
                + "effective update-after-bind sampled-image limit is "
                + $"{effectiveLimit} entries, but the renderer's initial page capacity is "
                + $"{Initial} (the placeholder plus {InitialRealPages} real pages). "
                + "maxPerStageDescriptorUpdateAfterBindSampledImages and "
                + "maxDescriptorSetUpdateAfterBindSampledImages must both be at least "
                + $"{Initial}.");
    }

    /// <summary>
    /// Doubles a capacity without exceeding <paramref name="limit"/>.
    /// </summary>
    /// <param name="current">The current total capacity.</param>
    /// <param name="limit">The effective device limit.</param>
    /// <param name="grown">The new total capacity when growth is possible.</param>
    /// <returns><see langword="true"/> when a larger capacity is available.</returns>
    internal static bool TryGrow(int current, int limit, out int grown)
    {
        if (current >= limit)
        {
            grown = current;
            return false;
        }

        long doubled = (long)current * 2;
        grown = (int)Math.Min(doubled, limit);
        return grown > current;
    }

    /// <summary>
    /// Creates the explicit failure for a table that cannot grow any further.
    /// </summary>
    /// <param name="capacity">The current total capacity.</param>
    /// <param name="limit">The effective device limit.</param>
    /// <param name="requested">The index the next real page would need.</param>
    /// <returns>The fatal renderer exception.</returns>
    internal static Bork2dException CreateLimitException(int capacity, int limit, int requested)
        => new(
            $"The sprite texture page capacity of {capacity} entries reached the device bindless "
                + $"limit of {limit} entries (the placeholder plus {limit - 1} real pages); the "
                + $"renderer would need entry {requested} for the next page. The limit is the "
                + "smaller of maxPerStageDescriptorUpdateAfterBindSampledImages and "
                + "maxDescriptorSetUpdateAfterBindSampledImages. No page is dropped silently.");
}
