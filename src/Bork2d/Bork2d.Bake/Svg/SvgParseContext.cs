using System.Xml.Linq;

namespace Bork2d.Bake.Svg;

/// <summary>
/// Walks one parsed SVG document and builds the artboard and normalized shapes.
/// </summary>
/// <remarks>
/// <para>
/// The context is created per parse call, owns the accumulated shapes and diagnostics, and is not
/// thread-safe or reusable. It applies the root transform and every nested element transform while
/// building geometry, resolves the artboard (root <c>viewBox</c>, then root <c>width</c>/<c>height</c>,
/// then geometry bounds), maps user coordinates to artboard-local Y-up coordinates, and normalizes
/// each shape's contours. Shape-element attribute conversion lives in <see cref="SvgShapeConverter"/>.
/// </para>
/// <para>
/// A nested <c>svg</c> element is not a viewport in this subset and is skipped with an informational
/// diagnostic, as is every other unsupported element and its subtree. <c>preserveAspectRatio</c> is
/// ignored: the artboard is exactly the <c>viewBox</c> rectangle.
/// </para>
/// </remarks>
internal sealed class SvgParseContext
{
    private static readonly SvgStyle _defaultStyle = new(true, SvgFillRule.NonZero);

    private readonly SvgDiagnostics _diagnostics;
    private readonly SvgShapeConverter _shapeConverter;
    private readonly List<ShapeDraft> _shapes = [];
    private XElement? _root;

    /// <summary>Initializes a new instance of the <see cref="SvgParseContext"/> class.</summary>
    /// <param name="diagnostics">The bag that receives diagnostics for one parse call.</param>
    internal SvgParseContext(SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics;
        _shapeConverter = new SvgShapeConverter(ReportError);
    }

    /// <summary>Parses a document whose root is an <c>svg</c> element.</summary>
    /// <param name="root">The root element.</param>
    /// <returns>The artwork, or <see langword="null"/> when an error was reported.</returns>
    internal SvgArtwork? Parse(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;

        if (!TryReadStyle(root, _defaultStyle, out SvgStyle rootStyle)
            || !TryReadTransform(root, out SvgAffine rootTransform)
            || !TryReadArtboard(root, out Artboard? artboard))
        {
            return null;
        }

        foreach (XElement child in root.Elements())
        {
            Walk(child, rootTransform, rootStyle);
        }

        if (_diagnostics.HasErrors)
        {
            return null;
        }

        if (_shapes.Count == 0)
        {
            return NoGeometry();
        }

        Artboard? resolved = artboard;
        if (resolved is null)
        {
            resolved = FromBounds();
            if (resolved is null)
            {
                return NoGeometry();
            }
        }

        Artboard board = resolved.Value;
        double tolerance = Math.Max(1e-6, Math.Max(board.Width, board.Height) * 1e-4);
        var shapes = new List<SvgShape>(_shapes.Count);
        foreach (ShapeDraft draft in _shapes)
        {
            var mapped = new List<SvgContour>(draft.Contours.Count);
            foreach (SvgContour contour in draft.Contours)
            {
                mapped.Add(contour.Transform(board.Mapping));
            }

            IReadOnlyList<SvgContour> normalized =
                SvgShapeNormalizer.Normalize(mapped, draft.FillRule, tolerance);
            if (normalized.Count > 0)
            {
                shapes.Add(new SvgShape(draft.FillRule, normalized));
            }
        }

        if (shapes.Count == 0)
        {
            return NoGeometry();
        }

        return new SvgArtwork(board.Width, board.Height, shapes);
    }

