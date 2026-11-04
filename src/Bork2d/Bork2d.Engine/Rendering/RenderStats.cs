namespace Bork2d.Engine.Rendering;

/// <summary>
/// An immutable snapshot of the renderer's counters for the last completed frame (M8 §6.9).
/// </summary>
/// <remarks>
/// <para>
/// The values describe the last completed frame, except <see cref="ValidationErrors"/> and
/// <see cref="ValidationWarnings"/>, which are cumulative for the run. <see cref="DrawCalls"/> counts
/// the frame's contiguous draw runs (one <c>vkCmdDraw</c> per sprite run and one
/// <c>vkCmdDrawIndexed</c> per line run) plus the final fullscreen present draw, so a default-only
/// camera records two draws (its run and the present) while a camera that alternates kinds or shaders
/// records one per contiguous group plus the present; <see cref="NonEmptyCameras"/> counts the cameras
/// with at least one run. <see cref="Instances"/> counts sprite instance records and
/// <see cref="Lines"/> counts line records submitted by the frame, both across cameras.
/// <see cref="TexturePages"/>
/// and <see cref="TexturePageCapacity"/> are current for that frame and include the placeholder at
/// index <c>0</c>; <see cref="TexturePages"/> counts the ready pages of the current asset catalog, so
/// a page retired by a mount stops counting even though its image may stay GPU-resident until
/// residency reclamation. <see cref="TextureUploadsPending"/> and
/// <see cref="TextureUploadBytesPending"/> describe the uploads the frame's pump left queued for a
/// later frame. <see cref="SkippedSprites"/> counts the null, invalid-layer, non-<c>Quad</c>, and
/// still-loading sprites (a loading custom shader, main texture, or declared additional texture)
/// skipped by that frame; a sprite drawn with the placeholder or the default shader fallback is not
/// skipped. <see cref="SkippedLines"/> counts the null-geometry, invalid-layer, and still-loading
/// lines skipped by that frame; a line drawn with the built-in line shader or the line default
/// fallback is not skipped.
/// </para>
/// <para>
/// The type is a value, so reading <c>IEngine.RenderStats</c> allocates nothing and cannot throw.
/// Before the first completed frame, in headless mode, and before the host installs its stats
/// source the getter returns <see langword="default"/>; after shutdown it may retain the last
/// published values.
/// </para>
/// </remarks>
/// <param name="FrameSequence">The snapshot sequence of the last completed frame.</param>
/// <param name="FramebufferWidth">The last completed frame's framebuffer/target width in pixels.</param>
/// <param name="FramebufferHeight">The last completed frame's framebuffer/target height in pixels.</param>
/// <param name="DrawCalls">
/// The number of draw calls recorded by the frame: one per sprite or line draw run plus the final
/// fullscreen present draw.
/// </param>
/// <param name="Instances">The number of sprite instance records submitted by the frame.</param>
/// <param name="Lines">The number of line records submitted by the frame.</param>
/// <param name="Cameras">The number of cameras captured for the frame.</param>
/// <param name="NonEmptyCameras">The number of cameras that recorded at least one draw run.</param>
/// <param name="TexturePages">
/// The ready texture pages in the current asset catalog, including the placeholder at index <c>0</c>.
/// Images retired by an asset mount and awaiting residency reclamation are not counted.
/// </param>
/// <param name="TexturePageCapacity">
/// The current bindless page capacity in entries, including the placeholder at index <c>0</c>.
/// </param>
/// <param name="TextureUploadsPending">
/// The content-ready texture pages queued for upload but not begun in the last completed frame.
/// </param>
/// <param name="TextureUploadBytesPending">
/// The declared payload bytes of <paramref name="TextureUploadsPending"/>.
/// </param>
/// <param name="SkippedSprites">
/// The sprites skipped by the frame (null, invalid layer, non-<c>Quad</c>, or a still-loading shader,
/// main texture, or additional texture page).
/// </param>
/// <param name="SkippedLines">
/// The lines skipped by the frame (null/invalid geometry, invalid layer, or a still-loading custom
/// shader).
/// </param>
/// <param name="ValidationErrors">The cumulative fatal validation errors for the run.</param>
/// <param name="ValidationWarnings">The cumulative validation warnings for the run.</param>
public readonly record struct RenderStats(
    long FrameSequence,
    int FramebufferWidth,
    int FramebufferHeight,
    int DrawCalls,
    int Instances,
    int Lines,
    int Cameras,
    int NonEmptyCameras,
    int TexturePages,
    int TexturePageCapacity,
    int TextureUploadsPending,
    long TextureUploadBytesPending,
    long SkippedSprites,
    long SkippedLines,
    long ValidationErrors,
    long ValidationWarnings);
