namespace Bork2d.Bake.Svg;

/// <summary>
/// A mutable axis-aligned bounding box used while the parser accumulates geometry.
/// </summary>
/// <remarks>
/// <see cref="Empty"/> is the only valid starting value. <see cref="Include(SvgPoint)"/> grows the
/// box; the accessors are only meaningful while <see cref="IsEmpty"/> is <see langword="false"/>,
/// and the parser never reads them otherwise. The type is a struct because bounds accumulation sits
/// on the parser's inner loops, but it is not shared across threads.
/// </remarks>
internal struct SvgBounds
{
    private bool _hasValue;
    private double _minX;
    private double _minY;
    private double _maxX;
    private double _maxY;

    /// <summary>Gets an empty bounds that has not included any point.</summary>
    internal static SvgBounds Empty => default;

    /// <summary>Gets a value indicating whether no point was included yet.</summary>
    internal readonly bool IsEmpty => !_hasValue;

    /// <summary>Gets the minimum X, or zero while the bounds are empty.</summary>
    internal readonly double MinX => _minX;

    /// <summary>Gets the minimum Y, or zero while the bounds are empty.</summary>
    internal readonly double MinY => _minY;

    /// <summary>Gets the maximum X, or zero while the bounds are empty.</summary>
    internal readonly double MaxX => _maxX;

    /// <summary>Gets the maximum Y, or zero while the bounds are empty.</summary>
    internal readonly double MaxY => _maxY;

    /// <summary>Gets the width, or zero while the bounds are empty.</summary>
    internal readonly double Width => _hasValue ? _maxX - _minX : 0;

    /// <summary>Gets the height, or zero while the bounds are empty.</summary>
    internal readonly double Height => _hasValue ? _maxY - _minY : 0;

    /// <summary>Grows the bounds so they contain the given point.</summary>
    /// <param name="point">The point to include.</param>
    internal void Include(SvgPoint point)
    {
        if (!_hasValue)
        {
            _hasValue = true;
            _minX = point.X;
            _minY = point.Y;
            _maxX = point.X;
            _maxY = point.Y;
            return;
        }

        _minX = Math.Min(_minX, point.X);
        _minY = Math.Min(_minY, point.Y);
        _maxX = Math.Max(_maxX, point.X);
        _maxY = Math.Max(_maxY, point.Y);
    }
}
