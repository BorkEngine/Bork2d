using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;
using Xunit;

namespace Bork2d.Tests.Bake.Project;

public sealed class ProjectDescriptorParserTests
{
    private const string MinimalDescriptor = """
        schema = 1

        [assets]
        root = "Assets"
        """;

    [Fact]
    public void MinimalDescriptorParsesWithDocumentedDefaults()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        ProjectDescriptor descriptor = Parse(directory, MinimalDescriptor);

        string projectDirectory = directory.RootPath;
        Assert.Null(descriptor.Name);
        Assert.Equal(Path.Combine(projectDirectory, "Assets"), descriptor.AssetsRoot);
        Assert.Equal(Path.Combine(projectDirectory, "assets.pck"), descriptor.PackPath);
        Assert.Equal(
            Path.Combine(projectDirectory, "obj", "bork2d", "bakehost"),
            descriptor.HostPath);
        Assert.Equal(BakeTextureFilter.Linear, descriptor.TextureFilter);
        Assert.Equal(BakeTextureWrap.Clamp, descriptor.TextureWrap);
        Assert.Equal(Path.Combine(projectDirectory, "bork2d.project.toml"), descriptor.DescriptorPath);
    }

    [Fact]
    public void FullDescriptorParsesEveryField()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        ProjectDescriptor descriptor = Parse(
            directory,
            """
            schema = 1
            name = "MyGame"

            [assets]
            root = "Assets"

            [bake]
            pack = "out/game.pck"
            host = "obj/host"

            [textures]
            filter = "nearest"
            wrap = "repeat"
            """);

        Assert.Equal("MyGame", descriptor.Name);
        Assert.Equal(Path.Combine(directory.RootPath, "out", "game.pck"), descriptor.PackPath);
        Assert.Equal(Path.Combine(directory.RootPath, "obj", "host"), descriptor.HostPath);
        Assert.Equal(BakeTextureFilter.Nearest, descriptor.TextureFilter);
        Assert.Equal(BakeTextureWrap.Repeat, descriptor.TextureWrap);
    }

    [Fact]
    public void RelativePathsResolveAgainstTheDescriptorDirectory()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("project/Assets");

        string descriptorPath = directory.WriteFile(
            "project/bork2d.project.toml",
            """
            schema = 1

            [assets]
            root = "Assets"

            [bake]
            pack = "../out/game.pck"
            host = "obj/host"
            """);
        var diagnostics = new BakeDiagnostics();

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(descriptorPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(descriptor);
        Assert.Equal(
            Path.Combine(directory.RootPath, "project", "Assets"),
            descriptor.AssetsRoot);
        Assert.Equal(
            Path.Combine(directory.RootPath, "out", "game.pck"),
            descriptor.PackPath);
        Assert.Equal(
            Path.Combine(directory.RootPath, "project", "obj", "host"),
            descriptor.HostPath);
    }

    [Fact]
    public void AbsolutePathsStayAbsolute()
    {
        using var directory = TempBakeDirectory.Create();
        string assetsRoot = directory.CreateDirectory("Elsewhere/Assets");
        string packPath = Path.Combine(directory.RootPath, "elsewhere", "game.pck");
        string hostPath = Path.Combine(directory.RootPath, "elsewhere", "host");
        string toml = string.Join(
            '\n',
            "schema = 1",
            string.Empty,
            "[assets]",
            $"root = '{ToTomlPath(assetsRoot)}'",
            string.Empty,
            "[bake]",
            $"pack = '{ToTomlPath(packPath)}'",
            $"host = '{ToTomlPath(hostPath)}'");
        string descriptorPath = directory.WriteFile("bork2d.project.toml", toml);
        var diagnostics = new BakeDiagnostics();

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(descriptorPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(descriptor);
        Assert.Equal(assetsRoot, descriptor.AssetsRoot);
        Assert.Equal(packPath, descriptor.PackPath);
        Assert.Equal(hostPath, descriptor.HostPath);
    }

    [Fact]
    public void DottedAndQuotedKeysAreSegmentAware()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        ProjectDescriptor descriptor = Parse(
            directory,
            """
            schema = 1
            "assets".root = "Assets"
            "textures".filter = "nearest"
            "textures".wrap = "repeat"
            """);

        Assert.Equal(Path.Combine(directory.RootPath, "Assets"), descriptor.AssetsRoot);
        Assert.Equal(BakeTextureFilter.Nearest, descriptor.TextureFilter);
        Assert.Equal(BakeTextureWrap.Repeat, descriptor.TextureWrap);
    }

    [Fact]
    public void TypesDescriptorKeyIsRejected()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [assets]
            root = "Assets"

            [[types]]
            prefix = "data"
            type = "MyGame.Data.GameConfig"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorUnknownKey, error.Code);
        Assert.Contains("types", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSchemaFails()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            [assets]
            root = "Assets"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorSchemaMissing, error.Code);
        Assert.EndsWith("bork2d.project.toml", error.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedSchemaFails()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 2

            [assets]
            root = "Assets"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorSchemaUnsupported, error.Code);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void SchemaWrongTypeFails()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = "1"

            [assets]
            root = "Assets"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorInvalidValue, error.Code);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void MissingAssetsTableFails()
    {
        using var directory = TempBakeDirectory.Create();

        BakeDiagnostic error = ParseError(directory, "schema = 1");

        Assert.Equal(BakeDiagnosticCodes.DescriptorMissingKey, error.Code);
        Assert.Contains("assets.root", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAssetsRootFailsWithTheTableLocation()
    {
        using var directory = TempBakeDirectory.Create();

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [assets]
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorMissingKey, error.Code);
        Assert.Contains("assets.root", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void AssetsRootMustExistAndBeADirectory()
    {
        using var directory = TempBakeDirectory.Create();
        string filePath = directory.WriteFile("Assets", "not a directory");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [assets]
            root = "Assets"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorAssetsRoot, error.Code);
        Assert.Contains(filePath, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKeyFailsWithLineAndColumn()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [assets]
            root = "Assets"
            unexpected = true
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorUnknownKey, error.Code);
        Assert.Contains("unexpected", error.Message, StringComparison.Ordinal);
        Assert.Equal(5, error.Line);
        Assert.Equal(1, error.Column);
    }

    [Fact]
    public void UnknownRootKeyFailsWithLineAndColumn()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1
            output = "assets.pck"

            [assets]
            root = "Assets"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorUnknownKey, error.Code);
        Assert.Contains("output", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, error.Line);
        Assert.Equal(1, error.Column);
    }

    [Fact]
    public void InvalidTextureFilterFailsWithTheValueLocation()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [assets]
            root = "Assets"

            [textures]
            filter = "cubic"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorTextureSetting, error.Code);
        Assert.Equal(7, error.Line);
        Assert.Equal(10, error.Column);
    }

    [Fact]
    public void InvalidTextureWrapFails()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [assets]
            root = "Assets"

            [textures]
            wrap = "mirror"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorTextureSetting, error.Code);
        Assert.Contains("mirror", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTomlKeyFailsWithItsLocation()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1
            schema = 2

            [assets]
            root = "Assets"
            """);

        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, error.Code);
        Assert.Equal(2, error.Line);
        Assert.Equal(1, error.Column);
    }

    [Fact]
    public void TomlSyntaxErrorKeepsItsSpan()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(directory, "schema =");

        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, error.Code);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void MissingDescriptorFileFailsWithoutThrowing()
    {
        using var directory = TempBakeDirectory.Create();
        var diagnostics = new BakeDiagnostics();

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(
            directory.FullPath("missing.toml"),
            diagnostics);

        Assert.Null(descriptor);
        BakeDiagnostic error = SingleError(diagnostics);
        Assert.Equal(BakeDiagnosticCodes.DescriptorReadFailed, error.Code);
    }

    [Fact]
    public void ParseRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ProjectDescriptorParser.Parse(null!, new BakeDiagnostics()));
        Assert.Throws<ArgumentNullException>(
            () => ProjectDescriptorParser.Parse("descriptor.toml", null!));
    }

    private static ProjectDescriptor Parse(TempBakeDirectory directory, string toml)
    {
        string descriptorPath = directory.WriteFile("bork2d.project.toml", toml);
        var diagnostics = new BakeDiagnostics();

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(descriptorPath, diagnostics);

        Assert.False(diagnostics.HasErrors, Describe(diagnostics));
        return Assert.IsType<ProjectDescriptor>(descriptor);
    }

    private static BakeDiagnostic ParseError(TempBakeDirectory directory, string toml)
    {
        string descriptorPath = directory.WriteFile("bork2d.project.toml", toml);
        var diagnostics = new BakeDiagnostics();

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(descriptorPath, diagnostics);

        Assert.Null(descriptor);
        return SingleError(diagnostics);
    }

    private static BakeDiagnostic SingleError(BakeDiagnostics diagnostics)
    {
        BakeDiagnostic[] errors = diagnostics.GetSorted()
            .Where(diagnostic => diagnostic.Severity == BakeSeverity.Error)
            .ToArray();
        return Assert.Single(errors);
    }

    private static string Describe(BakeDiagnostics diagnostics)
        => string.Join(
            Environment.NewLine,
            diagnostics.GetSorted().Select(diagnostic => diagnostic.Format()));

    private static string ToTomlPath(string path) => path.Replace('\\', '/');
}
