namespace Bork2d.Bake.Svg;

/// <summary>
/// Parses SVG path data into closed contours in the path's local user coordinates.
/// </summary>
/// <remarks>
/// <para>
/// Every command in the subset is supported in both absolute and relative form, including repeated
/// coordinate sets, the smooth cubic/quadratic reflection, and endpoint-parameterized elliptical arcs.
/// An arc becomes cubic Bézier segments of at most 90 degrees each; a zero radius becomes a line and
/// identical endpoints produce no segment. A close command closes the subpath and the next drawing
/// command starts a new subpath at that closed subpath's start point, as SVG specifies.
/// </para>
/// <para>
/// The last subpath is implicitly closed for fill. Degenerate subpaths (a moveto without a drawing
/// command) are discarded here; zero-length segments and area-less contours are discarded by
/// <see cref="SvgContourCleaner"/>.
/// </para>
/// <para>
/// The parser never throws for malformed data. On failure it returns <see langword="false"/> and the
/// message names the failing token with its zero-based character offset; the caller adds the
/// element's line and column.
/// </para>
/// </remarks>
internal static class SvgPathParser
{
    /// <summary>Parses path data.</summary>
    /// <param name="data">The <c>d</c> attribute value; may be empty.</param>
    /// <param name="contours">The closed contours in path order, or an empty list on failure.</param>
    /// <param name="message">The failure message, or an empty string on success.</param>
    /// <returns><see langword="true"/> when the data is valid.</returns>
    internal static bool TryParse(string data, out List<SvgContour> contours, out string message)
    {
        ArgumentNullException.ThrowIfNull(data);

        var reader = new PathDataReader(data);
        if (!reader.TryRead())
        {
            contours = [];
            message = reader.ErrorMessage;
            return false;
        }

        contours = reader.Contours;
        message = string.Empty;
        return true;
    }

    private sealed class PathDataReader
    {
        private readonly string _text;
        private readonly List<SvgContour> _contours = [];
        private int _position;
        private int _commandOffset;
        private List<SvgSegment>? _segments;
        private SvgPoint _subpathStart;
        private SvgPoint _current;
        private bool _sawMove;
        private bool _lastWasCubic;
        private bool _lastWasQuadratic;
        private SvgPoint _lastCubicControl2;
        private SvgPoint _lastQuadraticControl;
        private string _errorMessage = string.Empty;

        internal PathDataReader(string text)
        {
            _text = text;
        }

        internal List<SvgContour> Contours => _contours;

        internal string ErrorMessage => _errorMessage;

        internal bool TryRead()
        {
            while (true)
            {
                SvgNumberParser.SkipSeparators(_text, ref _position);
                if (_position >= _text.Length)
                {
                    break;
                }

                char command = _text[_position];
                if (!IsCommand(command))
                {
                    return Fail(
                        $"expected a path command, found '{command}' at character offset {_position}");
                }

                if (!_sawMove && command is not ('M' or 'm'))
                {
                    return Fail(
                        $"path data must begin with a moveto command, found '{command}' at "
                            + $"character offset {_position}");
                }

                _commandOffset = _position;
                _position++;
                if (!ReadCommand(command))
                {
                    return false;
                }
            }

            FinishSubpath();
            return true;
        }

        private static bool IsCommand(char character)
            => char.ToUpperInvariant(character)
                is 'M' or 'L' or 'H' or 'V' or 'C' or 'S' or 'Q' or 'T' or 'A' or 'Z';

        private bool ReadCommand(char command)
        {
            bool relative = char.IsAsciiLetterLower(command);
            return char.ToUpperInvariant(command) switch
            {
                'M' => ReadMove(relative),
                'L' => ReadLine(relative),
                'H' => ReadHorizontal(relative),
                'V' => ReadVertical(relative),
                'C' => ReadCubic(relative),
                'S' => ReadSmoothCubic(relative),
                'Q' => ReadQuadratic(relative),
                'T' => ReadSmoothQuadratic(relative),
                'A' => ReadArc(relative),
                'Z' => ReadClose(),
                _ => Fail($"unknown path command '{command}' at character offset {_commandOffset}"),
            };
        }

