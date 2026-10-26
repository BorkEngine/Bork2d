using System.Diagnostics;

namespace Bork2d.Tests.Bake;

/// <summary>
/// Runs an external tool (the SDK) for a fixture build with a bounded timeout and captured output.
/// </summary>
/// <remarks>
/// Fixture libraries and the generated host are built from sources written into unique temporary
/// directories; no binary fixture is committed. The timeout keeps a hung restore/build from hanging
/// the test pass, and the captured output makes a build failure diagnosable from the test report.
/// </remarks>
internal static class TempProcessRunner
{
    /// <summary>Runs a process and returns its exit code and combined output.</summary>
    /// <param name="fileName">The executable name or path.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="workingDirectory">The process working directory.</param>
    /// <param name="timeoutMilliseconds">The maximum run time.</param>
    /// <returns>The exit code and combined standard output/error.</returns>
    /// <exception cref="TimeoutException">The process did not exit in time and was killed.</exception>
    internal static Result Run(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        int timeoutMilliseconds = 300_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'{fileName} {string.Join(' ', arguments)}' did not exit within "
                    + $"{timeoutMilliseconds} ms.");
        }

        string combined = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
        return new Result(process.ExitCode, combined);
    }

    /// <summary>The exit code and combined output of a finished process.</summary>
    /// <param name="ExitCode">The process exit code.</param>
    /// <param name="Output">The combined standard output and standard error.</param>
    internal sealed record Result(int ExitCode, string Output);
}