    private void Walk(XElement element, SvgAffine parentTransform, SvgStyle inheritedStyle)
    {
        string name = element.Name.LocalName;
        switch (name)
        {
            case "g":
                if (TryReadTransform(element, out SvgAffine groupTransform)
                    && TryReadStyle(element, inheritedStyle, out SvgStyle groupStyle))
                {
                    SvgAffine combined = SvgAffine.Multiply(parentTransform, groupTransform);
                    foreach (XElement child in element.Elements())
                    {
                        Walk(child, combined, groupStyle);
                    }
                }

                return;

            case "path":
            case "rect":
            case "circle":
            case "ellipse":
            case "line":
            case "polyline":
            case "polygon":
                ReadShape(element, parentTransform, inheritedStyle);
                return;

            case "svg":
                ReportIgnored(element, "A nested SVG element is not supported and was skipped.");
                return;

            case "title":
            case "desc":
            case "metadata":
                return;

            default:
                ReportIgnored(element, $"The SVG element '{name}' is not supported and was skipped.");
                return;
        }
    }

    private void ReadShape(XElement element, SvgAffine parentTransform, SvgStyle inheritedStyle)
    {
        if (!TryReadTransform(element, out SvgAffine transform)
            || !TryReadStyle(element, inheritedStyle, out SvgStyle style))
        {
            return;
        }

        List<SvgContour> contours = _shapeConverter.CreateContours(element);
        if (contours.Count == 0 || !style.Painted)
        {
            return;
        }

        // Cleaning runs in user coordinates; area and containment are invariant under the later
        // artboard mapping, which is a translation plus a Y flip.
        SvgAffine combined = SvgAffine.Multiply(parentTransform, transform);
        var cleaned = new List<SvgContour>(contours.Count);
        foreach (SvgContour contour in contours)
        {
            SvgContour? candidate = SvgContourCleaner.Clean(contour.Transform(combined));
            if (candidate is not null)
            {
                cleaned.Add(candidate);
            }
        }

        if (cleaned.Count == 0)
        {
            return;
        }

        _shapes.Add(new ShapeDraft(style.FillRule, cleaned));
    }

    private bool TryReadStyle(XElement element, SvgStyle inherited, out SvgStyle style)
    {
        style = inherited;

        SvgFillRule fillRule = inherited.FillRule;
        XAttribute? fillRuleAttribute = element.Attribute("fill-rule");
        if (fillRuleAttribute is not null)
        {
            string value = fillRuleAttribute.Value.Trim();
            if (string.Equals(value, "nonzero", StringComparison.Ordinal))
            {
                fillRule = SvgFillRule.NonZero;
            }
            else if (string.Equals(value, "evenodd", StringComparison.Ordinal))
            {
                fillRule = SvgFillRule.EvenOdd;
            }
            else
            {
                ReportError(
                    element,
                    $"has an invalid 'fill-rule' value '{fillRuleAttribute.Value}'; expected "
                        + "'nonzero' or 'evenodd'");
                return false;
            }
        }

        bool painted = inherited.Painted;
        XAttribute? fill = element.Attribute("fill");
        if (fill is not null)
        {
            string value = fill.Value.Trim();
            painted = !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase);
        }

