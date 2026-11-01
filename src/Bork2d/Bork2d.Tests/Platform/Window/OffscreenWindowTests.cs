using System.Numerics;
using Bork2d.Runtime.Window;
using Xunit;

namespace Bork2d.Tests.Platform.Window;

/// <summary>
/// The offscreen <see cref="IWindow"/> facade reports the fixed target extent and stores inert
/// window state without throwing.
/// </summary>
public sealed class OffscreenWindowTests
{
    [Fact]
    public void SizeIsTheFixedTargetExtentAndTheSetterIsANoOp()
    {
        var window = new OffscreenWindow(320, 240, "offscreen");

        Assert.Equal(new Vector2(320f, 240f), window.Size);

        window.Size = new Vector2(800f, 600f);

        Assert.Equal(new Vector2(320f, 240f), window.Size);
    }

    [Fact]
    public void InertStateRoundTripsAndANullTitleDoesNotThrow()
    {
        var window = new OffscreenWindow(320, 240, "offscreen");

        Assert.Equal("offscreen", window.Title);
        window.Title = "changed";
        Assert.Equal("changed", window.Title);
        window.Title = null!;
        Assert.Equal(string.Empty, window.Title);

        Assert.False(window.Fullscreen);
        window.Fullscreen = true;
        Assert.True(window.Fullscreen);

        Assert.True(window.CursorVisible);
        window.CursorVisible = false;
        Assert.False(window.CursorVisible);

        Assert.False(window.IsMinimized);
    }

    [Fact]
    public void ConcurrentReadsAndWritesDoNotThrowOrTear()
    {
        const int iterations = 2_000;
        string[] titles = ["offscreen-a", "offscreen-b"];
        var window = new OffscreenWindow(320, 240, titles[0]);
        var errors = new List<Exception>();
        var threads = new Thread[6];

        for (var index = 0; index < threads.Length; index++)
        {
            bool writer = index < 3;
            int worker = index % 3;
            threads[index] = new Thread(() =>
            {
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    try
                    {
                        if (writer)
                        {
                            window.Title = titles[(worker + iteration) % titles.Length];
                            window.Fullscreen = (worker + iteration) % 2 == 0;
                            window.CursorVisible = (worker + iteration) % 2 != 0;
                            window.Size = new Vector2(iteration, iteration);
                            continue;
                        }

                        string title = window.Title;
                        if (!ReferenceEquals(title, titles[0])
                            && !ReferenceEquals(title, titles[1]))
                        {
                            throw new InvalidOperationException(
                                $"The title reference tore: '{title}'.");
                        }

                        _ = window.Fullscreen;
                        _ = window.CursorVisible;
                        if (window.IsMinimized)
                        {
                            throw new InvalidOperationException(
                                "The offscreen window must never be minimized.");
                        }

                        if (window.Size != new Vector2(320f, 240f))
                        {
                            throw new InvalidOperationException(
                                $"The fixed extent changed: {window.Size}.");
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (errors)
                        {
                            errors.Add(exception);
                        }
                    }
                }
            });
        }

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.Empty(errors);
    }
}
