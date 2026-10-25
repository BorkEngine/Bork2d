using System.Numerics;
using Bork2d.Api;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime;

/// <summary>
/// The internal <see cref="IWindow"/> implementation the runtime host attaches to a live
/// <see cref="SdlWindowPlatform"/>.
/// </summary>
/// <remarks>
/// <para>
/// The proxy is created with the engine (before any window exists) and attached by the windowed
/// host before game <c>Setup</c>. In the unattached state its members throw
/// <see cref="NotSupportedException"/>; that state is not reachable through the public API. After
/// <see cref="Detach"/> (once the platform has shut down) every member throws
/// <see cref="ObjectDisposedException"/> so retained references behave per D-M2-17.
/// </para>
/// <para>
/// Setters validate their arguments here and then only enqueue a platform command; they never
/// block on SDL. Getters copy values from the platform's latest immutable confirmed snapshot, so
/// they never block and may lag one event pump. The proxy's own lock protects only the
/// platform/detached fields and is never held while a platform command is enqueued.
/// </para>
/// </remarks>
internal sealed class WindowProxy : IWindow
{
    private readonly Lock _gate = new();
    private SdlWindowPlatform? _platform;
    private bool _detached;

    /// <summary>Attaches the proxy to the started windowed platform.</summary>
    /// <param name="platform">The live platform whose window this proxy controls.</param>
    /// <exception cref="InvalidOperationException">The proxy is already attached.</exception>
    /// <exception cref="ObjectDisposedException">The proxy was already detached.</exception>
    internal void Attach(SdlWindowPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            if (_platform is not null)
            {
                throw new InvalidOperationException("The window proxy is already attached.");
            }

            _platform = platform;
        }
    }

    /// <summary>Detaches the proxy and marks it disposed. Idempotent.</summary>
    internal void Detach()
    {
        lock (_gate)
        {
            if (_detached)
            {
                return;
            }

            _detached = true;
            _platform = null;
        }
    }

    /// <inheritdoc/>
    public string Title
    {
        get => RequirePlatform().State.Title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            RequirePlatform().SetTitle(value);
        }
    }

    /// <inheritdoc/>
    public Vector2 Size
    {
        get
        {
            var size = RequirePlatform().State.FramebufferSize;
            return new Vector2(size.Width, size.Height);
        }

        set
        {
            var platform = RequirePlatform();
            if (!float.IsFinite(value.X)
                || !float.IsFinite(value.Y)
                || value.X <= 0f
                || value.Y <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The window size must be finite and greater than zero in both components.");
            }

            if (platform.State.Fullscreen)
            {
                platform.Logger.LogDebug(
                    "Bork2d IWindow.Size ignored while fullscreen: requested={Width}x{Height}",
                    value.X,
                    value.Y);
                return;
            }

            platform.SetSize(value.X, value.Y);
        }
    }

    /// <inheritdoc/>
    public bool Fullscreen
    {
        get => RequirePlatform().State.Fullscreen;
        set => RequirePlatform().SetFullscreen(value);
    }

    /// <inheritdoc/>
    public bool CursorVisible
    {
        get => RequirePlatform().State.CursorVisible;
        set => RequirePlatform().SetCursorVisible(value);
    }

    /// <inheritdoc/>
    public bool IsMinimized => RequirePlatform().State.Minimized;

    private SdlWindowPlatform RequirePlatform()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_detached, this);
            return _platform
                ?? throw new NotSupportedException(
                    "The window proxy is not attached to a live window. Window members are "
                    + "available only during a windowed Run().");
        }
    }
}
