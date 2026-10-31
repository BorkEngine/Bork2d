using Bork2d.Engine.Time;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Direct tests for the internal synthesized headless clock (Q12=L2, F5, F7): exact values, the
/// zero first tick, argument validation, and thread-safe reads of values that are never torn.
/// </summary>
public sealed class SynthesizedTimeTests
{
    [Fact]
    public void TickZeroIsZeroAndLaterTicksUseTheExactStep()
    {
        var time = new SynthesizedTime();

        time.SetTick(0, 60);
        Assert.Equal(0d, time.Time);
        Assert.Equal(0d, time.DeltaTime);

        time.SetTick(1, 60);
        Assert.Equal(1d / 60d, time.Time, precision: 12);
        Assert.Equal(1d / 60d, time.DeltaTime, precision: 12);

        time.SetTick(120, 60);
        Assert.Equal(2d, time.Time, precision: 12);
        Assert.Equal(1d / 60d, time.DeltaTime, precision: 12);
    }

    [Fact]
    public void TheStepIsExactBelowFourHertzAndHasNoClamp()
    {
        var time = new SynthesizedTime();

        time.SetTick(0, 3);
        Assert.Equal(0d, time.Time);
        Assert.Equal(0d, time.DeltaTime);

        time.SetTick(1, 3);
        Assert.Equal(1d / 3d, time.Time, precision: 12);
        Assert.Equal(1d / 3d, time.DeltaTime, precision: 12);
    }

    [Fact]
    public void InvalidTickOrRateThrows()
    {
        var time = new SynthesizedTime();

        Assert.Throws<ArgumentOutOfRangeException>(() => time.SetTick(-1, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => time.SetTick(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => time.SetTick(0, -60));
    }

    [Fact]
    public void ConcurrentReadsNeverObserveANonSynthesizedValue()
    {
        const int rate = 60;
        var time = new SynthesizedTime();
        time.SetTick(1, rate);

        var writer = new Thread(() =>
        {
            for (var tick = 1; tick <= 100_000; tick++)
            {
                time.SetTick(tick, rate);
            }
        })
        {
            IsBackground = true,
            Name = "Bork2d.Tests.SynthesizedTimeWriter",
        };

        writer.Start();
        try
        {
            while (writer.IsAlive)
            {
                double t = time.Time;
                double delta = time.DeltaTime;

                Assert.True(double.IsFinite(t) && t >= 0d);
                Assert.True(double.IsFinite(delta) && delta >= 0d);
                Assert.True(
                    delta == 0d || Math.Abs(delta - (1d / rate)) < 1e-12,
                    $"DeltaTime was {delta:R}, which is not 0 or the synthesized step.");
                double steps = t * rate;
                Assert.True(
                    Math.Abs(steps - Math.Round(steps)) < 1e-6,
                    $"Time was {t:R}, which is not a whole number of synthesized steps.");

                // Yield so the writer makes progress without the reader spinning at full speed.
                Thread.Yield();
            }
        }
        finally
        {
            writer.Join(TimeSpan.FromSeconds(20));
        }

        Assert.False(writer.IsAlive, "The synthesized-time writer did not finish.");
    }
}
