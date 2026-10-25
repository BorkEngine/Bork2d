using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Tests;

internal sealed class FakeWindow : IWindow
{
    public string Title { get; set; } = "test";

    public Vector2 Size { get; set; } = new(1280f, 720f);

    public bool Fullscreen { get; set; }

    public bool CursorVisible { get; set; } = true;
}
