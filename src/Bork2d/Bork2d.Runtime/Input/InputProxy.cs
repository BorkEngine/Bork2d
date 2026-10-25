using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Runtime;

/// <summary>
/// The internal <see cref="IInput"/> implementation the runtime host attaches to a live
/// <see cref="SdlWindowPlatform"/>.
/// </summary>
/// <remarks>
/// <para>
/// The proxy is created with the engine (before any window exists) and attached by the windowed host
/// before game <c>Setup</c>. In the unattached state its members throw
/// <see cref="NotSupportedException"/>; that state is not reachable through the public API. After
/// <see cref="Detach"/> (once the platform has shut down) every member throws
/// <see cref="ObjectDisposedException"/> so retained references behave per D-M2-17.
/// </para>
/// <para>
/// Reads return the last snapshot published by <see cref="Freeze"/>; before the first freeze that is
/// a preallocated empty snapshot, and during <c>Teardown</c> it is the most recently frozen one. The
/// snapshot reference is replaced with a volatile write, so reads during a tick are stable and see
/// only complete state. Events that arrive after the freeze belong to the next snapshot.
/// </para>
/// <para>
/// <see cref="Freeze"/> and <see cref="DispatchText"/> are the tick-thread handoff for the runtime
/// host: freeze publishes the next snapshot, and dispatch raises <see cref="OnTextTyped"/> for the
/// frozen characters in order before <c>Tick</c>. Handler exceptions propagate; the proxy never
/// swallows them. Adding or removing a handler is thread-safe, and no lock is held while a handler
/// runs.
/// </para>
/// </remarks>
internal sealed class InputProxy : IInput
{
    private readonly Lock _gate = new();
    private readonly InputSnapshot _emptySnapshot = new();
    private readonly InputSnapshot _snapshotA = new();
    private readonly InputSnapshot _snapshotB = new();

    private SdlWindowPlatform? _platform;
    private InputSnapshot _published;
    private Action<char>? _textHandlers;
    private bool _detached;
    private bool _useSecondSnapshot;

    /// <summary>Initializes a new instance of the <see cref="InputProxy"/> class.</summary>
    internal InputProxy()
    {
        _published = _emptySnapshot;
    }

    /// <summary>Raised for each frozen text character on the tick thread.</summary>
    /// <remarks>
    /// Subscription is checked against the proxy lifecycle and is thread-safe. A handler exception
    /// propagates out of <see cref="DispatchText"/> and stops further dispatch for that frame.
    /// </remarks>
    public event Action<char> OnTextTyped
    {
        add
        {
            lock (_gate)
            {
                ThrowIfUnavailable();
                Volatile.Write(ref _textHandlers, _textHandlers + value);
            }
        }

        remove
        {
            lock (_gate)
            {
                ThrowIfUnavailable();
                Volatile.Write(ref _textHandlers, _textHandlers - value);
            }
        }
    }

    /// <summary>Attaches the proxy to the started windowed platform.</summary>
    /// <param name="platform">The live platform whose input this proxy exposes.</param>
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
                throw new InvalidOperationException("The input proxy is already attached.");
            }

            Volatile.Write(ref _platform, platform);
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

            Volatile.Write(ref _detached, true);
            Volatile.Write(ref _platform, null);
        }
    }

    /// <summary>Freezes the platform input into the next published snapshot.</summary>
    /// <remarks>
    /// Called once per tick by the runtime host on the tick thread. Allocates nothing when no text is
    /// pending; the two snapshot instances are filled alternately and published by reference.
    /// </remarks>
    internal void Freeze()
    {
        var platform = RequirePlatform();
        var target = _useSecondSnapshot ? _snapshotB : _snapshotA;
        _useSecondSnapshot = !_useSecondSnapshot;
        platform.FreezeInput(target);
        Volatile.Write(ref _published, target);
    }

    /// <summary>Dispatches the text captured by the last <see cref="Freeze"/> in order.</summary>
    /// <remarks>
    /// Called on the tick thread after the freeze and before <c>Tick</c>. The pending characters are
    /// cleared before the first handler runs, so an exception stops this frame's dispatch without
    /// replaying characters on a later call.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The proxy was detached.</exception>
    /// <exception cref="NotSupportedException">The proxy is not attached.</exception>
    internal void DispatchText()
    {
        RequirePlatform();
        var snapshot = Volatile.Read(ref _published);
        var count = snapshot.TextLength;
        snapshot.TextLength = 0;
        if (count == 0)
        {
            return;
        }

        var handlers = Volatile.Read(ref _textHandlers);
        if (handlers is null)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            handlers(snapshot.Text[i]);
        }
    }

    /// <inheritdoc/>
    public bool IsKeyPressed(KeyCode key) => GetSnapshot().IsKeyPressed(key);

    /// <inheritdoc/>
    public bool IsKeyReleased(KeyCode key) => GetSnapshot().IsKeyReleased(key);

    /// <inheritdoc/>
    public bool IsKeyDown(KeyCode key) => GetSnapshot().IsKeyDown(key);

    /// <inheritdoc/>
    public Vector2 MousePosition => GetSnapshot().Position;

    /// <inheritdoc/>
    public Vector2 MouseDelta => GetSnapshot().Delta;

    /// <inheritdoc/>
    public float MouseWheelDelta => GetSnapshot().Wheel;

    /// <inheritdoc/>
    public bool IsMousePressed(MouseButton button) => GetSnapshot().IsMousePressed(button);

    /// <inheritdoc/>
    public bool IsMouseReleased(MouseButton button) => GetSnapshot().IsMouseReleased(button);

    /// <inheritdoc/>
    public bool IsMouseDown(MouseButton button) => GetSnapshot().IsMouseDown(button);

    private InputSnapshot GetSnapshot()
    {
        ThrowIfUnavailable();
        return Volatile.Read(ref _published);
    }

    private SdlWindowPlatform RequirePlatform()
    {
        var platform = Volatile.Read(ref _platform);
        if (Volatile.Read(ref _detached))
        {
            throw new ObjectDisposedException(nameof(InputProxy));
        }

        return platform
            ?? throw new NotSupportedException(
                "The input proxy is not attached to a live window. Input is available only during "
                + "a windowed Run().");
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _detached), this);
        if (Volatile.Read(ref _platform) is null)
        {
            throw new NotSupportedException(
                "The input proxy is not attached to a live window. Input is available only during "
                + "a windowed Run().");
        }
    }
}
