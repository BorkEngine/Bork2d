using System.Globalization;

namespace Bork2d.AotSmoke;

/// <summary>
/// The parsed command-line arguments of the NativeAOT smoke app.
/// </summary>
/// <remarks>
/// The default invocation has no arguments and runs the one-tick headless smoke. The windowed demo
/// is opt-in with <c>--windowed</c>; <c>--ticks N</c> is valid only together with it and turns the
/// interactive demo into an automated run that exits after exactly <c>N</c> completed ticks.
/// Parsing is strict on purpose: an unknown, duplicated, or malformed argument prints the usage and
/// exits with code 2 instead of silently changing the mode.
/// </remarks>
internal sealed class DemoArguments
{
    private DemoArguments(bool windowed, int? tickLimit)
    {
        Windowed = windowed;
        TickLimit = tickLimit;
    }

    /// <summary>Gets a value indicating whether the windowed demo was selected.</summary>
    internal bool Windowed { get; }

    /// <summary>Gets the automated tick limit, or <see langword="null"/> for an interactive run.</summary>
    internal int? TickLimit { get; }

    /// <summary>Parses the command-line arguments.</summary>
    /// <param name="args">The arguments passed to <c>Main</c>; never <see langword="null"/>.</param>
    /// <param name="arguments">The parsed arguments when parsing succeeds.</param>
    /// <param name="error">The diagnostic when parsing fails; otherwise empty.</param>
    /// <returns><see langword="true"/> when the arguments are valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    internal static bool TryParse(
        string[] args,
        out DemoArguments arguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        arguments = new DemoArguments(windowed: false, tickLimit: null);
        error = string.Empty;
        var windowed = false;
        int? tickLimit = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--windowed":
                    if (windowed)
                    {
                        error = "--windowed may be specified only once.";
                        return false;
                    }

                    windowed = true;
                    break;

                case "--ticks":
                    if (tickLimit is not null)
                    {
                        error = "--ticks may be specified only once.";
                        return false;
                    }

                    if (i + 1 >= args.Length)
                    {
                        error = "--ticks requires a tick count.";
                        return false;
                    }

                    i++;
                    if (!int.TryParse(
                            args[i],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var parsed)
                        || parsed < 1)
                    {
                        error = $"--ticks requires an integer >= 1; got '{args[i]}'.";
                        return false;
                    }

                    tickLimit = parsed;
                    break;

                default:
                    error = $"unknown argument '{args[i]}'.";
                    return false;
            }
        }

        if (tickLimit is not null && !windowed)
        {
            error = "--ticks requires --windowed.";
            return false;
        }

        arguments = new DemoArguments(windowed, tickLimit);
        return true;
    }
}
