using System.Numerics;
using Bork2d.Engine.Platform;

namespace Bork2d.Tests.TestDoubles;

internal sealed class FakeWindow : IWindow
{
    public string Title { get; set; } = "test";

    public Vector2 Size { get; set; } = new(1280f, 720f);

    public bool Fullscreen { get; set; }

    public bool CursorVisible { get; set; } = true;

    public bool IsMinimized { get; set; }
}
