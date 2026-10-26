using Bork2d.Api;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime;

/// <summary>
/// The non-generic runtime owned by <see cref="Bork2dApp"/>.
/// </summary>
/// <remarks>
/// This is the type-erasure seam that keeps <see cref="Bork2dApp"/> non-generic while the
/// generic engine and game stay internal to <see cref="AppRuntime{TLayer}"/>.
/// </remarks>
internal interface IAppRuntime : IDisposable
{
    /// <summary>Gets the engine-owned scene root.</summary>
    ISceneRoot Root { get; }

    /// <summary>Gets the logger factory owned by the app.</summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>Gets the logging configuration stored for WP5.</summary>
    Action<ILoggingBuilder>? ConfigureLogging { get; }

    /// <summary>Runs the configured windowed loop or the M1 one-tick sequence.</summary>
    void Run();
}
