namespace Bork2d.Engine.Scene;

/// <summary>
/// The horizontal alignment of a font renderable's lines around its local origin.
/// </summary>
/// <remarks>
/// Alignment is applied per line: <see cref="Left"/> starts a line at local <c>x = 0</c>,
/// <see cref="Center"/> shifts it by minus half the line's advance width, and <see cref="Right"/>
/// shifts it by minus the whole advance width. The vertical origin is the first line's baseline and
/// is unaffected by this value.
/// </remarks>
public enum TextAlignment
{
    /// <summary>A line starts at the local origin and advances to the right.</summary>
    Left = 0,

    /// <summary>A line is centered on the local origin.</summary>
    Center = 1,

    /// <summary>A line ends at the local origin.</summary>
    Right = 2,
}
