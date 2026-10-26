using Bork2d.Common;

namespace Bork2d.Tests;

internal static class TestEngineFactory
{
    internal static Engine<GameLayer> Create()
        => new(new FakeTime(), new FakeWindow(), new FakeAssets());
}
