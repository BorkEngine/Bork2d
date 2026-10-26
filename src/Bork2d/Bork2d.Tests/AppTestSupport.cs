using System.Runtime.ExceptionServices;
using Bork2d.Runtime;

namespace Bork2d.Tests;

/// <summary>
/// Runs a built app on a bounded background thread so a loop bug fails a test instead of hanging
/// the test run, and so exceptions thrown on the run thread are observed instead of crashing the
/// process.
/// </summary>
internal static class AppTestSupport
{
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Runs the app on a bounded background thread and returns any failure.</summary>
    /// <param name="app">The app to run.</param>
    /// <returns>The exception the run threw, or <see langword="null"/> on a normal return.</returns>
    /// <exception cref="TimeoutException">The run did not finish within the bounded wait.</exception>
    internal static Exception? RunCaptured(Bork2dApp app)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                app.Run();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "Bork2d.Tests.AppRun",
        };

        thread.Start();
        if (!thread.Join(_joinTimeout))
        {
            throw new TimeoutException(
                $"The app run did not finish within {_joinTimeout.TotalSeconds:0} s.");
        }

        return failure;
    }

    /// <summary>Runs the app and rethrows any failure with its original stack.</summary>
    /// <param name="app">The app to run.</param>
    internal static void RunToCompletion(Bork2dApp app)
    {
        var failure = RunCaptured(app);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
