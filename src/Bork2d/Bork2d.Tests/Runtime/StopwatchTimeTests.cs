using System.Diagnostics;
using Bork2d.Engine.Time;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Deterministic tests for the windowed clock: the zero first frame, raw monotonic time, the delta
/// clamp, and lock-protected reads that never tear.
/// </summary>
public sealed class StopwatchTimeTests
{
    private const double MaxDelta = 0.25;

    [Fact]
    public void FirstFramePublishesZeroTimeAndDelta()
    {
        var time = new StopwatchTime(MaxDelta);

        time.NewFrame();

        Assert.Equal(0d, time.Time);
        Assert.Equal(0d, time.DeltaTime);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-0.25d)]
    [InlineData(double.NaN)]
    public void InvalidMaximumDeltaIsRejected(double maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StopwatchTime(maximum));
    }

    [Fact]
    public void PositiveInfinityDisablesTheClamp()
    {
        var time = new StopwatchTime(double.PositiveInfinity);

        time.NewFrame();
        SpinFor(TimeSpan.FromMilliseconds(2));
        time.NewFrame();

        Assert.True(time.DeltaTime > 0d);
        Assert.True(double.IsFinite(time.DeltaTime));
    }

    [Fact]
    public void TimeIsRawMonotonicWhileDeltaIsClamped()
    {
        const double clamp = 0.001;
        var time = new StopwatchTime(clamp);

        time.NewFrame();
        SpinFor(TimeSpan.FromMilliseconds(20));
        time.NewFrame();

        // Raw elapsed time far exceeds the clamp and must not be clamped...
        Assert.True(time.Time >= 0.010, $"Time should stay raw; was {time.Time}.");
        // ...while the gameplay delta is exactly the clamp.
        Assert.Equal(clamp, time.DeltaTime);
    }

    [Fact]
    public void TimeNeverDecreasesAndDeltaStaysWithinTheClamp()
    {
        var time = new StopwatchTime(MaxDelta);
        time.NewFrame();
        var previousTime = time.Time;
        var previousDelta = time.DeltaTime;

        for (var frame = 0; frame < 5; frame++)
        {
            SpinFor(TimeSpan.FromMilliseconds(2));
            time.NewFrame();

            Assert.True(time.Time >= previousTime);
            Assert.InRange(time.DeltaTime, 0d, MaxDelta);
            previousTime = time.Time;
            previousDelta = time.DeltaTime;
        }

        Assert.True(previousTime > 0d);
        Assert.True(previousDelta > 0d);
    }

    [Fact]
    public void ConcurrentReadsSeeFiniteMonotonicSnapshots()
    {
        var time = new StopwatchTime(MaxDelta);
        time.NewFrame();
        Exception? failure = null;
        var stop = 0;

        var writer = new Thread(() =>
        {
            try
            {
                for (var frame = 0; frame < 20_000; frame++)
                {
                    time.NewFrame();
                }
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref failure, exception, null);
            }
            finally
            {
                Volatile.Write(ref stop, 1);
            }
        })
        {
            IsBackground = true,
            Name = "Bork2d.Tests.TimeWriter",
        };

        Thread Reader(string name)
        {
            var thread = new Thread(() =>
            {
                var previous = 0d;
                while (Volatile.Read(ref stop) == 0)
                {
                    var current = time.Time;
                    var delta = time.DeltaTime;
                    if (!double.IsFinite(current)
                        || current < previous
                        || !double.IsFinite(delta)
                        || delta < 0d
                        || delta > MaxDelta)
                    {
                        Interlocked.CompareExchange(
                            ref failure,
                            new InvalidOperationException(
                                $"Torn time read: time={current}, delta={delta}, "
                                + $"previous={previous}."),
                            null);
                        return;
                    }

                    previous = current;
                }
            })
            {
                IsBackground = true,
                Name = name,
            };

            return thread;
        }

        var readers = new[] { Reader("Bork2d.Tests.TimeReaderA"), Reader("Bork2d.Tests.TimeReaderB") };

        writer.Start();
        foreach (var reader in readers)
        {
            reader.Start();
        }

        Assert.True(writer.Join(TimeSpan.FromSeconds(20)), "The time writer did not finish.");
        foreach (var reader in readers)
        {
            Assert.True(reader.Join(TimeSpan.FromSeconds(20)), "A time reader did not finish.");
        }

        Assert.Null(failure);
    }

    private static void SpinFor(TimeSpan duration)
    {
        // Test-only busy wait: the clock must advance without Thread.Sleep, and the margin is far
        // larger than the clamp under test, so the assertions cannot be timing-flaky.
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
        }
    }
}
