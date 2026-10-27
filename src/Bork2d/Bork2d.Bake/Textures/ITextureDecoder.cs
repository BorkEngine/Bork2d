namespace Bork2d.Bake.Textures;

/// <summary>
/// The decoder seam: turns a texture source stream into straight-alpha RGBA8 pixels.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must not premultiply alpha, apply gamma or ICC correction, flip, or resample.
/// The returned pixels are row-major with a top-left origin.
/// </para>
/// <para>
/// The seam exists so tests can exercise the pipeline without a real image codec, and so a future
/// codec (KTX2) can be added without changing discovery, sidecar, or payload logic.
/// </para>
/// </remarks>
internal interface ITextureDecoder
{
    /// <summary>
    /// Decodes one texture source.
    /// </summary>
    /// <param name="stream">A readable, seekable stream over the source file.</param>
    /// <returns>The decoded RGBA8 image.</returns>
    /// <exception cref="InvalidOperationException">The source is corrupt, truncated, or unsupported.</exception>
    DecodedImage Decode(Stream stream);
}
