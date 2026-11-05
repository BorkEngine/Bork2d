namespace Bork2d.Bake.Svg;

/// <summary>
/// The geometry of one SVG document: the artboard size and the normalized fillable shapes.
/// </summary>
/// <remarks>
/// <para>
/// The artboard is the coordinate rectangle the root <c>viewBox</c> declares, else the root
/// <c>width</c>/<c>height</c>, else the geometry bounds. Its size is in user units and is never
/// scaled to pixels; the MSDF baker chooses the pixel resolution in a later task.
/// </para>
/// <para>
/// Shape points are artboard-local with the origin at the bottom-left corner and +Y up. The parser
/// only returns an instance when at least one shape has geometry; an empty document is a
/// <c>BK9205</c> error and yields <see langword="null"/>.
/// </para>
/// </remarks>
internal sealed class SvgArtwork
{
    private readonly SvgShape[] _shapes;

    /// <summary>Initializes a new instance of the <see cref="SvgArtwork"/> class.</summary>
    /// <param name="width">The artboard width in user units; finite and greater than zero.</param>
    /// <param name="height">The artboard height in user units; finite and greater than zero.</param>
    /// <param name="shapes">The normalized shapes.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An artboard dimension is not finite or not greater than zero.
    /// </exception>
    internal SvgArtwork(double width, double height, IReadOnlyList<SvgShape> shapes)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The artboard width must be finite and greater than zero.");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "The artboard height must be finite and greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(shapes);
        Width = width;
        Height = height;
        _shapes = [.. shapes];
    }

    /// <summary>Gets the artboard width in user units.</summary>
    internal double Width { get; }

    /// <summary>Gets the artboard height in user units.</summary>
    internal double Height { get; }

    /// <summary>Gets the normalized shapes in draw order.</summary>
    internal IReadOnlyList<SvgShape> Shapes => _shapes;

    /// <summary>Gets a value indicating whether at least one shape has geometry.</summary>
    internal bool HasGeometry => _shapes.Length > 0;
}
