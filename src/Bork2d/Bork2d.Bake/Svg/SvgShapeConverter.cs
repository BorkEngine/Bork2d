using System.Xml.Linq;

namespace Bork2d.Bake.Svg;

/// <summary>
/// Converts the geometry attributes of one SVG shape element into contours in the element's local
/// user coordinates.
/// </summary>
/// <remarks>
/// <para>
/// The converter owns the per-element attribute rules: a <c>path</c> requires <c>d</c>; a
/// <c>rect</c> requires <c>width</c> and <c>height</c>; <c>circle</c> requires <c>r</c>;
/// <c>ellipse</c> requires <c>rx</c> and <c>ry</c>; <c>polyline</c> and <c>polygon</c> require
/// <c>points</c>. Optional attributes keep their SVG defaults. Lengths are user units with an
/// optional <c>px</c> suffix.
/// </para>
/// <para>
/// A line never produces fill geometry, a zero radius or extent produces no contour, and a rounded
/// rectangle (<c>rx</c>/<c>ry</c> greater than zero) is a <c>BK9202</c> error because the subset has
/// no rounded-rectangle support. Failures are reported through the constructor's callback so the
/// owning parser keeps one diagnostic format.
/// </para>
/// </remarks>
internal sealed class SvgShapeConverter
{
    /// <summary>The circle quarter control-point multiplier, <c>4/3 * tan(pi/8)</c>.</summary>
    private const double EllipseKappa = 0.5522847498307936;

    private readonly Action<XElement, string> _reportError;

    /// <summary>Initializes a new instance of the <see cref="SvgShapeConverter"/> class.</summary>
    /// <param name="reportError">
    /// The callback that reports one invalid-content message for an element; the owner adds the
    /// diagnostic code and location.
    /// </param>
    internal SvgShapeConverter(Action<XElement, string> reportError)
    {
        ArgumentNullException.ThrowIfNull(reportError);
        _reportError = reportError;
    }

    /// <summary>Builds the local-coordinate contours of one supported shape element.</summary>
    /// <param name="element">The shape element.</param>
    /// <returns>The contours, or an empty list when the element is invalid or has no fill area.</returns>
    internal List<SvgContour> CreateContours(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Name.LocalName switch
        {
            "path" => CreatePath(element),
            "rect" => CreateRect(element),
            "circle" => CreateCircle(element),
            "ellipse" => CreateEllipse(element),
            "line" => CreateLine(element),
            "polyline" => CreatePointListContour(element),
            "polygon" => CreatePointListContour(element),
            _ => [],
        };
    }

    private List<SvgContour> CreatePath(XElement element)
    {
        XAttribute? data = element.Attribute("d");
        if (data is null)
        {
            _reportError(element, "is missing the required 'd' attribute");
            return [];
        }

        if (!SvgPathParser.TryParse(data.Value, out List<SvgContour> contours, out string message))
        {
            _reportError(element, $"has invalid path data: {message}");
            return [];
        }

        return contours;
    }

    private List<SvgContour> CreateRect(XElement element)
    {
        if (!TryReadLength(element, "x", 0, out double x)
            || !TryReadLength(element, "y", 0, out double y)
            || !TryReadRequiredLength(element, "width", out double width)
            || !TryReadRequiredLength(element, "height", out double height)
            || !TryReadCornerRadius(element, "rx")
            || !TryReadCornerRadius(element, "ry"))
        {
            return [];
        }

        if (width < 0 || height < 0)
        {
            _reportError(element, "has a negative 'width' or 'height'");
            return [];
        }

        if (width == 0 || height == 0)
        {
            return [];
        }

        var topLeft = new SvgPoint(x, y);
        var topRight = new SvgPoint(x + width, y);
        var bottomRight = new SvgPoint(x + width, y + height);
        var bottomLeft = new SvgPoint(x, y + height);
        return
        [
            new SvgContour(
            [
                new SvgLineSegment(topLeft, topRight),
                new SvgLineSegment(topRight, bottomRight),
                new SvgLineSegment(bottomRight, bottomLeft),
                new SvgLineSegment(bottomLeft, topLeft),
            ]),
        ];
    }

    /// <summary>
    /// Validates one rounded-rectangle radius attribute.
    /// </summary>
    /// <remarks>
    /// The subset has no rounded-rectangle support, so a positive radius is the dedicated
    /// <c>BK9202</c> error the brief assigns to this case; zero keeps the sharp rectangle valid.
    /// </remarks>
    private bool TryReadCornerRadius(XElement element, string name)
    {
        XAttribute? attribute = element.Attribute(name);
        if (attribute is null)
        {
            return true;
        }

        if (!SvgNumberParser.TryParseLength(attribute.Value, out double radius))
        {
            _reportError(element, $"has an invalid '{name}' length '{attribute.Value}'");
            return false;
        }

        if (radius < 0)
        {
            _reportError(element, $"has a negative '{name}' radius");
            return false;
        }

        if (radius > 0)
        {
            _reportError(
                element,
                "uses a rounded rectangle ('rx'/'ry'), which the supported subset does not include");
            return false;
        }

        return true;
    }

    private List<SvgContour> CreateCircle(XElement element)
    {
        if (!TryReadLength(element, "cx", 0, out double cx)
            || !TryReadLength(element, "cy", 0, out double cy)
            || !TryReadRequiredLength(element, "r", out double radius))
        {
            return [];
        }

        if (radius < 0)
        {
            _reportError(element, "has a negative 'r' radius");
            return [];
        }

        if (radius == 0)
        {
            return [];
        }

        return [CreateEllipseContour(cx, cy, radius, radius)];
    }

