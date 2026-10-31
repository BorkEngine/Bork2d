using Bork2d.Engine;
using Bork2d.Engine.Scene;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A game whose callbacks can capture the engine and throw on demand.
/// </summary>
/// <remarks>
/// <see cref="ExitAfterFirstTick"/> keeps single-tick tests valid under the M7 loops, which run
/// until <see cref="IEngine{TLayer}.Exit"/> (Q11=K1); multi-tick tests leave it off and call
/// <c>Exit</c> explicitly.
/// </remarks>
internal sealed class ScriptedGame<TLayer> : IGame<TLayer> where TLayer : struct, Enum
{
    public int SetupCount { get; private set; }

    public int TickCount { get; private set; }

    public int TeardownCount { get; private set; }

    public IEngine<TLayer>? Engine { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the first <see cref="Tick"/> requests
    /// <see cref="IEngine{TLayer}.Exit"/> after running the callback.
    /// </summary>
    public bool ExitAfterFirstTick { get; set; }

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
        if (ExitAfterFirstTick)
        {
            Engine?.Exit();
        }
    }

    public void Teardown()
    {
        TeardownCount++;
        OnTeardown?.Invoke();
    }
}
