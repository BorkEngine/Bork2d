namespace Bork2d.Api;

/// <summary>
/// An opaque, session-local handle for a loaded sprite asset.
/// </summary>
/// <param name="Id">The handle value; <c>0</c> is the empty handle.</param>
/// <remarks>
/// Handles are returned by <see cref="IAssets.LoadSprite(string)"/> and never persisted; save game
/// data stores asset paths. <see cref="Sprite.Empty"/> draws nothing and its handle value is not an
/// asset path.
/// </remarks>
public readonly record struct Sprite(int Id)
{
    /// <summary>The empty handle; its <see cref="Id"/> is <c>0</c>.</summary>
    public static readonly Sprite Empty = default;
}

/// <summary>
/// A linear RGBA color with each component in <c>[0, 1]</c>.
/// </summary>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
/// <param name="A">The alpha component.</param>
/// <remarks>
/// Sprite tints and clear colors are authored in linear space; sRGB conversion happens at output.
/// Use the predefined members for common colors.
/// </remarks>
public readonly record struct Color(float R, float G, float B, float A)
{
    /// <summary>Fully transparent black.</summary>
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);

    /// <summary>Opaque black.</summary>
    public static readonly Color Black = new(0f, 0f, 0f, 1f);

    /// <summary>Opaque white.</summary>
    public static readonly Color White = new(1f, 1f, 1f, 1f);

    /// <summary>Opaque red.</summary>
    public static readonly Color Red = new(1f, 0f, 0f, 1f);

    /// <summary>Opaque green.</summary>
    public static readonly Color Green = new(0f, 1f, 0f, 1f);

    /// <summary>Opaque blue.</summary>
    public static readonly Color Blue = new(0f, 0f, 1f, 1f);

    /// <summary>Opaque yellow.</summary>
    public static readonly Color Yellow = new(1f, 1f, 0f, 1f);

    /// <summary>Opaque cyan.</summary>
    public static readonly Color Cyan = new(0f, 1f, 1f, 1f);

    /// <summary>Opaque magenta.</summary>
    public static readonly Color Magenta = new(1f, 0f, 1f, 1f);

    /// <summary>Opaque mid-gray.</summary>
    public static readonly Color Gray = new(0.5f, 0.5f, 0.5f, 1f);
}

/// <summary>
/// Selects how a camera clears its viewport before drawing.
/// </summary>
public enum ClearKind
{
    /// <summary>
    /// Preserve earlier cameras' output and the safety clear.
    /// </summary>
    None,

    /// <summary>
    /// Reserved for a depth clear; v1 has no depth attachment, so this is a no-op when rendering
    /// lands (M8).
    /// </summary>
    Depth,

    /// <summary>
    /// Clear the camera viewport to the camera's <c>ClearColor</c>.
    /// </summary>
    Color,
}
