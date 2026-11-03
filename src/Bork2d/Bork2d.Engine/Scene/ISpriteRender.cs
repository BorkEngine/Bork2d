using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A sprite renderable in the scene graph: a transform plus a sprite asset and draw state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// Sprite renderables are scene entities. They do not implement <see cref="IDisposable"/> and
/// have no liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or
/// clear the owning parent to remove one, and re-attach the same instance later. Hide a sprite
/// without removing it by assigning <see langword="null"/>.
/// </para>
/// <para>
/// <see cref="IRenderable.Bounds"/> is the world-space AABB of the drawn quad, derived from the
/// frozen render data, the pivot, the flips, and the world matrix with the same formula the vertex
/// shader uses. It returns <see cref="Bounds.Empty"/> for a null or malformed sprite and never
/// throws for those cases.
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
public interface ISpriteRender<TLayer> : IRenderable where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the loaded sprite asset; <see langword="null"/> draws nothing.
    /// </summary>
    /// <remarks>
    /// Load the asset with <see cref="IAssets.LoadAsset{T}(string)"/> and assign the same instance
    /// to one or more renderables. The loader does not validate the asset's texture reference; a
    /// missing texture simply makes the sprite non-drawable.
    /// </remarks>
    SpriteAsset? Sprite { get; set; }

    /// <summary>
    /// Gets or sets the custom fragment shader; <see langword="null"/> uses the built-in sprite
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
    /// Gets or sets the additional texture references passed to a custom shader, or
    /// <see langword="null"/> for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The setter clones the array and <see langword="null"/> clears it; the getter returns a copy.
    /// Every entry is stored so the renderer can warn about references beyond four, while capture
    /// copies the first four entries and the total count into a value-only snapshot.
    /// </para>
    /// <para>
    /// References are positional and do not have to match
    /// <see cref="ShaderAsset.TextureSlots"/>; mismatches are the renderer's concern.
    /// </para>
    /// </remarks>
    AssetRef<TextureDescriptor>[]? AdditionalTextures { get; set; }

    /// <summary>
    /// Gets or sets the single layer bit used for camera filtering. <c>0</c> and combined masks
    /// are invalid and skipped by the renderer.
    /// </summary>
    TLayer Layer { get; set; }

    /// <summary>
    /// Gets or sets the linear RGBA tint multiplied with the texture;
    /// <see cref="Color.White"/> is neutral.
    /// </summary>
    Color Tint { get; set; }

    /// <summary>
    /// Gets or sets the sort order within a layer; higher draws later.
    /// </summary>
    int ZIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite is mirrored horizontally.
    /// </summary>
    /// <remarks>
    /// The flag mirrors the quad around the sprite's pivot when it is drawn, without changing the
    /// transform or its world matrix. The default is <see langword="false"/>. M7 stores the value
    /// and captures it into the frame snapshot; the M8 renderer applies it, so it has no visible
    /// effect yet.
    /// </remarks>
    bool FlipX { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite is mirrored vertically.
    /// </summary>
    /// <remarks>
    /// The flag mirrors the quad around the sprite's pivot when it is drawn, without changing the
    /// transform or its world matrix. The default is <see langword="false"/>. M7 stores the value
    /// and captures it into the frame snapshot; the M8 renderer applies it, so it has no visible
    /// effect yet.
    /// </remarks>
    bool FlipY { get; set; }

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
