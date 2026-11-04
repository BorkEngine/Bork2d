using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A line renderable in the scene graph: a transform plus an open or closed polyline and draw
/// state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// Line renderables are scene entities. They do not implement <see cref="IDisposable"/> and have no
/// liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or clear the
/// owning parent to remove one, and re-attach the same instance later. Hide a line by assigning
/// <see cref="Points"/> = <see langword="null"/> or an empty list.
/// </para>
/// <para>
/// The engine expands the polyline on the CPU into local-space triangles and caches the immutable
/// result. Points, <see cref="Width"/>, and <see cref="Closed"/> rebuild the cached geometry; color,
/// anti-aliasing, shader, layer, z-index, and transform changes do not. The world transform is
/// applied by the renderer, so moving a line never rebuilds it.
/// </para>
/// <para>
/// Geometry validation is conservative and happens without throwing: an open line needs at least
/// two distinct points after duplicate consecutive points are skipped, a closed line needs at least
/// three, every point must be finite, and <see cref="Width"/> must be finite and greater than zero.
/// A line that fails validation is captured as not drawable and the renderer later skips it with a
/// one-time diagnostic. A miter join beyond the limit (<c>4</c> times the half-width) falls back to
/// a bevel for that corner.
/// </para>
/// <para>
/// <see cref="IRenderable.Bounds"/> is the world-space AABB of the expanded geometry, including half
/// the width and every miter or bevel extent. It returns <see cref="Bounds.Empty"/> for a line with
/// no drawable geometry or a non-finite transform and never throws for those cases.
/// </para>
/// <para>
/// No member requires a particular thread. Property access and
/// <see cref="SetUniform(string, float)"/> use the renderable's per-object lock, and capture reads
/// all fields under one lock acquisition; the engine's structural lock is never held across a
/// wait.
/// </para>
/// <para>
/// Instance uniform values are validated immediately: <c>SetUniform</c> throws
/// <see cref="ArgumentException"/> when no <see cref="Shader"/> is assigned, when the name is not
/// one of that shader's frozen instance declarations, or when the declared type does not match the
/// overload. Accepted values are stored per renderable until <see cref="ResetUniforms"/> and are
/// applied over the frozen shader defaults at capture. Reassigning <see cref="Shader"/> keeps them;
/// a value whose name or type no longer matches is inert at capture.
/// </para>
/// </remarks>
public interface ILineRender<TLayer> : IRenderable where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the polyline points in local world units; <see langword="null"/> or an empty
    /// list draws nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The setter copies the values from the supplied list, so mutating the caller's array or list
    /// afterwards does not change this renderable or a published snapshot. The getter returns a
    /// read-only view of the stored copy; it allocates nothing per get and does not expose a mutable
    /// array. Assigning a new list rebuilds the cached geometry; a list with the same values still
    /// counts as a new assignment.
    /// </para>
    /// <para>
    /// An open line requires at least two distinct points and a closed line at least three.
    /// Duplicate consecutive points are skipped, and a closed line drops a final point equal to the
    /// first before joining the last point to the first. A non-finite point makes the line
    /// non-drawable without throwing.
    /// </para>
    /// </remarks>
    IReadOnlyList<Vector2> Points { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last point joins the first; the default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A closed line has no end caps. Changing this value rebuilds the cached geometry; an
    /// out-of-range value is stored as-is and only affects whether the geometry is drawable.
    /// </remarks>
    bool Closed { get; set; }

    /// <summary>
    /// Gets or sets the full line width in local world units; the default is <c>1</c>.
    /// </summary>
    /// <remarks>
    /// The setter stores the value without validating or throwing. A non-finite or non-positive
    /// width makes the line non-drawable at capture. Changing the width rebuilds the cached
    /// geometry.
    /// </remarks>
    float Width { get; set; }

    /// <summary>
    /// Gets or sets the linear RGBA line color; <see cref="Color.White"/> is the default.
    /// </summary>
    /// <remarks>
    /// The setter stores the value without validation. The color does not affect geometry and never
    /// rebuilds it.
    /// </remarks>
    Color Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the renderer softens the line edges; the default is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Anti-aliasing is a fragment-stage coverage term, not extra geometry, so changing it never
    /// rebuilds the cached triangles. The engine stores and captures the flag; the line renderer
    /// consumes it.
    /// </remarks>
    bool AntiAlias { get; set; }

    /// <summary>
    /// Gets or sets the custom fragment shader; <see langword="null"/> uses the built-in line
    /// shader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assigning the asset freezes its declarations once under the renderable's lock, and capture
    /// reads only that frozen <see cref="ShaderRenderData"/>. Mutating the asset afterwards —
    /// including replacing or editing its declaration arrays — does not change a published snapshot,
    /// and re-assigning the same asset keeps the first frozen values. Freezing tolerates malformed
    /// metadata; the renderer rejects or falls back from it.
    /// </para>
    /// <para>
    /// Reassigning this property does not clear the values stored by
    /// <see cref="SetUniform(string, float)"/> or <see cref="ResetUniforms"/>. A stored value whose
    /// name or type does not match the new shader is inert at capture.
    /// </para>
    /// </remarks>
    ShaderAsset? Shader { get; set; }

    /// <summary>
    /// Gets or sets the single layer bit used for camera filtering. <c>0</c> and combined masks
    /// are invalid and skipped by the renderer.
    /// </summary>
    /// <remarks>
    /// The default is the layer enum's default value (<c>0</c>). Invalid values are captured as-is
    /// so the renderer can apply the same one-time diagnostic as for sprites.
    /// </remarks>
    TLayer Layer { get; set; }

    /// <summary>
    /// Gets or sets the shared 2D draw order; higher draws later and equal values fall back to
    /// per-camera screen-space Y then X.
    /// </summary>
    /// <remarks>
    /// <c>ZIndex</c> is shared by sprites and lines and is not layer-scoped: <see cref="Layer"/> only
    /// filters which cameras draw this renderable. The default is <c>0</c>.
    /// </remarks>
    int ZIndex { get; set; }

    /// <summary>
    /// Stores an instance uniform value of type <see cref="ShaderUniformType.Float"/>.
    /// </summary>
    /// <param name="name">The shader-declared instance uniform name, matched ordinally.</param>
    /// <param name="value">The value to store; only the declared component is captured.</param>
    /// <remarks>
    /// Validates immediately against the currently assigned <see cref="Shader"/>'s frozen metadata
    /// and stores the value until <see cref="ResetUniforms"/>. Capture applies it over the default
    /// when the name is still declared with the same type. The call may run on any thread.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or whitespace; no shader is
    /// assigned; the shader does not declare the name; or the declared type is not
    /// <see cref="ShaderUniformType.Float"/>.
    /// </exception>
    void SetUniform(string name, float value);

    /// <summary>
    /// Stores an instance uniform value of type <see cref="ShaderUniformType.Vector2"/>.
    /// </summary>
    /// <param name="name">The shader-declared instance uniform name, matched ordinally.</param>
    /// <param name="value">The value to store.</param>
    /// <remarks>
    /// Validates immediately against the currently assigned <see cref="Shader"/>'s frozen metadata
    /// and stores the value until <see cref="ResetUniforms"/>. Capture applies it over the default
    /// when the name is still declared with the same type. The call may run on any thread.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or whitespace; no shader is
    /// assigned; the shader does not declare the name; or the declared type is not
    /// <see cref="ShaderUniformType.Vector2"/>.
    /// </exception>
    void SetUniform(string name, Vector2 value);

    /// <summary>
    /// Stores an instance uniform value of type <see cref="ShaderUniformType.Vector3"/>.
    /// </summary>
    /// <param name="name">The shader-declared instance uniform name, matched ordinally.</param>
    /// <param name="value">The value to store.</param>
    /// <remarks>
    /// Validates immediately against the currently assigned <see cref="Shader"/>'s frozen metadata
    /// and stores the value until <see cref="ResetUniforms"/>. Capture applies it over the default
    /// when the name is still declared with the same type. The call may run on any thread.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or whitespace; no shader is
    /// assigned; the shader does not declare the name; or the declared type is not
    /// <see cref="ShaderUniformType.Vector3"/>.
    /// </exception>
    void SetUniform(string name, Vector3 value);

    /// <summary>
    /// Stores an instance uniform value of type <see cref="ShaderUniformType.Vector4"/>.
    /// </summary>
    /// <param name="name">The shader-declared instance uniform name, matched ordinally.</param>
    /// <param name="value">The value to store.</param>
    /// <remarks>
    /// Validates immediately against the currently assigned <see cref="Shader"/>'s frozen metadata
    /// and stores the value until <see cref="ResetUniforms"/>. Capture applies it over the default
    /// when the name is still declared with the same type. The call may run on any thread.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or whitespace; no shader is
    /// assigned; the shader does not declare the name; or the declared type is not
    /// <see cref="ShaderUniformType.Vector4"/>.
    /// </exception>
    void SetUniform(string name, Vector4 value);

    /// <summary>
    /// Stores an instance uniform value of type <see cref="ShaderUniformType.Color"/>.
    /// </summary>
    /// <param name="name">The shader-declared instance uniform name, matched ordinally.</param>
    /// <param name="value">The linear RGBA value to store.</param>
    /// <remarks>
    /// Validates immediately against the currently assigned <see cref="Shader"/>'s frozen metadata
    /// and stores the value until <see cref="ResetUniforms"/>. Capture applies it over the default
    /// when the name is still declared with the same type. The call may run on any thread.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or whitespace; no shader is
    /// assigned; the shader does not declare the name; or the declared type is not
    /// <see cref="ShaderUniformType.Color"/>.
    /// </exception>
    void SetUniform(string name, Color value);

    /// <summary>
    /// Clears every stored instance uniform value so the assigned shader's defaults apply again.
    /// </summary>
    /// <remarks>
    /// The call is a no-op when no values are stored and may run on any thread. It does not change
    /// the assigned <see cref="Shader"/> or its frozen declarations.
    /// </remarks>
    void ResetUniforms();
}
