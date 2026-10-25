using Bork2d.Api;

namespace Bork2d.Tests;

internal sealed class TestGame<TLayer> : IGame<TLayer>
    where TLayer : struct, Enum
{
    public int SetupCount { get; private set; }

    public int TickCount { get; private set; }

    public int TeardownCount { get; private set; }

    public void Setup(IEngine<TLayer> engine) => SetupCount++;

    public void Tick() => TickCount++;

    public void Teardown() => TeardownCount++;
}
