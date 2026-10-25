using Bork2d.Api;

namespace Bork2d.Tests;

internal sealed class FakeTime : ITime
{
    public double DeltaTime => 0;

    public double Time => 0;

    public void NewFrame()
    {
    }
}
