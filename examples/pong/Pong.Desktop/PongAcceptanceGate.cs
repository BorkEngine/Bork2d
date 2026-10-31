using Bork2d.Engine.Rendering;

namespace Pong.Desktop;

/// <summary>
/// The Pong desktop acceptance gate's short-vs-long run decision and render-stat relationships
/// (M8 §10/§13).
/// </summary>
/// <remarks>
/// <para>
/// The M7 windowed/offscreen tick loop stays unpaced on purpose: a 120-tick automated run can finish
/// before the renderer has consumed the frames the asynchronous texture-page upload needs. The
/// owner-approved M8 acceptance split therefore treats runs below
/// <see cref="LongRunTickThreshold"/> completed ticks as short runs: they assert the lifecycle, tick
/// count, validation, and loaded pack/content but not the draw/statistics relationships, and print
/// the observed stats. Runs at or above the threshold are the presentation gate and assert every
/// relationship unconditionally.
/// </para>
/// <para>
/// The decision is pure and lives in this separate file so <c>Pong.Tests</c> can link it without
/// referencing the AOT host assembly. No pacing, sleep, or host-loop behavior is involved.
/// </para>
/// </remarks>
internal static class PongAcceptanceGate
{
    /// <summary>
    /// The completed-tick count at which a windowed/offscreen run becomes the long presentation gate.
    /// </summary>
    /// <remarks>
    /// 1000 ticks is far above the three frames the asynchronous page upload needs and well below the
    /// documented 20,000-tick evidence run; a short acceptance run (for example 120 ticks) is
    /// therefore never mistaken for the presentation gate.
    /// </remarks>
    internal const int LongRunTickThreshold = 1000;

    /// <summary>Returns whether a completed run is the long presentation gate.</summary>
    /// <param name="completedTicks">The number of completed ticks; never negative.</param>
    /// <returns><see langword="true"/> at or above <see cref="LongRunTickThreshold"/>.</returns>
    internal static bool IsLongRun(int completedTicks) => completedTicks >= LongRunTickThreshold;

    /// <summary>
    /// Validates the run's render stats for the short or long rule.
    /// </summary>
    /// <param name="stats">The stats re-read after the run completed.</param>
    /// <param name="completedTicks">The number of completed ticks.</param>
    /// <returns>
    /// One diagnostic per failed relationship; empty when the run satisfies its rule. Both rules
    /// require cumulative validation to be zero; only the long rule asserts the draw/statistics
    /// relationships.
    /// </returns>
    internal static IReadOnlyList<string> Validate(in RenderStats stats, int completedTicks)
    {
        var failures = new List<string>();
        if (stats.ValidationErrors != 0)
        {
            failures.Add($"validation errors must be 0; got {stats.ValidationErrors}.");
        }

        if (stats.ValidationWarnings != 0)
        {
            failures.Add($"validation warnings must be 0; got {stats.ValidationWarnings}.");
        }

        if (!IsLongRun(completedTicks))
        {
            return failures;
        }

        if (stats.Cameras < 1)
        {
            failures.Add($"at least one camera must be captured; got {stats.Cameras}.");
        }

        if (stats.NonEmptyCameras < 1)
        {
            failures.Add($"at least one camera must draw; got {stats.NonEmptyCameras}.");
        }

        if (stats.DrawCalls < 1)
        {
            failures.Add($"the frame must record at least one draw; got {stats.DrawCalls}.");
        }

        if (stats.Instances < 3)
        {
            failures.Add($"the three Pong sprites must be instanced; got {stats.Instances}.");
        }

        if (stats.TexturePages < 3)
        {
            failures.Add(
                $"the placeholder and the two Pong texture pages must be resident; got "
                    + $"{stats.TexturePages}.");
        }

        if (stats.DrawCalls != stats.NonEmptyCameras)
        {
            failures.Add(
                $"DrawCalls must equal NonEmptyCameras; got {stats.DrawCalls} and "
                    + $"{stats.NonEmptyCameras}.");
        }

        if (stats.DrawCalls > stats.Cameras)
        {
            failures.Add(
                $"DrawCalls must never exceed Cameras; got {stats.DrawCalls} and {stats.Cameras}.");
        }

        return failures;
    }
}
