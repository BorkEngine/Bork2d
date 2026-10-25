using Bork2d.Common;

namespace Bork2d.Tests;

internal static class TestEngineFactory
{
    internal static Engine<GameLayer, TestGame<GameLayer>> Create()
        => new(new TestGame<GameLayer>(), new FakeTime(), new FakeWindow(), new FakeAssets());
}
