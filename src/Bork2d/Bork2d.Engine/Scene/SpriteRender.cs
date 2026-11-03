using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned sprite renderable: a transform plus a sprite asset and draw state.
/// </summary>
/// <remarks>
/// <para>
/// Sprite renderables are scene entities with no destruction state and no global registration.
/// They are retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </para>
/// <para>
/// Assigning <see cref="Sprite"/> keeps the live asset for the public property and freezes its
/// render fields once under <see cref="Transform.Sync"/>. Capture reads only the frozen copy, so
/// mutating the asset after assignment cannot change a published snapshot or a planned frame.
/// </para>
/// <para>
/// <see cref="Shader"/> follows the same freeze rule for its declarations. <see cref="SetUniform(string, float)"/>
/// validates immediately against the currently assigned shader's frozen metadata and stores accepted
/// values in a per-renderable dictionary; <see cref="ResetUniforms"/> clears them so the shader
/// defaults apply again. Reassigning the shader keeps stored values, and capture applies only those
/// whose name and type still match the new frozen declarations. Capture builds the four-lane
/// instance uniform block from the frozen defaults and matching overrides on the stack, so a warm
/// capture allocates nothing.
/// </para>
/// </remarks>
internal sealed class SpriteRender<TLayer> : Transform, ISpriteRender<TLayer>
    where TLayer : struct, Enum
{
    private readonly Dictionary<string, InstanceUniformValue> _uniforms =
        new(StringComparer.Ordinal);

    private SpriteAsset? _sprite;
    private SpriteRenderData? _renderData;
    private ShaderAsset? _shader;
    private ShaderRenderData? _shaderData;
    private AssetRef<TextureDescriptor>[]? _additionalTextures;
    private TLayer _layer;
    private Color _tint;
    private int _zIndex;
    private bool _flipX;
    private bool _flipY;

    internal SpriteRender(Lock structuralLock)
        : base(structuralLock)
    {
        _sprite = null;
        _renderData = null;
        _shader = null;
        _shaderData = null;
        _additionalTextures = null;
        _layer = default;
        _tint = Color.White;
        _zIndex = 0;
        _flipX = false;
        _flipY = false;
    }

    public SpriteAsset? Sprite
    {
        get
        {
            lock (Sync)
            {
                return _sprite;
            }
        }

        set
        {
            SpriteRenderData? renderData = SpriteRenderData.FreezeOrNull(value);
            lock (Sync)
            {
                _sprite = value;
                _renderData = renderData;
            }
        }
    }

    /// <inheritdoc/>
    public ShaderAsset? Shader
    {
        get
        {
            lock (Sync)
            {
                return _shader;
            }
        }

        set
        {
            // Freeze outside the lock: a malformed asset must not throw from this setter, and the
            // conditional weak table publishes exactly one copy for concurrent assignments.
            ShaderRenderData? shaderData = ShaderRenderData.FreezeOrNull(value);
            lock (Sync)
            {
                _shader = value;
                _shaderData = shaderData;
            }
        }
    }

    /// <inheritdoc/>
    public AssetRef<TextureDescriptor>[]? AdditionalTextures
    {
        get
        {
            lock (Sync)
            {
                return _additionalTextures is { } textures
                    ? (AssetRef<TextureDescriptor>[])textures.Clone()
                    : null;
            }
        }

        set
        {
            lock (Sync)
            {
                _additionalTextures = value is { } textures
                    ? (AssetRef<TextureDescriptor>[])textures.Clone()
                    : null;
            }
        }
    }

    /// <inheritdoc/>
    public Bounds Bounds
    {
        get
        {
            SpriteRenderData? data;
            bool flipX;
            bool flipY;
            lock (Sync)
            {
                data = _renderData;
                flipX = _flipX;
                flipY = _flipY;
            }

            // The world matrix is composed under the structural lock, so read the renderable state
            // first and never hold the per-object lock while acquiring the structural lock.
            return data is null
                ? Bounds.Empty
                : SpriteBounds.CreateWorldBounds(data, WorldMatrix(), flipX, flipY);
        }
    }

    /// <summary>
    /// Copies this sprite's capture state under one per-object lock acquisition.
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock. Capture uses this instead of the public property
    /// getters, which would acquire <see cref="Transform.Sync"/> once per field and could observe a
    /// concurrent writer between fields. The frozen sprite and shader data, the stored uniform
    /// values, and the additional texture references are read in the same critical section. The
    /// snapshot's <see cref="SpriteSnapshot.WorldMatrix"/> is the local matrix as a placeholder; the
    /// capture pass replaces it with the composed world matrix after this method returns and the lock
    /// is released.
    /// </remarks>
    /// <param name="snapshot">
    /// The sprite values with the local matrix in <see cref="SpriteSnapshot.WorldMatrix"/>.
    /// </param>
    /// <returns>The sprite's local-to-parent matrix.</returns>
    internal Matrix3x2 CaptureForSnapshot(out SpriteSnapshot snapshot)
    {
        lock (Sync)
        {
            var local = LocalMatrixUnderLock;
            ShaderRenderData? shaderData = _shaderData;
            var uniform0 = Vector4.Zero;
            var uniform1 = Vector4.Zero;
            var uniform2 = Vector4.Zero;
            var uniform3 = Vector4.Zero;
            if (shaderData is not null)
            {
                Span<Vector4> block = stackalloc Vector4[ShaderLayout.InstanceBlockVectorCount];
                shaderData.DefaultInstanceUniformBlock.CopyTo(block);
                foreach (KeyValuePair<string, InstanceUniformValue> pair in _uniforms)
                {
                    // A stored value survives a shader reassignment; it only applies when the new
                    // frozen declaration still has the same ordinal name and value type.
                    if (shaderData.TryGetInstanceUniform(pair.Key, out ShaderUniform uniform)
                        && uniform.Type == pair.Value.Type)
                    {
                        ShaderLayout.WriteUniform(block, in uniform, pair.Value.Value);
                    }
                }

                uniform0 = block[0];
                uniform1 = block[1];
                uniform2 = block[2];
                uniform3 = block[3];
            }

            AssetRef<TextureDescriptor>[]? textures = _additionalTextures;
            snapshot = new SpriteSnapshot(
                _renderData,
                local,
                _tint,
                ToMask(_layer),
                _zIndex,
                _flipX,
                _flipY)
            {
                ShaderData = shaderData,
                Uniform0 = uniform0,
                Uniform1 = uniform1,
                Uniform2 = uniform2,
                Uniform3 = uniform3,
                ExtraTexture0 = GetAdditionalTexture(textures, 0),
                ExtraTexture1 = GetAdditionalTexture(textures, 1),
                ExtraTexture2 = GetAdditionalTexture(textures, 2),
                ExtraTexture3 = GetAdditionalTexture(textures, 3),
                AdditionalTextureCount = textures?.Length ?? 0,
            };
            return local;
        }
    }

    public TLayer Layer
    {
        get
        {
            lock (Sync)
            {
                return _layer;
            }
        }

        set
        {
            lock (Sync)
            {
                _layer = value;
            }
        }
    }

    public Color Tint
    {
        get
        {
            lock (Sync)
            {
                return _tint;
            }
        }

        set
        {
            lock (Sync)
            {
                _tint = value;
            }
        }
    }

    public int ZIndex
    {
        get
        {
            lock (Sync)
            {
                return _zIndex;
            }
        }

        set
        {
            lock (Sync)
            {
                _zIndex = value;
            }
        }
    }

    public bool FlipX
    {
        get
        {
            lock (Sync)
            {
                return _flipX;
            }
        }

        set
        {
            lock (Sync)
            {
                _flipX = value;
            }
        }
    }

    public bool FlipY
    {
        get
        {
            lock (Sync)
            {
                return _flipY;
            }
        }

        set
        {
            lock (Sync)
            {
                _flipY = value;
            }
        }
    }

    /// <inheritdoc/>
    public void SetUniform(string name, float value)
        => SetUniformCore(name, ShaderUniformType.Float, new Vector4(value, 0f, 0f, 0f));

    /// <inheritdoc/>
    public void SetUniform(string name, Vector2 value)
        => SetUniformCore(name, ShaderUniformType.Vector2, new Vector4(value.X, value.Y, 0f, 0f));

    /// <inheritdoc/>
    public void SetUniform(string name, Vector3 value)
        => SetUniformCore(
            name,
            ShaderUniformType.Vector3,
            new Vector4(value.X, value.Y, value.Z, 0f));

    /// <inheritdoc/>
    public void SetUniform(string name, Vector4 value)
        => SetUniformCore(name, ShaderUniformType.Vector4, value);

    /// <inheritdoc/>
    public void SetUniform(string name, Color value)
        => SetUniformCore(name, ShaderUniformType.Color, value);

    /// <inheritdoc/>
    public void ResetUniforms()
    {
        lock (Sync)
        {
            _uniforms.Clear();
        }
    }

    private void SetUniformCore(string name, ShaderUniformType type, Vector4 value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (Sync)
        {
            ShaderRenderData? shaderData = _shaderData;
            if (shaderData is null)
            {
                throw new ArgumentException(
                    $"Cannot set instance uniform '{name}': the sprite has no Shader assigned.",
                    nameof(name));
            }

            if (!shaderData.TryGetInstanceUniform(name, out ShaderUniform uniform))
            {
                throw new ArgumentException(
                    $"The assigned shader does not declare an instance uniform named '{name}'.",
                    nameof(name));
            }

            if (uniform.Type != type)
            {
                throw new ArgumentException(
                    $"Instance uniform '{name}' is declared as {uniform.Type}, not {type}.",
                    nameof(name));
            }

            _uniforms[name] = new InstanceUniformValue(type, value);
        }
    }

    private static AssetRef<TextureDescriptor> GetAdditionalTexture(
        AssetRef<TextureDescriptor>[]? textures,
        int index)
        => textures is not null && index < textures.Length ? textures[index] : default;

    private static uint ToMask(TLayer layer) => (uint)Unsafe.As<TLayer, int>(ref layer);

    private readonly record struct InstanceUniformValue(ShaderUniformType Type, Vector4 Value);
}
