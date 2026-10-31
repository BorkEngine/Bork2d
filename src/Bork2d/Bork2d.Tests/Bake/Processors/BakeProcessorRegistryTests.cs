using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The processor registry contract: extension normalization, longest compound-suffix matching, latest
/// override with an informational diagnostic, and the <c>BK7007</c> invalid-registration matrix.
/// </summary>
public sealed class BakeProcessorRegistryTests
{
    [Fact]
    public void PngAndDotPngNormalizeToOneExtension()
    {
        var first = new RawTestProcessor("first", ["png"]);
        var second = new RawTestProcessor("second", [".PNG"]);
        var diagnostics = new BakeDiagnostics();

        Assert.True(BakeProcessorRegistry.TryCreate([first, second], diagnostics, out var registry));
        Assert.NotNull(registry);
        Assert.Equal(["png"], registry.Extensions);
        Assert.True(registry.TryMatch("art/Player.PNG", out BakeProcessorMatch match));
        Assert.Same(second, match.Processor);
        Assert.Equal("png", match.Extension);

        BakeDiagnostic overrideInfo = Assert.Single(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorExtensionOverride);
        Assert.Equal(BakeSeverity.Info, overrideInfo.Severity);
        Assert.Contains("first", overrideInfo.Message, StringComparison.Ordinal);
        Assert.Contains("second", overrideInfo.Message, StringComparison.Ordinal);
        Assert.Contains("png", overrideInfo.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LongestCompoundSuffixWinsCaseInsensitively()
    {
        var gzip = new RawTestProcessor("gzip", ["gz"]);
        var tarball = new RawTestProcessor("tarball", ["tar.gz"]);
        var diagnostics = new BakeDiagnostics();

        Assert.True(BakeProcessorRegistry.TryCreate([gzip, tarball], diagnostics, out var registry));
        Assert.NotNull(registry);
        Assert.Equal(["gz", "tar.gz"], registry.Extensions);

        Assert.True(registry.TryMatch("art/archive.tar.gz", out BakeProcessorMatch compound));
        Assert.Same(tarball, compound.Processor);
        Assert.Equal("tar.gz", compound.Extension);

        Assert.True(registry.TryMatch("art/archive.GZ", out BakeProcessorMatch single));
        Assert.Same(gzip, single.Processor);
        Assert.Equal("gz", single.Extension);

        Assert.True(registry.TryMatch("art/ARCHIVE.TAR.GZ", out BakeProcessorMatch uppercase));
        Assert.Same(tarball, uppercase.Processor);
    }

    [Fact]
    public void UnregisteredExtensionsAreNotMatched()
    {
        var diagnostics = new BakeDiagnostics();
        Assert.True(
            BakeProcessorRegistry.TryCreate([new RawTestProcessor("png", ["png"])], diagnostics, out var registry));
        Assert.NotNull(registry);

        Assert.False(registry.TryMatch("art/player.psd", out _));
        Assert.False(registry.TryMatch("art/player.png.toml", out _));
        Assert.False(registry.TryMatch("art/notpng", out _));
    }

    [Fact]
    public void AProcessorMayRegisterMultipleDistinctExtensions()
    {
        var processor = new RawTestProcessor("multi", ["png", ".JPG", " jpeg "]);
        var diagnostics = new BakeDiagnostics();

        Assert.True(BakeProcessorRegistry.TryCreate([processor], diagnostics, out var registry));
        Assert.NotNull(registry);
        Assert.Equal(["jpeg", "jpg", "png"], registry.Extensions);
        Assert.Empty(diagnostics.GetSorted());
    }

    [Fact]
    public void SameProcessorDeclaringOneExtensionTwiceIsNotAnOverride()
    {
        var processor = new RawTestProcessor("same", ["png", ".PNG"]);
        var diagnostics = new BakeDiagnostics();

        Assert.True(BakeProcessorRegistry.TryCreate([processor], diagnostics, out var registry));
        Assert.NotNull(registry);
        Assert.Equal(["png"], registry.Extensions);
        Assert.Empty(diagnostics.GetSorted());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("toml")]
    [InlineData(".TOML")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a:b")]
    [InlineData("p ng")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("tar..gz")]
    [InlineData("tar.")]
    public void InvalidExtensionsAreBK7007(string extension)
    {
        var diagnostics = new BakeDiagnostics();

        Assert.False(
            BakeProcessorRegistry.TryCreate(
                [new RawTestProcessor("bad-processor", [extension])],
                diagnostics,
                out BakeProcessorRegistry? registry));
        Assert.Null(registry);

        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics.GetSorted(),
            candidate => candidate.Code == BakeDiagnosticCodes.ProcessorRegistrationInvalid);
        Assert.Contains("bad-processor", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProcessorWithNoExtensionsIsInvalid()
    {
        var diagnostics = new BakeDiagnostics();

        Assert.False(
            BakeProcessorRegistry.TryCreate(
                [new RawTestProcessor("empty", [])],
                diagnostics,
                out _));

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorRegistrationInvalid, diagnostic.Code);
        Assert.Contains("empty", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyNameIsInvalid()
    {
        var diagnostics = new BakeDiagnostics();

        Assert.False(
            BakeProcessorRegistry.TryCreate(
                [new RawTestProcessor(" ", ["png"])],
                diagnostics,
                out _));

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorRegistrationInvalid, diagnostic.Code);
    }

    [Fact]
    public void NoneWithASidecarShapeIsInvalid()
    {
        var diagnostics = new BakeDiagnostics();

        Assert.False(
            BakeProcessorRegistry.TryCreate(
                [new RawTestProcessor(
                    "wrong-none",
                    ["png"],
                    BakeSidecarPolicy.None,
                    ShapeOf<TestSidecar>())],
                diagnostics,
                out _));

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorRegistrationInvalid, diagnostic.Code);
        Assert.Contains("wrong-none", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredWithoutASidecarShapeIsInvalid()
    {
        var diagnostics = new BakeDiagnostics();

        Assert.False(
            BakeProcessorRegistry.TryCreate(
                [new RawTestProcessor("wrong-required", ["png"], BakeSidecarPolicy.Required)],
                diagnostics,
                out _));

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorRegistrationInvalid, diagnostic.Code);
        Assert.Contains("wrong-required", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSidecarPolicyIsInvalid()
    {
        var diagnostics = new BakeDiagnostics();

        Assert.False(
            BakeProcessorRegistry.TryCreate(
                [new RawTestProcessor("bad-policy", ["png"], (BakeSidecarPolicy)42)],
                diagnostics,
                out _));

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorRegistrationInvalid, diagnostic.Code);
        Assert.Contains("bad-policy", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequiredProcessorWithAShapeIsValid()
    {
        var diagnostics = new BakeDiagnostics();

        Assert.True(
            BakeProcessorRegistry.TryCreate(
                [new RawTestProcessor(
                    "valid-required",
                    ["png"],
                    BakeSidecarPolicy.Required,
                    ShapeOf<TestSidecar>())],
                diagnostics,
                out BakeProcessorRegistry? registry));
        Assert.NotNull(registry);
        Assert.Empty(diagnostics.GetSorted());
    }

    private static ITypeShape ShapeOf<T>()
        where T : IShapeable<T>
        => T.GetTypeShape();
}
