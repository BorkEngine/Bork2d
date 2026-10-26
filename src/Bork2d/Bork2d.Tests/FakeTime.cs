using Bork2d.Common;

namespace Bork2d.Tests;

internal sealed class FakeTime : IFrameTime
{
    public double DeltaTime => 0;

    public double Time => 0;

    public void NewFrame()
    {
    }
}
