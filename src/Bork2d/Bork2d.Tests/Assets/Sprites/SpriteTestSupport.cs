using System.Numerics;
using Bork2d.Assets.Descriptors;
using Bork2d.Assets.Pack;
using Bork2d.Tests.Assets.Vfs;

namespace Bork2d.Tests.Assets.Sprites;

/// <summary>
/// Builds valid internal texture-descriptor and sprite-asset values and temporary packs for the M3
/// WP4 tests. No binary fixture is committed.
/// </summary>
internal static class SpriteTestSupport
{
    internal const string SpritePath = "sprites/player";
    internal const string TexturePath = "textures/player";
    internal const string PayloadPath = "textures/raw/player";

    /// <summary>Creates a valid 2 × 2 RGBA8 descriptor.</summary>
    internal static TextureDescriptor Descriptor() => new()
    {
        Payload = PayloadPath,
        Width = 2,
        Height = 2,
        Format = "rgba8",
        Filter = "linear",
        Wrap = "clamp",
    };

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
        Scale = 16f,
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

    /// <summary>Creates a raw RGBA8 payload of the descriptor's declared size.</summary>
    internal static byte[] Payload(TextureDescriptor descriptor)
        => new byte[checked((int)((ulong)descriptor.Width * descriptor.Height * 4))];

    /// <summary>
    /// Writes a debug pack containing a texture descriptor, its raw payload, and a sprite asset.
    /// Callers override the values to build invalid cases; descriptor and payload may be omitted.
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
