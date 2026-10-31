namespace Bork2d.Render;

/// <summary>
/// Creates the renderer for one app; the host and tests inject the implementation.
/// </summary>
internal interface IRendererFactory
{
    /// <summary>Creates the renderer for one start request. The renderer is not started yet.</summary>
    /// <param name="startInfo">The start request.</param>
    /// <returns>The created renderer.</returns>
    IRenderer Create(RendererStartInfo startInfo);
}
