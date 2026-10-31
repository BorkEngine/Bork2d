namespace Bork2d.Engine.Rendering;

/// <summary>
/// Why a camera cannot produce a valid projection at draw time (M8 §6.3).
/// </summary>
/// <remarks>
/// The renderer skips the whole camera, including its clear, with a throttled diagnostic. The
/// public screen/world helpers throw <see cref="InvalidOperationException"/> for the same values
/// instead of returning NaN.
/// </remarks>
internal enum CameraInvalidReason
{
    /// <summary>The camera values are valid.</summary>
    None,

    /// <summary>The framebuffer size is not finite or not positive.</summary>
    Framebuffer,

    /// <summary><c>Size</c> is not finite or not positive.</summary>
    Size,

    /// <summary>The normalized viewport is not finite or has a non-positive width/height.</summary>
    Viewport,

    /// <summary>The rounded pixel viewport is empty.</summary>
    EmptyRect,

    /// <summary>The clear kind is not one of the defined values.</summary>
    ClearKind,

    /// <summary>A <c>Clear.Color</c> camera has a non-finite clear color.</summary>
    ClearColor,

    /// <summary>The camera world matrix is singular.</summary>
    SingularMatrix,

    /// <summary>The camera world matrix has a non-unit scale.</summary>
    NonUnitScale,
}
