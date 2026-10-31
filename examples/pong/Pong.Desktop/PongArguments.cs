using System.Globalization;

namespace Pong.Desktop;

/// <summary>
/// The parsed command-line arguments of the Pong desktop host.
/// </summary>
/// <remarks>
/// The default invocation (no arguments) and <c>--windowed</c> run the interactive windowed
/// example. <c>--ticks N</c> is valid with <c>--windowed</c>, <c>--offscreen</c>, and
/// <c>--headless</c>: with <c>--windowed</c> it turns the run into the automated acceptance run that
/// exits after exactly <c>N</c> completed ticks; <c>--offscreen</c> requires it; and with
/// <c>--headless</c> it overrides the default 120-tick acceptance run. The three modes are mutually
/// exclusive. Parsing is strict: an unknown, duplicated, or malformed argument prints the usage and
/// exits with code 2.
/// </remarks>
internal sealed class PongArguments
{
    /// <summary>The number of ticks a <c>--headless</c> run completes when <c>--ticks</c> is omitted.</summary>
    internal const int DefaultHeadlessTickLimit = 120;

    private PongArguments(bool headless, bool windowed, bool offscreen, int? tickLimit)
    {
        Headless = headless;
        Windowed = windowed;
        Offscreen = offscreen;
        TickLimit = tickLimit;
    }

    /// <summary>Gets a value indicating whether the headless acceptance run was selected.</summary>
    internal bool Headless { get; }

    /// <summary>Gets a value indicating whether windowed mode was selected explicitly.</summary>
    internal bool Windowed { get; }

    /// <summary>Gets a value indicating whether the non-interactive offscreen run was selected.</summary>
    internal bool Offscreen { get; }

    /// <summary>
    /// Gets the automated tick limit: the explicit <c>--ticks N</c> value or, for <c>--headless</c>,
    /// <see cref="DefaultHeadlessTickLimit"/>; <see langword="null"/> for an interactive windowed run.
    /// </summary>
    internal int? TickLimit { get; }

    /// <summary>Parses the command-line arguments.</summary>
    /// <param name="args">The arguments passed to <c>Main</c>; never <see langword="null"/>.</param>
    /// <param name="arguments">The parsed arguments when parsing succeeds.</param>
    /// <param name="error">The diagnostic when parsing fails; otherwise empty.</param>
    /// <returns><see langword="true"/> when the arguments are valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    internal static bool TryParse(
        string[] args,
        out PongArguments arguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        arguments = new PongArguments(headless: false, windowed: false, offscreen: false, tickLimit: null);
        error = string.Empty;
        var headless = false;
        var windowed = false;
        var offscreen = false;
        int? tickLimit = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless":
                    if (headless)
                    {
                        error = "--headless may be specified only once.";
                        return false;
                    }

                    headless = true;
                    break;

                case "--windowed":
                    if (windowed)
                    {
                        error = "--windowed may be specified only once.";
                        return false;
                    }

                    windowed = true;
                    break;

                case "--offscreen":
                    if (offscreen)
                    {
                        error = "--offscreen may be specified only once.";
                        return false;
                    }

                    offscreen = true;
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

        if (headless && windowed)
        {
            error = "--headless and --windowed are mutually exclusive.";
            return false;
        }

        if (headless && offscreen)
        {
            error = "--headless and --offscreen are mutually exclusive.";
            return false;
        }

        if (windowed && offscreen)
        {
            error = "--windowed and --offscreen are mutually exclusive.";
            return false;
        }

        if (offscreen && tickLimit is null)
        {
            error = "--offscreen requires --ticks N.";
            return false;
        }

        if (tickLimit is not null && !windowed && !offscreen && !headless)
        {
            error = "--ticks requires --windowed, --offscreen, or --headless.";
            return false;
        }

        if (tickLimit is null && headless)
        {
            tickLimit = DefaultHeadlessTickLimit;
        }

        arguments = new PongArguments(headless, windowed, offscreen, tickLimit);
        return true;
    }
}
