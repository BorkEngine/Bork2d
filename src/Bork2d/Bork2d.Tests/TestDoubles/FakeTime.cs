using Bork2d.Engine.Time;

namespace Bork2d.Tests.TestDoubles;

internal sealed class FakeTime : IFrameTime
{
    public double DeltaTime { get; set; }

    public double Time { get; set; }

    public void NewFrame()
    {
    }
}
