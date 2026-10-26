using Bork2d.Common.Scene;
using Bork2d.Tests.TestDoubles;

namespace Bork2d.Tests.TestSupport;

internal static class TestEngineFactory
{
    internal static Engine<GameLayer> Create()
        => new(new FakeTime(), new FakeWindow(), new FakeAssets());
}
