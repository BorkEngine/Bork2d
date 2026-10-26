using Bork2d.Bake.Diagnostics;
using Xunit;

namespace Bork2d.Tests.Bake.Diagnostics;

public sealed class BakeDiagnosticsTests
{
    [Fact]
    public void DiagnosticsSortByFileLineColumnCodeThenMessage()
    {
        var diagnostics = new BakeDiagnostics();
        diagnostics.Add(BakeSeverity.Error, "BK1002", "second", "/z/descriptor.toml", 2, 1);
        diagnostics.Add(BakeSeverity.Error, "BK1001", "later column", "/a/descriptor.toml", 1, 5);
        diagnostics.Add(BakeSeverity.Error, "BK1001", "b message", "/a/descriptor.toml", 1, 2);
        diagnostics.Add(BakeSeverity.Error, "BK1001", "a message", "/a/descriptor.toml", 1, 2);

        BakeDiagnostic[] sorted = [.. diagnostics.GetSorted()];

        Assert.Equal(
            new[]
            {
                "/a/descriptor.toml(1,2): error BK1001: a message",
                "/a/descriptor.toml(1,2): error BK1001: b message",
                "/a/descriptor.toml(1,5): error BK1001: later column",
                "/z/descriptor.toml(2,1): error BK1002: second",
            },
            sorted.Select(diagnostic => diagnostic.Format()));
    }

    [Fact]
    public void FormatUsesTheMsBuildCanonicalShape()
    {
        var diagnostic = new BakeDiagnostic(
            BakeSeverity.Error,
            "BK2301",
            "'hp' must be an integer; got a string.",
            "Assets/data/enemies/grunt.toml",
            12,
            17,
            "data/enemies/grunt",
            "hp");

        Assert.Equal(
            "Assets/data/enemies/grunt.toml(12,17): error BK2301: "
                + "'hp' must be an integer; got a string.",
            diagnostic.Format());
    }

    [Fact]
    public void FormatOmitsMissingLocations()
    {
        Assert.Equal(
            "error BK1001: no file",
            new BakeDiagnostic(BakeSeverity.Error, "BK1001", "no file", null, null, null, null, null)
                .Format());

        Assert.Equal(
            "descriptor.toml: warning BK1002: file only",
            new BakeDiagnostic(
                BakeSeverity.Warning,
                "BK1002",
                "file only",
                "descriptor.toml",
                null,
                null,
                null,
                null)
                .Format());

        Assert.Equal(
            "descriptor.toml(7): info BK1003: line only",
            new BakeDiagnostic(
                BakeSeverity.Info,
                "BK1003",
                "line only",
                "descriptor.toml",
                7,
                null,
                null,
                null)
                .Format());
    }

    [Fact]
    public void HasErrorsTracksOnlyErrorSeverity()
    {
        var diagnostics = new BakeDiagnostics();
        Assert.False(diagnostics.HasErrors);

        diagnostics.Add(BakeSeverity.Warning, "BK1001", "warning");
        diagnostics.Add(BakeSeverity.Info, "BK1001", "info");
        Assert.False(diagnostics.HasErrors);

        diagnostics.Add(BakeSeverity.Error, "BK1001", "error");
        Assert.True(diagnostics.HasErrors);
        Assert.Equal(3, diagnostics.Count);
    }

    [Fact]
    public void CodesStayInsideTheDocumentedRanges()
    {
        Assert.StartsWith("BK1", BakeDiagnosticCodes.DescriptorReadFailed, StringComparison.Ordinal);
        Assert.StartsWith("BK1", BakeDiagnosticCodes.DescriptorUnknownKey, StringComparison.Ordinal);
        Assert.StartsWith("BK2", BakeDiagnosticCodes.TomlSyntax, StringComparison.Ordinal);
    }
}
