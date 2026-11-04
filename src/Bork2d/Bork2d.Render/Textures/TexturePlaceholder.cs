namespace Bork2d.Render.Textures;

/// <summary>
/// The asset paths and built-in resolution of the bindless placeholder page.
/// </summary>
/// <remarks>
/// <para>
/// The placeholder is index <c>0</c>, always resident, and is uploaded at renderer startup from the
/// embedded built-in pack. Its content is an uncompressed 8x8 magenta/opaque-black checkerboard baked
/// by the tool-only <c>builtin-placeholder</c> processor, so the runtime has no generated pixel path
/// and the placeholder is ordinary pack content like every other texture.
/// </para>
/// <para>
/// The built-in source is 8x8, but the page store records the loaded descriptor's dimensions rather
/// than assuming this constant; <see cref="Size"/> is the expected built-in resolution used by tests
/// and diagnostics.
/// </para>
/// </remarks>
internal static class TexturePlaceholder
{
    /// <summary>The canonical path of the built-in placeholder <c>TextureDescriptor</c>.</summary>
    internal const string DescriptorPath = "textures/placeholder";

    /// <summary>The canonical path of the built-in placeholder KTX2 payload.</summary>
    internal const string PayloadPath = "textures/placeholder-ktx2";

    /// <summary>The built-in placeholder's width and height in pixels.</summary>
    internal const int Size = 8;
}
