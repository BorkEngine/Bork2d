namespace Bork2d.Api;

public readonly record struct Sprite(int Id)
{
    public static readonly Sprite Empty = default;
}

public readonly record struct Color(float R, float G, float B, float A)
{
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);
    public static readonly Color Black = new(0f, 0f, 0f, 1f);
    public static readonly Color White = new(1f, 1f, 1f, 1f);

    public static readonly Color Red = new(1f, 0f, 0f, 1f);
    public static readonly Color Green = new(0f, 1f, 0f, 1f);
    public static readonly Color Blue = new(0f, 0f, 1f, 1f);

    public static readonly Color Yellow = new(1f, 1f, 0f, 1f);
    public static readonly Color Cyan = new(0f, 1f, 1f, 1f);
    public static readonly Color Magenta = new(1f, 0f, 1f, 1f);
    public static readonly Color Gray = new(0.5f, 0.5f, 0.5f, 1f);

}

public enum ClearKind
{
    None,
    Depth,
    Color,
}
