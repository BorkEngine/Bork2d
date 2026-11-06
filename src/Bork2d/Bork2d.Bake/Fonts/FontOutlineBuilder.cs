using Season.MSDF;
using StbTrueTypeSharp;
using MsdfContour = Season.MSDF.Contour;
using MsdfShape = Season.MSDF.Shape;
using MsdfVector2 = Season.MSDF.Vector2;

namespace Bork2d.Bake.Fonts;

/// <summary>
/// Builds a SeasonMSDF <see cref="MsdfShape"/> from the vertices StbTrueTypeSharp returns for one
/// glyph.
/// </summary>
/// <remarks>
/// <para>
/// The builder consumes the <c>stbtt_vertex</c> stream: a move starts a contour, a line closes the
/// current point, a quadratic uses one control point, and a cubic uses two. The last point of every
/// contour is joined back to its first point when they differ, so no contour stays open. Coordinates
/// are divided by the font's units per em, so the shape is in em units.
/// </para>
/// <para>
/// After the contours are built, the shape is oriented for the nonzero winding rule, normalized, and
/// edge-colored with the default simple coloring. The result matches the pipeline the SVG MSDF bake
/// uses: a positive signed distance is inside the glyph.
/// </para>
/// </remarks>
internal static class FontOutlineBuilder
{
    /// <summary>The <c>STBTT_vmove</c> vertex type.</summary>
    internal const byte VertexMove = 1;

    /// <summary>The <c>STBTT_vline</c> vertex type.</summary>
    internal const byte VertexLine = 2;

    /// <summary>The <c>STBTT_vcurve</c> quadratic vertex type.</summary>
    internal const byte VertexQuadratic = 3;

    /// <summary>The <c>STBTT_vcubic</c> vertex type.</summary>
    internal const byte VertexCubic = 4;

    private const double EdgeColoringAngleThreshold = Math.PI / 60;

    /// <summary>Builds one glyph shape from its TrueType/CFF vertex stream.</summary>
    /// <param name="vertices">The glyph's vertices in font units, in contour order.</param>
    /// <param name="unitsPerEm">The font's units per em; positive.</param>
    /// <param name="shape">The built shape, or an empty shape when the stream has no edges.</param>
    /// <param name="failure">The reason the outline cannot be represented, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the shape was built and validated.</returns>
    internal static bool TryBuild(
        ReadOnlySpan<StbTrueType.stbtt_vertex> vertices,
        int unitsPerEm,
        out MsdfShape shape,
        out string? failure)
    {
        shape = new MsdfShape();
        failure = null;

        if (unitsPerEm <= 0)
        {
            failure = "the font's units-per-em value is not positive";
            return false;
        }

        double scale = 1.0 / unitsPerEm;
        MsdfContour? contour = null;
        double startX = 0;
        double startY = 0;
        double currentX = 0;
        double currentY = 0;
        bool open = false;

        for (int index = 0; index < vertices.Length; index++)
        {
            StbTrueType.stbtt_vertex vertex = vertices[index];
            switch (vertex.type)
            {
                case VertexMove:
                    FlushContour(shape, contour, open, currentX, currentY, startX, startY);
                    contour = new MsdfContour();
                    open = true;
                    startX = currentX = vertex.x * scale;
                    startY = currentY = vertex.y * scale;
                    break;

                case VertexLine:
                    if (contour is null)
                    {
                        failure = "a line segment appears before the first contour move";
                        return false;
                    }

                    contour.AddEdge(new EdgeHolder(
                        new MsdfVector2(currentX, currentY),
                        new MsdfVector2(vertex.x * scale, vertex.y * scale),
                        EdgeColor.WHITE));
                    currentX = vertex.x * scale;
                    currentY = vertex.y * scale;
                    break;

                case VertexQuadratic:
                    if (contour is null)
                    {
                        failure = "a quadratic segment appears before the first contour move";
                        return false;
                    }

                    contour.AddEdge(new EdgeHolder(
                        new MsdfVector2(currentX, currentY),
                        new MsdfVector2(vertex.cx * scale, vertex.cy * scale),
                        new MsdfVector2(vertex.x * scale, vertex.y * scale),
                        EdgeColor.WHITE));
                    currentX = vertex.x * scale;
                    currentY = vertex.y * scale;
                    break;

                case VertexCubic:
                    if (contour is null)
                    {
                        failure = "a cubic segment appears before the first contour move";
                        return false;
                    }

                    contour.AddEdge(new EdgeHolder(
                        new MsdfVector2(currentX, currentY),
                        new MsdfVector2(vertex.cx * scale, vertex.cy * scale),
                        new MsdfVector2(vertex.cx1 * scale, vertex.cy1 * scale),
                        new MsdfVector2(vertex.x * scale, vertex.y * scale),
                        EdgeColor.WHITE));
                    currentX = vertex.x * scale;
                    currentY = vertex.y * scale;
                    break;

                default:
                    failure = $"the vertex type {vertex.type} is not a supported outline command";
                    return false;
            }
        }

        FlushContour(shape, contour, open, currentX, currentY, startX, startY);
        if (shape.Contours.Count == 0)
        {
            return true;
        }

        shape.OrientContours();
        shape.Normalize();
        if (!shape.Validate())
        {
            failure = "the outline failed SeasonMSDF shape validation";
            return false;
        }

        EdgeColoring.EdgeColoringSimple(shape, EdgeColoringAngleThreshold, seed: 0);
        return true;
    }

    private static void FlushContour(
        MsdfShape shape,
        MsdfContour? contour,
        bool open,
        double currentX,
        double currentY,
        double startX,
        double startY)
    {
        if (contour is null)
        {
            return;
        }

        if (open && (currentX != startX || currentY != startY))
        {
            contour.AddEdge(new EdgeHolder(
                new MsdfVector2(currentX, currentY),
                new MsdfVector2(startX, startY),
                EdgeColor.WHITE));
        }

        if (contour.Edges.Count > 0)
        {
            shape.AddContour(contour);
        }
    }
}
