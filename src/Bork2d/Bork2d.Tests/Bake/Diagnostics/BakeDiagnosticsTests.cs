using Bork2d.Bake.Diagnostics;
using Xunit;

namespace Bork2d.Tests.Bake.Diagnostics;

public sealed class BakeDiagnosticsTests
{
    [Fact]
    public void DiagnosticsSortByFileLineColumnCodeThenMessage()
    {
        var diagnostics = new BakeDiagnostics();
        diagnostics.Add(BakeSeverity.Error, "BK1002", "second", "/z/project.toml", 2, 1);
        diagnostics.Add(BakeSeverity.Error, "BK1001", "later column", "/a/project.toml", 1, 5);
        diagnostics.Add(BakeSeverity.Error, "BK1001", "b message", "/a/project.toml", 1, 2);
        diagnostics.Add(BakeSeverity.Error, "BK1001", "a message", "/a/project.toml", 1, 2);

        BakeDiagnostic[] sorted = [.. diagnostics.GetSorted()];

        Assert.Equal(
            new[]
            {
                "/a/project.toml(1,2): error BK1001: a message",
                "/a/project.toml(1,2): error BK1001: b message",
                "/a/project.toml(1,5): error BK1001: later column",
                "/z/project.toml(2,1): error BK1002: second",
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
            "project.toml: warning BK1002: file only",
            new BakeDiagnostic(
                BakeSeverity.Warning,
                "BK1002",
                "file only",
                "project.toml",
                null,
                null,
                null,
                null)
                .Format());

        Assert.Equal(
            "project.toml(7): info BK1003: line only",
            new BakeDiagnostic(
                BakeSeverity.Info,
                "BK1003",
                "line only",
                "project.toml",
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
        Assert.StartsWith("BK1", BakeDiagnosticCodes.ProjectRootInvalid, StringComparison.Ordinal);
        Assert.StartsWith("BK1", BakeDiagnosticCodes.AssetTypeNotRegistered, StringComparison.Ordinal);
        Assert.StartsWith("BK1", BakeDiagnosticCodes.SourceTypeMissing, StringComparison.Ordinal);
        Assert.StartsWith("BK2", BakeDiagnosticCodes.TomlSyntax, StringComparison.Ordinal);
        Assert.StartsWith("BK5", BakeDiagnosticCodes.PackWriteFailed, StringComparison.Ordinal);
    }
}
