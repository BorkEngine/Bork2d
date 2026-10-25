using System.Numerics;

namespace Bork2d.Api;

public interface IInput
{
    event Action<char> OnTextTyped;

    bool IsKeyPressed(KeyCode key);
    bool IsKeyReleased(KeyCode key);
    bool IsKeyDown(KeyCode key);

    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    float MouseWheelDelta { get; }

    bool IsMousePressed(MouseButton button);
    bool IsMouseReleased(MouseButton button);
    bool IsMouseDown(MouseButton button);
}
