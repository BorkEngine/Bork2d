using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// The SVG transform parser: every function, list ordering, nested group composition, and invalid
/// input.
/// </summary>
public sealed class SvgTransformParserTests
{
    [Fact]
    public void AbsentOrBlankTransformIsTheIdentity()
    {
        SvgTestSupport.AssertPoint(Parse(null).Transform(new SvgPoint(3, 4)), 3, 4);
        SvgTestSupport.AssertPoint(Parse(string.Empty).Transform(new SvgPoint(3, 4)), 3, 4);
        SvgTestSupport.AssertPoint(Parse("   ").Transform(new SvgPoint(3, 4)), 3, 4);
    }

    [Fact]
    public void TranslateUsesZeroForTheOptionalY()
    {
        SvgTestSupport.AssertPoint(Parse("translate(10,20)").Transform(new SvgPoint(1, 1)), 11, 21);
        SvgTestSupport.AssertPoint(Parse("translate(10)").Transform(new SvgPoint(1, 1)), 11, 1);
    }

    [Fact]
    public void ScaleUsesTheXFactorForTheOptionalY()
    {
        SvgTestSupport.AssertPoint(Parse("scale(2)").Transform(new SvgPoint(3, 4)), 6, 8);
        SvgTestSupport.AssertPoint(Parse("scale(2,3)").Transform(new SvgPoint(3, 4)), 6, 12);
    }

    [Fact]
    public void RotateTurnsAboutTheOrigin()
    {
        SvgAffine rotate = Parse("rotate(90)");

        SvgTestSupport.AssertPoint(rotate.Transform(new SvgPoint(1, 0)), 0, 1);
        SvgTestSupport.AssertPoint(rotate.Transform(new SvgPoint(0, 1)), -1, 0);
        SvgTestSupport.AssertNear(1, rotate.Determinant);
    }

    [Fact]
    public void RotateTurnsAboutTheGivenCenter()
    {
        SvgAffine rotate = Parse("rotate(90, 10, 20)");

        SvgTestSupport.AssertPoint(rotate.Transform(new SvgPoint(10, 20)), 10, 20);
        SvgTestSupport.AssertPoint(rotate.Transform(new SvgPoint(11, 20)), 10, 21);
    }

    [Fact]
    public void SkewXAndSkewYShearOneAxis()
    {
        SvgTestSupport.AssertPoint(Parse("skewX(45)").Transform(new SvgPoint(0, 1)), 1, 1);
        SvgTestSupport.AssertPoint(Parse("skewY(45)").Transform(new SvgPoint(1, 0)), 1, 1);
    }

    [Fact]
    public void MatrixMapsPointByTheSvgFormula()
    {
        SvgAffine matrix = Parse("matrix(1,2,3,4,5,6)");

        SvgTestSupport.AssertPoint(matrix.Transform(new SvgPoint(1, 1)), 9, 12);
        SvgTestSupport.AssertNear(-2, matrix.Determinant);
    }

    [Fact]
    public void TransformListAppliesTheRightmostFunctionFirst()
    {
        SvgAffine transform = Parse("translate(10,20) scale(2)");

        SvgTestSupport.AssertPoint(transform.Transform(new SvgPoint(1, 1)), 12, 22);
    }

    [Fact]
    public void SeparatorsMayBeCommasWhitespaceOrBoth()
    {
        SvgAffine transform = Parse("  translate( 10 , 20 )  scale( 2 )  ");

        SvgTestSupport.AssertPoint(transform.Transform(new SvgPoint(1, 1)), 12, 22);
    }

    [Fact]
    public void MultiplyAppliesTheChildFirst()
    {
        SvgAffine combined = SvgAffine.Multiply(
            SvgAffine.Translate(10, 0),
            SvgAffine.Scale(2, 2));

        SvgTestSupport.AssertPoint(combined.Transform(new SvgPoint(1, 0)), 12, 0);
    }

    [Fact]
    public void NestedGroupTransformsCompose()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<g transform=\"translate(10,20)\"><g transform=\"scale(2)\">"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" /></g></g>");

        SvgBounds bounds = SvgTestSupport.Bounds(SvgTestSupport.SingleShape(artwork).Contours);
        SvgTestSupport.AssertNear(10, bounds.MinX);
        SvgTestSupport.AssertNear(30, bounds.MaxX);
        SvgTestSupport.AssertNear(60, bounds.MinY);
        SvgTestSupport.AssertNear(80, bounds.MaxY);
    }

    [Fact]
    public void NegativeScaleIsAppliedAndNormalized()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<g transform=\"scale(-1,1)\">"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" /></g>");

        SvgContour contour = Assert.Single(SvgTestSupport.SingleShape(artwork).Contours);
        SvgBounds bounds = SvgTestSupport.Bounds(contour);
        SvgTestSupport.AssertNear(-10, bounds.MinX);
        SvgTestSupport.AssertNear(0, bounds.MaxX);
        Assert.True(SvgTestSupport.SignedArea(contour) > 0);
    }

    [Fact]
    public void UnknownFunctionIsAnError()
    {
        Assert.Contains("skewW", ParseFailure("skewW(10)"), StringComparison.Ordinal);
    }

    [Fact]
    public void WrongArgumentCountIsAnError()
    {
        Assert.Contains("6", ParseFailure("matrix(1 2 3)"), StringComparison.Ordinal);
        Assert.Contains("1 or 3", ParseFailure("rotate(10 20 30 40)"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnclosedFunctionIsAnError()
    {
        Assert.Contains("')'", ParseFailure("translate(10"), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOpenParenthesisIsAnError()
    {
        Assert.Contains("'('", ParseFailure("translate 10"), StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteArgumentIsAnError()
    {
        Assert.Contains("non-finite", ParseFailure("translate(1e999, 0)"), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedNumberIsAnError()
    {
        Assert.Contains("number", ParseFailure("translate(a)"), StringComparison.Ordinal);
    }

    private static SvgAffine Parse(string? text)
    {
        bool parsed = SvgTransformParser.TryParse(text, out SvgAffine transform, out string message);
        Assert.True(parsed, message);
        Assert.Empty(message);
        return transform;
    }

    private static string ParseFailure(string text)
    {
        bool parsed = SvgTransformParser.TryParse(text, out SvgAffine transform, out string message);

        Assert.False(parsed);
        Assert.Equal(SvgAffine.Identity, transform);
        Assert.NotEmpty(message);
        return message;
    }
}
