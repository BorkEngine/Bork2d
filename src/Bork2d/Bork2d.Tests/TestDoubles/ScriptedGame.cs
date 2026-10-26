using Bork2d.Api;
using Bork2d.Common.Scene;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A game whose callbacks can capture the engine and throw on demand.
/// </summary>
internal sealed class ScriptedGame<TLayer> : IGame<TLayer> where TLayer : struct, Enum
{
    public int SetupCount { get; private set; }

    public int TickCount { get; private set; }

    public int TeardownCount { get; private set; }

    public IEngine<TLayer>? Engine { get; private set; }

    public Action<IEngine<TLayer>>? OnSetup { get; set; }

    public Action? OnTick { get; set; }

    public Action? OnTeardown { get; set; }

    public void Setup(IEngine<TLayer> engine)
    {
        SetupCount++;
        Engine = engine;
        OnSetup?.Invoke(engine);
    }

    public void Tick()
    {
        TickCount++;
        OnTick?.Invoke();
    }

    public void Teardown()
    {
        TeardownCount++;
        OnTeardown?.Invoke();
    }
}
