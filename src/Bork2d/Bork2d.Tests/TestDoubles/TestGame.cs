using Bork2d.Engine;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A single-tick game test double: it records the callbacks and requests <see cref="IEngine{TLayer}.Exit"/>
/// from its first <see cref="Tick"/>.
/// </summary>
/// <remarks>
/// M7 runs headless (and windowed/offscreen) until <c>Exit</c> (Q11=K1), so a test double must stop
/// the loop to keep single-tick tests valid. Use <see cref="ScriptedGame{TLayer}"/> for multi-tick
/// cases and explicit control over when the loop stops.
/// </remarks>
/// <typeparam name="TLayer">The game's render layer flags.</typeparam>
internal sealed class TestGame<TLayer> : IGame<TLayer>
    where TLayer : struct, Enum
{
    private IEngine<TLayer>? _engine;

    public int SetupCount { get; private set; }

    public int TickCount { get; private set; }

    public int TeardownCount { get; private set; }

    public void Setup(IEngine<TLayer> engine)
    {
        SetupCount++;
        _engine = engine;
    }

    public void Tick()
    {
        TickCount++;
        _engine?.Exit();
    }

    public void Teardown() => TeardownCount++;
}
