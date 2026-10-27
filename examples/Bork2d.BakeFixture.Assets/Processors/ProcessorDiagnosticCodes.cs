namespace Bork2d.BakeFixture.Assets.Processors;

/// <summary>
/// The processor-framework diagnostic codes the fixture acceptance processors report.
/// </summary>
/// <remarks>
/// Processor code uses the <c>BK7xxx</c> range only. The codes are duplicated here as literals because
/// the fixture assets executable references the public builder, not the internal <c>Bork2d.Bake</c>
/// implementation that defines <c>BakeDiagnosticCodes</c>.
/// </remarks>
internal static class ProcessorDiagnosticCodes
{
    /// <summary>A processor source could not be read or parsed as its source format.</summary>
    internal const string SourceRead = "BK7001";

    /// <summary>A processor source is valid for its format but not valid processor input.</summary>
    internal const string InputInvalid = "BK7002";
}
