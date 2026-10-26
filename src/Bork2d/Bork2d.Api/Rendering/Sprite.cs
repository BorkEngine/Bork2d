namespace Bork2d.Api.Rendering;

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
