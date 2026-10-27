using Bork2d.Bake.Baking;
using Bork2d.Bake.Builder;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Tests.Bake;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// Builder-level processor configuration: invalid registrations are configuration errors with exit
/// code 2 before any source is read, and a later registration overrides an earlier one with an
/// informational diagnostic.
/// </summary>
public sealed class BakeProcessorConfigurationTests
{
    [Fact]
    public void InvalidExtensionIsBK7007AndCreatesNoProject()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .AddProcessor(new TestProcessor("bad-processor", "a/b"));

        var diagnostics = new BakeDiagnostics();
        BakeProject? project = builder.CreateProject(diagnostics);

        Assert.Null(project);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorRegistrationInvalid, diagnostic.Code);
        Assert.Contains("bad-processor", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidProcessorRegistrationExitsWithCodeTwoBeforeReadingSources()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .AddProcessor(new TestProcessor("bad-processor", "a/b"))
            .Build();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = application.Run(["validate"], output, error, "test-assets");

        Assert.Equal(2, exitCode);
        Assert.Contains("BK7007", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("bad-processor", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoneWithASidecarShapeIsBK7007AndCreatesNoProject()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .AddProcessor(new TestProcessor(
                "none-with-shape",
                "png",
                policy: SidecarPolicy.None,
                sidecarShape: ShapeOf<TestSidecar>()));

        var diagnostics = new BakeDiagnostics();
        Assert.Null(builder.CreateProject(diagnostics));
        Assert.Equal(
            BakeDiagnosticCodes.ProcessorRegistrationInvalid,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void RequiredWithoutASidecarShapeIsBK7007AndCreatesNoProject()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .AddProcessor(new TestProcessor(
                "required-without-shape",
                "png",
                policy: SidecarPolicy.Required));

        var diagnostics = new BakeDiagnostics();
        Assert.Null(builder.CreateProject(diagnostics));
        Assert.Equal(
            BakeDiagnosticCodes.ProcessorRegistrationInvalid,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void ALaterRegistrationOverridesAnEarlierOneWithAnInfo()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        var first = new TestProcessor("first", "png");
        var second = new TestProcessor("second", ".PNG");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .AddProcessor(first)
            .AddProcessor(second);

        var diagnostics = new BakeDiagnostics();
        BakeProject? project = builder.CreateProject(diagnostics);

        Assert.NotNull(project);
        Assert.Equal(["png"], project!.ProcessorRegistry.Extensions);
        Assert.True(project.ProcessorRegistry.TryMatch("art/Player.PNG", out BakeProcessorMatch match));
        Assert.Equal("second", match.Processor.Name);
        BakeDiagnostic info = Assert.Single(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorExtensionOverride);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Contains("first", info.Message, StringComparison.Ordinal);
        Assert.Contains("second", info.Message, StringComparison.Ordinal);
    }

    private static ITypeShape ShapeOf<T>()
        where T : IShapeable<T>
        => T.GetTypeShape();
}