        style = new SvgStyle(painted, fillRule);
        return true;
    }

    private bool TryReadTransform(XElement element, out SvgAffine transform)
    {
        transform = SvgAffine.Identity;
        XAttribute? attribute = element.Attribute("transform");
        if (attribute is null)
        {
            return true;
        }

        if (!SvgTransformParser.TryParse(attribute.Value, out transform, out string message))
        {
            transform = SvgAffine.Identity;
            ReportError(element, $"has an invalid 'transform' attribute: {message}");
            return false;
        }

        return true;
    }

    private bool TryReadArtboard(XElement root, out Artboard? artboard)
    {
        artboard = null;

        XAttribute? viewBox = root.Attribute("viewBox");
        if (viewBox is not null)
        {
            if (!TryParseViewBox(viewBox.Value, out Artboard parsed))
            {
                ReportError(root, $"has an invalid 'viewBox' value '{viewBox.Value}'");
                return false;
            }

            artboard = parsed;
            return true;
        }

        bool hasWidth = root.Attribute("width") is not null;
        bool hasHeight = root.Attribute("height") is not null;
        if (!hasWidth && !hasHeight)
        {
            return true;
        }

        bool valid = true;
        double width = 0;
        double height = 0;
        if (hasWidth && !TryReadPositiveLength(root, "width", out width))
        {
            valid = false;
        }

        if (hasHeight && !TryReadPositiveLength(root, "height", out height))
        {
            valid = false;
        }

        if (!valid)
        {
            return false;
        }

        if (hasWidth && hasHeight)
        {
            artboard = new Artboard(0, 0, width, height);
        }

        return true;
    }

    /// <summary>
    /// Parses the four unitless numbers of a <c>viewBox</c>.
    /// </summary>
    /// <remarks>
    /// The brief requires a positive width and height; a zero or negative extent would describe an
    /// empty artboard and is reported as invalid content instead of silently falling back.
    /// </remarks>
    private static bool TryParseViewBox(string text, out Artboard artboard)
    {
        artboard = default;
        ReadOnlySpan<char> span = text.AsSpan();
        double[] values = new double[4];
        int position = 0;
        for (int index = 0; index < values.Length; index++)
        {
            SvgNumberParser.SkipSeparators(span, ref position);
            if (!SvgNumberParser.TryRead(span, ref position, out values[index])
                || !double.IsFinite(values[index]))
            {
                return false;
            }
        }

        SvgNumberParser.SkipSeparators(span, ref position);
        if (position != span.Length || values[2] <= 0 || values[3] <= 0)
        {
            return false;
        }

        artboard = new Artboard(values[0], values[1], values[2], values[3]);
        return true;
    }

    private bool TryReadPositiveLength(XElement element, string name, out double value)
    {
        value = 0;
        XAttribute? attribute = element.Attribute(name);
        if (attribute is null)
        {
            return false;
        }

        if (!SvgNumberParser.TryParseLength(attribute.Value, out value))
        {
            ReportError(element, $"has an invalid '{name}' length '{attribute.Value}'");
            return false;
        }

        if (value <= 0)
        {
            ReportError(element, $"has a non-positive '{name}' length '{attribute.Value}'");
            return false;
        }

        return true;
    }

    private Artboard? FromBounds()
    {
        SvgBounds bounds = SvgBounds.Empty;
        foreach (ShapeDraft draft in _shapes)
        {
            foreach (SvgContour contour in draft.Contours)
            {
                foreach (SvgSegment segment in contour.Segments)
                {
                    segment.IncludeBounds(ref bounds);
                }
            }
        }

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        return new Artboard(bounds.MinX, bounds.MinY, bounds.Width, bounds.Height);
    }

    private SvgArtwork? NoGeometry()
    {
        (int? line, int? column) = SvgXmlLocation.Get(_root);
        _diagnostics.Error(
            SvgDiagnosticCodes.NoGeometry,
            "The SVG source has no drawable fill geometry.",
            line,
            column);
        return null;
    }

    private void ReportError(XElement element, string message)
    {
        (int? line, int? column) = SvgXmlLocation.Get(element);
        _diagnostics.Error(
            SvgDiagnosticCodes.ContentInvalid,
            $"SVG element '{element.Name.LocalName}' {message}.",
            line,
            column);
    }

    private void ReportIgnored(XElement element, string message)
    {
        (int? line, int? column) = SvgXmlLocation.Get(element);
        _diagnostics.Info(SvgDiagnosticCodes.FeatureIgnored, message, line, column);
    }

    private readonly record struct SvgStyle(bool Painted, SvgFillRule FillRule);

    private readonly record struct ShapeDraft(SvgFillRule FillRule, List<SvgContour> Contours);

    /// <summary>
    /// The resolved artboard rectangle in user coordinates.
    /// </summary>
    /// <remarks>
    /// All three artboard sources reduce to the same mapping: subtract the minimum corner, then flip Y
    /// about the rectangle's top edge so the result has its origin at the bottom-left with +Y up.
    /// </remarks>
    private readonly record struct Artboard(double MinX, double MinY, double Width, double Height)
    {
        internal SvgAffine Mapping => new(1, 0, 0, -1, -MinX, MinY + Height);
    }
}
