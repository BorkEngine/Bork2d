using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;

namespace Bork2d.Tests.TestSupport;

internal static class TestEngineFactory
{
    internal static GameEngine<GameLayer> Create()
        => new(new FakeTime(), new FakeWindow(), new FakeAssets());
}