    private List<SvgContour> CreateEllipse(XElement element)
    {
        if (!TryReadLength(element, "cx", 0, out double cx)
            || !TryReadLength(element, "cy", 0, out double cy)
            || !TryReadRequiredLength(element, "rx", out double radiusX)
            || !TryReadRequiredLength(element, "ry", out double radiusY))
        {
            return [];
        }

        if (radiusX < 0 || radiusY < 0)
        {
            _reportError(element, "has a negative 'rx' or 'ry' radius");
            return [];
        }

        if (radiusX == 0 || radiusY == 0)
        {
            return [];
        }

        return [CreateEllipseContour(cx, cy, radiusX, radiusY)];
    }

    /// <summary>
    /// Builds an ellipse from four cubic Bézier quarters.
    /// </summary>
    /// <remarks>
    /// The control offset is the standard kappa constant times the radius per axis. Quarters share
    /// their endpoint objects exactly, so the segment chain is closed without rounding gaps.
    /// </remarks>
    private static SvgContour CreateEllipseContour(
        double cx,
        double cy,
        double radiusX,
        double radiusY)
    {
        double offsetX = radiusX * EllipseKappa;
        double offsetY = radiusY * EllipseKappa;

        var right = new SvgPoint(cx + radiusX, cy);
        var bottom = new SvgPoint(cx, cy + radiusY);
        var left = new SvgPoint(cx - radiusX, cy);
        var top = new SvgPoint(cx, cy - radiusY);

        return new SvgContour(
        [
            new SvgCubicSegment(
                right,
                new SvgPoint(cx + radiusX, cy + offsetY),
                new SvgPoint(cx + offsetX, cy + radiusY),
                bottom),
            new SvgCubicSegment(
                bottom,
                new SvgPoint(cx - offsetX, cy + radiusY),
                new SvgPoint(cx - radiusX, cy + offsetY),
                left),
            new SvgCubicSegment(
                left,
                new SvgPoint(cx - radiusX, cy - offsetY),
                new SvgPoint(cx - offsetX, cy - radiusY),
                top),
            new SvgCubicSegment(
                top,
                new SvgPoint(cx + offsetX, cy - radiusY),
                new SvgPoint(cx + radiusX, cy - offsetY),
                right),
        ]);
    }

    private List<SvgContour> CreateLine(XElement element)
    {
        // A line has no fill area at any orientation, so its geometry is only validated here.
        if (!TryReadLength(element, "x1", 0, out _)
            || !TryReadLength(element, "y1", 0, out _)
            || !TryReadLength(element, "x2", 0, out _)
            || !TryReadLength(element, "y2", 0, out _))
        {
            return [];
        }

        return [];
    }

    /// <summary>
    /// Builds the fill contour of a <c>polyline</c> or <c>polygon</c>.
    /// </summary>
    /// <remarks>
    /// Fill closes an open polyline and an open polygon subpath implicitly, so both elements produce
    /// the same closed contour; only the closing segment is added when the last point differs from the
    /// first. A list with fewer than two points produces no contour.
    /// </remarks>
    private List<SvgContour> CreatePointListContour(XElement element)
    {
        XAttribute? attribute = element.Attribute("points");
        if (attribute is null)
        {
            _reportError(element, "is missing the required 'points' attribute");
            return [];
        }

        if (!TryParsePoints(attribute.Value, out List<SvgPoint> points, out int errorOffset))
        {
            _reportError(element, $"has invalid 'points' data at character offset {errorOffset}");
            return [];
        }

        if (points.Count < 2)
        {
            return [];
        }

        var segments = new List<SvgSegment>(points.Count);
        for (int index = 0; index + 1 < points.Count; index++)
        {
            if (points[index] != points[index + 1])
            {
                segments.Add(new SvgLineSegment(points[index], points[index + 1]));
            }
        }

        if (points[^1] != points[0])
        {
            segments.Add(new SvgLineSegment(points[^1], points[0]));
        }

        return segments.Count == 0 ? [] : [new SvgContour(segments)];
    }

    private static bool TryParsePoints(string text, out List<SvgPoint> points, out int errorOffset)
    {
        points = [];
        errorOffset = 0;
        ReadOnlySpan<char> span = text.AsSpan();
        int position = 0;
        while (true)
        {
            SvgNumberParser.SkipSeparators(span, ref position);
            if (position >= span.Length)
            {
                return true;
            }

            errorOffset = position;
            if (!SvgNumberParser.TryRead(span, ref position, out double x) || !double.IsFinite(x))
            {
                return false;
            }

            errorOffset = position;
            SvgNumberParser.SkipSeparators(span, ref position);
            if (!SvgNumberParser.TryRead(span, ref position, out double y) || !double.IsFinite(y))
            {
                return false;
            }

            points.Add(new SvgPoint(x, y));
        }
    }

    private bool TryReadLength(XElement element, string name, double defaultValue, out double value)
    {
        XAttribute? attribute = element.Attribute(name);
        if (attribute is null)
        {
            value = defaultValue;
            return true;
        }

        if (!SvgNumberParser.TryParseLength(attribute.Value, out value))
        {
            _reportError(element, $"has an invalid '{name}' length '{attribute.Value}'");
            return false;
        }

        return true;
    }

    private bool TryReadRequiredLength(XElement element, string name, out double value)
    {
        value = 0;
        XAttribute? attribute = element.Attribute(name);
        if (attribute is null)
        {
            _reportError(element, $"is missing the required '{name}' attribute");
            return false;
        }

        if (!SvgNumberParser.TryParseLength(attribute.Value, out value))
        {
            _reportError(element, $"has an invalid '{name}' length '{attribute.Value}'");
            return false;
        }

        return true;
    }
}