        private bool ReadMove(bool relative)
        {
            if (!TryReadPoint(relative, out SvgPoint point))
            {
                return false;
            }

            _sawMove = true;
            BeginSubpath(point);
            while (IsNumberAhead())
            {
                if (!TryReadPoint(relative, out SvgPoint line))
                {
                    return false;
                }

                AddLine(line);
            }

            return true;
        }

        private bool ReadLine(bool relative)
        {
            EnsureSubpath();
            do
            {
                if (!TryReadPoint(relative, out SvgPoint point))
                {
                    return false;
                }

                AddLine(point);
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadHorizontal(bool relative)
        {
            EnsureSubpath();
            do
            {
                if (!TryReadNumber(out double value))
                {
                    return false;
                }

                AddLine(new SvgPoint(relative ? _current.X + value : value, _current.Y));
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadVertical(bool relative)
        {
            EnsureSubpath();
            do
            {
                if (!TryReadNumber(out double value))
                {
                    return false;
                }

                AddLine(new SvgPoint(_current.X, relative ? _current.Y + value : value));
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadCubic(bool relative)
        {
            EnsureSubpath();
            do
            {
                if (!TryReadPoint(relative, out SvgPoint control1)
                    || !TryReadPoint(relative, out SvgPoint control2)
                    || !TryReadPoint(relative, out SvgPoint end))
                {
                    return false;
                }

                AddCubic(control1, control2, end);
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadSmoothCubic(bool relative)
        {
            EnsureSubpath();
            do
            {
                SvgPoint control1 = _lastWasCubic ? Reflect(_lastCubicControl2) : _current;
                if (!TryReadPoint(relative, out SvgPoint control2)
                    || !TryReadPoint(relative, out SvgPoint end))
                {
                    return false;
                }

                AddCubic(control1, control2, end);
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadQuadratic(bool relative)
        {
            EnsureSubpath();
            do
            {
                if (!TryReadPoint(relative, out SvgPoint control)
                    || !TryReadPoint(relative, out SvgPoint end))
                {
                    return false;
                }

                AddQuadratic(control, end);
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadSmoothQuadratic(bool relative)
        {
            EnsureSubpath();
            do
            {
                SvgPoint control = _lastWasQuadratic ? Reflect(_lastQuadraticControl) : _current;
                if (!TryReadPoint(relative, out SvgPoint end))
                {
                    return false;
                }

                AddQuadratic(control, end);
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadArc(bool relative)
        {
            EnsureSubpath();
            do
            {
                if (!TryReadNumber(out double radiusX)
                    || !TryReadNumber(out double radiusY)
                    || !TryReadNumber(out double rotation)
                    || !TryReadFlag(out bool largeArc)
                    || !TryReadFlag(out bool sweep)
                    || !TryReadPoint(relative, out SvgPoint end))
                {
                    return false;
                }

                if (!AddArc(radiusX, radiusY, rotation, largeArc, sweep, end))
                {
                    return false;
                }
            }
            while (IsNumberAhead());

            return true;
        }

        private bool ReadClose()
        {
            FinishSubpath();
            _current = _subpathStart;
            ResetSmoothState();
            return true;
        }

        /// <summary>
        /// Converts one endpoint-parameterized arc to cubic Bézier segments and appends them.
        /// </summary>
        /// <remarks>
        /// This is the SVG implementation-notes endpoint-to-center conversion. Radii take their
        /// absolute value and are scaled up when the endpoints cannot be joined, the two flags choose
        /// the center, and the resulting sweep is split into equal pieces of at most 90 degrees. Each
        /// piece uses the standard <c>4/3 * tan(delta/4)</c> control-point multiplier. The final piece
        /// ends at the authored endpoint so the segment chain stays exact.
        /// </remarks>
        private bool AddArc(
            double radiusX,
            double radiusY,
            double rotationDegrees,
            bool largeArc,
            bool sweep,
            SvgPoint end)
        {
            ResetSmoothState();

            SvgPoint start = _current;
            if (start == end)
            {
                return true;
            }

            radiusX = Math.Abs(radiusX);
            radiusY = Math.Abs(radiusY);
            if (radiusX == 0 || radiusY == 0)
            {
                AddLine(end);
                return true;
            }

            double rotation = rotationDegrees * Math.PI / 180;
            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);
            double halfDx = (start.X - end.X) / 2;
            double halfDy = (start.Y - end.Y) / 2;
            double x1 = (cos * halfDx) + (sin * halfDy);
            double y1 = (-sin * halfDx) + (cos * halfDy);

            // The endpoints may lie outside the authored radii; SVG scales both radii up uniformly.
            double scale = ((x1 * x1) / (radiusX * radiusX)) + ((y1 * y1) / (radiusY * radiusY));
            if (scale > 1)
            {
                double factor = Math.Sqrt(scale);
                radiusX *= factor;
                radiusY *= factor;
            }

            double radiusXSquared = radiusX * radiusX;
            double radiusYSquared = radiusY * radiusY;
            double numerator = (radiusXSquared * radiusYSquared)
                - (radiusXSquared * y1 * y1)
                - (radiusYSquared * x1 * x1);
            double denominator = (radiusXSquared * y1 * y1) + (radiusYSquared * x1 * x1);
            if (denominator == 0)
            {
                AddLine(end);
                return true;
            }

            double coefficient = Math.Sqrt(Math.Max(0, numerator / denominator));
            if (largeArc == sweep)
            {
                coefficient = -coefficient;
            }

            double centerX1 = coefficient * radiusX * y1 / radiusY;
            double centerY1 = -coefficient * radiusY * x1 / radiusX;
            double centerX = (cos * centerX1) - (sin * centerY1) + ((start.X + end.X) / 2);
            double centerY = (sin * centerX1) + (cos * centerY1) + ((start.Y + end.Y) / 2);

            double ux = (x1 - centerX1) / radiusX;
            double uy = (y1 - centerY1) / radiusY;
            double vx = (-x1 - centerX1) / radiusX;
            double vy = (-y1 - centerY1) / radiusY;
            double startAngle = Math.Atan2(uy, ux);
            double delta = Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
            if (!sweep && delta > 0)
            {
                delta -= 2 * Math.PI;
            }
            else if (sweep && delta < 0)
            {
                delta += 2 * Math.PI;
            }

            if (delta == 0)
            {
                return true;
            }

            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)));
            double step = delta / count;
            double multiplier = 4.0 / 3.0 * Math.Tan(step / 4);

            SvgPoint segmentStart = start;
            for (int index = 0; index < count; index++)
            {
                double startOfPiece = startAngle + (index * step);
                double endOfPiece = startOfPiece + step;
                SvgPoint segmentEnd = index == count - 1
                    ? end
                    : PointOnEllipse(centerX, centerY, radiusX, radiusY, cos, sin, endOfPiece);
                SvgPoint derivativeStart =
                    EllipseDerivative(radiusX, radiusY, cos, sin, startOfPiece);
                SvgPoint derivativeEnd = EllipseDerivative(radiusX, radiusY, cos, sin, endOfPiece);
                var control1 = new SvgPoint(
                    segmentStart.X + (multiplier * derivativeStart.X),
                    segmentStart.Y + (multiplier * derivativeStart.Y));
                var control2 = new SvgPoint(
                    segmentEnd.X - (multiplier * derivativeEnd.X),
                    segmentEnd.Y - (multiplier * derivativeEnd.Y));

                if (!IsFinite(control1) || !IsFinite(control2) || !IsFinite(segmentEnd))
                {
                    return Fail(
                        $"arc data at character offset {_commandOffset} produces a non-finite point");
                }

                AddCubic(control1, control2, segmentEnd);
                segmentStart = segmentEnd;
            }

            // An arc is not a cubic for the smooth reflection rule; a following S or T must use the
            // current point as its reflected control instead of the last arc piece's control.
            ResetSmoothState();
            return true;
        }

        private static SvgPoint PointOnEllipse(
            double centerX,
            double centerY,
            double radiusX,
            double radiusY,
            double cos,
            double sin,
            double angle)
        {
            double x = radiusX * Math.Cos(angle);
            double y = radiusY * Math.Sin(angle);
            return new SvgPoint(
                centerX + (cos * x) - (sin * y),
                centerY + (sin * x) + (cos * y));
        }

        private static SvgPoint EllipseDerivative(
            double radiusX,
            double radiusY,
            double cos,
            double sin,
            double angle)
        {
            double x = -radiusX * Math.Sin(angle);
            double y = radiusY * Math.Cos(angle);
            return new SvgPoint((cos * x) - (sin * y), (sin * x) + (cos * y));
        }

        private static bool IsFinite(SvgPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

        private bool TryReadPoint(bool relative, out SvgPoint point)
        {
            point = default;
            if (!TryReadNumber(out double x) || !TryReadNumber(out double y))
            {
                return false;
            }

            point = relative ? new SvgPoint(_current.X + x, _current.Y + y) : new SvgPoint(x, y);
            return true;
        }

        private bool TryReadNumber(out double value)
        {
            SvgNumberParser.SkipSeparators(_text, ref _position);
            int start = _position;
            if (!SvgNumberParser.TryRead(_text, ref _position, out value))
            {
                return Fail($"expected a number at character offset {start}");
            }

            if (!double.IsFinite(value))
            {
                return Fail($"the number at character offset {start} is not finite");
            }

            return true;
        }

        private bool TryReadFlag(out bool value)
        {
            SvgNumberParser.SkipSeparators(_text, ref _position);
            if (_position >= _text.Length || (_text[_position] != '0' && _text[_position] != '1'))
            {
                value = false;
                return Fail($"expected an arc flag ('0' or '1') at character offset {_position}");
            }

            value = _text[_position] == '1';
            _position++;
            return true;
        }

        private bool IsNumberAhead()
        {
            SvgNumberParser.SkipSeparators(_text, ref _position);
            return _position < _text.Length && SvgNumberParser.IsNumberStart(_text[_position]);
        }

        private void BeginSubpath(SvgPoint point)
        {
            FinishSubpath();
            _segments = [];
            _subpathStart = point;
            _current = point;
            ResetSmoothState();
        }

        private List<SvgSegment> EnsureSubpath()
        {
            if (_segments is null)
            {
                _segments = [];
                _subpathStart = _current;
            }

            return _segments;
        }

        private void FinishSubpath()
        {
            if (_segments is null)
            {
                return;
            }

            if (_segments.Count > 0)
            {
                if (_current != _subpathStart)
                {
                    _segments.Add(new SvgLineSegment(_current, _subpathStart));
                }

                _contours.Add(new SvgContour(_segments));
            }

            _segments = null;
        }

        private void AddLine(SvgPoint end)
        {
            List<SvgSegment> segments = EnsureSubpath();
            if (_current != end)
            {
                segments.Add(new SvgLineSegment(_current, end));
            }

            _current = end;
            ResetSmoothState();
        }

        private void AddCubic(SvgPoint control1, SvgPoint control2, SvgPoint end)
        {
            List<SvgSegment> segments = EnsureSubpath();
            segments.Add(new SvgCubicSegment(_current, control1, control2, end));
            _current = end;
            _lastWasCubic = true;
            _lastWasQuadratic = false;
            _lastCubicControl2 = control2;
        }

        private void AddQuadratic(SvgPoint control, SvgPoint end)
        {
            List<SvgSegment> segments = EnsureSubpath();
            segments.Add(new SvgQuadraticSegment(_current, control, end));
            _current = end;
            _lastWasCubic = false;
            _lastWasQuadratic = true;
            _lastQuadraticControl = control;
        }

        private SvgPoint Reflect(SvgPoint control)
            => new((2 * _current.X) - control.X, (2 * _current.Y) - control.Y);

        private void ResetSmoothState()
        {
            _lastWasCubic = false;
            _lastWasQuadratic = false;
        }

        private bool Fail(string message)
        {
            _errorMessage = message;
            return false;
        }
    }
}
