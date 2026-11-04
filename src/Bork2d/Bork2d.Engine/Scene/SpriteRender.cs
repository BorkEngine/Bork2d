using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned sprite renderable: a transform plus a sprite asset and sprite-specific draw
/// state.
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
/// The shader, color, layer, z-index, instance-uniform state, and their capture packing come from
/// <see cref="Renderable2D{TLayer}"/>. Capture reads them through
/// <see cref="Renderable2D{TLayer}.CaptureShared"/> in the same critical section as the sprite
/// fields, so a warm capture allocates nothing.
/// </para>
/// </remarks>
internal sealed class SpriteRender<TLayer> : Renderable2D<TLayer>, ISpriteRender<TLayer>
    where TLayer : struct, Enum
{
    private SpriteAsset? _sprite;
    private SpriteRenderData? _renderData;
    private AssetRef<TextureDescriptor>[]? _additionalTextures;
    private bool _flipX;
    private bool _flipY;

    internal SpriteRender(Lock structuralLock)
        : base(structuralLock, "sprite")
    {
        _sprite = null;
        _renderData = null;
        _additionalTextures = null;
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
    public override Bounds Bounds
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
    /// concurrent writer between fields. The frozen sprite data, the shared shader, color, layer,
    /// z-index and uniform state, and the additional texture references are read in the same
    /// critical section. The snapshot's <see cref="SpriteSnapshot.WorldMatrix"/> is the local matrix
    /// as a placeholder; the capture pass replaces it with the composed world matrix after this
    /// method returns and the lock is released.
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
            CaptureShared(
                out ShaderRenderData? shaderData,
                out Vector4 uniform0,
                out Vector4 uniform1,
                out Vector4 uniform2,
                out Vector4 uniform3,
                out Color color,
                out uint layer,
                out int zIndex);
            AssetRef<TextureDescriptor>[]? textures = _additionalTextures;
            snapshot = new SpriteSnapshot(
                _renderData,
                local,
                color,
                layer,
                zIndex,
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

    private static AssetRef<TextureDescriptor> GetAdditionalTexture(
        AssetRef<TextureDescriptor>[]? textures,
        int index)
        => textures is not null && index < textures.Length ? textures[index] : default;
}
