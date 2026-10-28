using Bork2d.Engine.Time;

namespace Bork2d.Tests.TestDoubles;

internal sealed class FakeTime : IFrameTime
{
    public double DeltaTime => 0;

    public double Time => 0;

    public void NewFrame()
    {
    }
}
