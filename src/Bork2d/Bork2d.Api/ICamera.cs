using System.Numerics;

namespace Bork2d.Api;

/// <summary>
/// A camera in the scene graph: a transform plus projection and clear state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// Cameras are scene entities. They do not implement <see cref="IDisposable"/> and have no
/// liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or clear
/// the owning parent to remove one, and re-attach the same instance later.
/// </remarks>
public interface ICamera<TLayer> : ITransform where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the normalized viewport <c>(x, y, width, height)</c> with a top-left origin.
    /// </summary>
    Vector4 Viewport { get; set; }

    /// <summary>
    /// Gets or sets the half visible height in world units.
    /// </summary>
    float Size { get; set; }

    /// <summary>
    /// Gets or sets the layer mask this camera renders; <c>0</c> sees nothing.
    /// </summary>
    TLayer CullingMask { get; set; }

    /// <summary>
    /// Gets or sets how the camera clears its viewport before drawing.
    /// </summary>
    ClearKind Clear { get; set; }

    /// <summary>
    /// Gets or sets the linear RGBA clear color used by <see cref="ClearKind.Color"/>.
    /// </summary>
    Color ClearColor { get; set; }
}
