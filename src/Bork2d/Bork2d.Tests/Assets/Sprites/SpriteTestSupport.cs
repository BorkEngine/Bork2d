using System.Numerics;
using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.Assets.Vfs;
using Bork2d.Tests.TestSupport;

namespace Bork2d.Tests.Assets.Sprites;

/// <summary>
/// Builds valid public texture-descriptor, KTX2 payload, and sprite-asset values and temporary packs
/// for the asset-reference tests. No binary fixture is committed.
/// </summary>
internal static class SpriteTestSupport
{
    internal const string SpritePath = "sprites/player";
    internal const string TexturePath = "textures/player";
    internal const string PayloadPath = "textures/raw/player";

    /// <summary>Creates a valid 2 × 2 Rgba8 KTX2 descriptor.</summary>
    internal static TextureDescriptor Descriptor()
    {
        BakedTexture baked = Ktx2TextureFactory.BakeSolid(
            TextureFormat.Rgba8,
            2,
            2,
            255,
            0,
            255);
        return Ktx2TextureFactory.Descriptor(PayloadPath, baked, 2, 2, TextureFilter.Linear);
    }

    /// <summary>Creates a valid Quad sprite over the standard texture path.</summary>
    internal static SpriteAsset Quad() => new()
    {
        Texture = TexturePath,
        Uvs =
        [
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        ],
        Scale = new Vector2(16f, 16f),
        Pivot = new Vector2(0.5f, 0.5f),
        Topology = SpriteTopology.Quad,
        Indices = null,
    };

    /// <summary>Creates a valid indexed triangle-strip sprite.</summary>
    internal static SpriteAsset TriangleStrip()
    {
        SpriteAsset sprite = Quad();
        sprite.Topology = SpriteTopology.TriangleStrip;
        sprite.Indices = [0, 1, 2, 3];
        return sprite;
    }

    /// <summary>Creates a valid indexed triangles sprite.</summary>
    internal static SpriteAsset Triangles()
    {
        SpriteAsset sprite = Quad();
        sprite.Topology = SpriteTopology.Triangles;
        sprite.Indices = [0, 1, 2];
        return sprite;
    }

    /// <summary>Creates the KTX2 payload of the descriptor's declared format and dimensions.</summary>
    internal static byte[] Payload(TextureDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Ktx2TextureFactory.BakeSolid(
                descriptor.Format,
                (int)descriptor.Width,
                (int)descriptor.Height,
                255,
                0,
                255)
            .Payload
            .ToArray();
    }

    /// <summary>
    /// Writes a debug pack containing a texture descriptor, its raw payload, and a sprite asset.
    /// Callers override the values to build other cases; descriptor and payload may be omitted to
    /// exercise pack-time reference failures.
    /// </summary>
    internal static string WriteSpritePack(
        TempPackDirectory directory,
        TextureDescriptor? descriptor = null,
        SpriteAsset? sprite = null,
        byte[]? payload = null,
        string spritePath = SpritePath,
        string texturePath = TexturePath,
        string payloadPath = PayloadPath,
        bool includeDescriptor = true,
        bool includePayload = true,
        PackCompressionMode mode = PackCompressionMode.Debug)
    {
        descriptor ??= Descriptor();
        sprite ??= Quad();
        payload ??= Payload(descriptor);

        return directory.WritePack(
            "base.pck",
            builder =>
            {
                if (includeDescriptor)
                {
                    builder.AddMessagePack(texturePath, descriptor);
                }

                if (includePayload)
                {
                    builder.AddRaw(payloadPath, payload);
                }

                builder.AddMessagePack(spritePath, sprite);
            },
            mode);
    }
}
