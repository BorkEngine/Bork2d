using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A drawable 2D scene transform: a bounded transform plus the shader, color, layer, and draw-order
/// state shared by every renderable kind.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// Renderables are scene entities. They do not implement <see cref="IDisposable"/> and have no
/// liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or clear the
/// owning parent to remove one, and re-attach the same instance later. Hide a renderable by
/// assigning its kind-specific content to <see langword="null"/> or empty.
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
public interface IRenderable<TLayer> : IBoundedTransform where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the linear RGBA render color; <see cref="Color.White"/> is neutral and the
    /// default.
    /// </summary>
    /// <remarks>
    /// The setter stores the value without validation. A sprite multiplies its sampled texel by the
    /// color; a line uses it as its flat color. The color never affects geometry.
    /// </remarks>
    Color Color { get; set; }

    /// <summary>
    /// Gets or sets the custom fragment shader; <see langword="null"/> uses the built-in shader for
    /// this renderable kind.
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
    /// so the renderer can apply the same one-time diagnostic to every renderable kind.
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
