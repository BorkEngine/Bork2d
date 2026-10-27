using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;
using Xunit;

namespace Bork2d.Tests.Bake.Project;

public sealed class ProjectDescriptorParserTests
{
    private const string MinimalDescriptor = """
        schema = 1

        [[assets]]
        root = "Assets"
        pack = "assets.pck"
        """;

    [Fact]
    public void MinimalDescriptorParsesWithDocumentedDefaults()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        ProjectDescriptor descriptor = Parse(directory, MinimalDescriptor);

        string projectDirectory = directory.RootPath;
        ProjectPack main = Assert.Single(descriptor.Packs);
        Assert.True(main.IsMain);
        Assert.Equal(0, main.Index);
        Assert.Equal("Assets", main.RelativeRoot);
        Assert.Equal("assets.pck", main.PackPath);
        Assert.Equal(Path.Combine(projectDirectory, "Assets"), main.Root);
        Assert.Equal(
            Path.Combine(projectDirectory, "obj", "bork2d", "bakehost"),
            descriptor.HostPath);
        Assert.Equal(BakeTextureFilter.Linear, descriptor.TextureFilter);
        Assert.Equal(BakeTextureWrap.Clamp, descriptor.TextureWrap);
        Assert.Equal(Path.Combine(projectDirectory, "bork2d.project.toml"), descriptor.DescriptorPath);
    }

    [Fact]
    public void MultipleAssetsEntriesParseInOrderAndTheFirstIsMain()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        directory.CreateDirectory("DLC1");
        directory.CreateDirectory("DLC1/More");

        ProjectDescriptor descriptor = Parse(
            directory,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

            [[assets]]
            root = "DLC1"
            pack = "dlc1.pck"

            [[assets]]
            root = "DLC1/More"
            pack = "dlc/more.pck"
            """);

        ProjectDescriptor descriptorWithThree = descriptor;
        Assert.Equal(3, descriptorWithThree.Packs.Count);
        Assert.Same(descriptorWithThree.Packs[0], descriptorWithThree.MainPack);
        Assert.True(descriptorWithThree.Packs[0].IsMain);
        Assert.False(descriptorWithThree.Packs[1].IsMain);
        Assert.False(descriptorWithThree.Packs[2].IsMain);
        Assert.Equal(
            new[] { "assets.pck", "dlc1.pck", "dlc/more.pck" },
            descriptorWithThree.Packs.Select(pack => pack.PackPath));
        Assert.Equal(
            new[] { 0, 1, 2 },
            descriptorWithThree.Packs.Select(pack => pack.Index));
    }

    [Fact]
    public void FullDescriptorParsesEveryField()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        directory.CreateDirectory("DLC1");

        ProjectDescriptor descriptor = Parse(
            directory,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "out/game.pck"

            [[assets]]
            root = "DLC1"
            pack = "dlc/dlc1.pck"

            [bake]
            host = "obj/host"

            [textures]
            filter = "nearest"
            wrap = "repeat"
            """);

        Assert.Equal(
            new[] { "out/game.pck", "dlc/dlc1.pck" },
            descriptor.Packs.Select(pack => pack.PackPath));
        Assert.Equal(Path.Combine(directory.RootPath, "obj", "host"), descriptor.HostPath);
        Assert.Equal(BakeTextureFilter.Nearest, descriptor.TextureFilter);
        Assert.Equal(BakeTextureWrap.Repeat, descriptor.TextureWrap);
    }

    [Fact]
    public void RelativePathsResolveAgainstTheDescriptorDirectory()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("project/Assets");
        directory.CreateDirectory("project/DLC1");

        string descriptorPath = directory.WriteFile(
            "project/bork2d.project.toml",
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "packs/assets.pck"

            [[assets]]
            root = "DLC1"
            pack = "packs/dlc1.pck"

            [bake]
            host = "obj/host"
            """);
        var diagnostics = new BakeDiagnostics();

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(descriptorPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(descriptor);
        Assert.Equal(
            Path.Combine(directory.RootPath, "project", "Assets"),
            descriptor.Packs[0].Root);
        Assert.Equal("packs/assets.pck", descriptor.Packs[0].PackPath);
        Assert.Equal(
            Path.Combine(directory.RootPath, "project", "obj", "host"),
            descriptor.HostPath);
    }

    [Fact]
    public void RootDotDeclaresTheDescriptorDirectory()
    {
        using var directory = TempBakeDirectory.Create();

        ProjectDescriptor descriptor = Parse(
            directory,
            """
            schema = 1

            [[assets]]
            root = "."
            pack = "assets.pck"
            """);

        Assert.Equal(directory.RootPath, descriptor.MainPack.Root);
        Assert.Equal(".", descriptor.MainPack.RelativeRoot);
    }

    [Fact]
    public void AbsoluteAndTraversalPathsAreRejected()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        Assert.Equal(
            BakeDiagnosticCodes.DescriptorInvalidValue,
            ParseError(
                directory,
                $"""
                schema = 1

                [[assets]]
                root = '{ToTomlPath(directory.FullPath("Assets"))}'
                pack = "assets.pck"
                """).Code);
        Assert.Equal(
            BakeDiagnosticCodes.DescriptorInvalidValue,
            ParseError(
                directory,
                """
                schema = 1

                [[assets]]
                root = "../Assets"
                pack = "assets.pck"
                """).Code);
        Assert.Equal(
            BakeDiagnosticCodes.DescriptorInvalidValue,
            ParseError(
                directory,
                """
                schema = 1

                [[assets]]
                root = "Assets"
                pack = "../assets.pck"
                """).Code);
        Assert.Equal(
            BakeDiagnosticCodes.DescriptorInvalidValue,
            ParseError(
                directory,
                $"""
                schema = 1

                [[assets]]
                root = "Assets"
                pack = '{ToTomlPath(Path.Combine(directory.RootPath, "assets.pck"))}'
                """).Code);
        Assert.Equal(
            BakeDiagnosticCodes.DescriptorInvalidValue,
            ParseError(
                directory,
                """
                schema = 1

                [[assets]]
                root = "Assets"
                pack = "packs/../assets.pck"
                """).Code);
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
            "textures".filter = "nearest"
            "textures".wrap = "repeat"

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);

        Assert.Single(descriptor.Packs);
        Assert.Equal("assets.pck", descriptor.MainPack.PackPath);
        Assert.Equal(BakeTextureFilter.Nearest, descriptor.TextureFilter);
        Assert.Equal(BakeTextureWrap.Repeat, descriptor.TextureWrap);
    }

    [Fact]
    public void ProjectNameAndBakePackKeysAreRejected()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic nameError = ParseError(
            directory,
            """
            schema = 1
            name = "MyGame"

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);
        Assert.Equal(BakeDiagnosticCodes.DescriptorUnknownKey, nameError.Code);
        Assert.Contains("name", nameError.Message, StringComparison.Ordinal);

        BakeDiagnostic packError = ParseError(
            directory,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

            [bake]
            pack = "old.pck"
            """);
        Assert.Equal(BakeDiagnosticCodes.DescriptorUnknownKey, packError.Code);
        Assert.Contains("bake.pack", packError.Message, StringComparison.Ordinal);
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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

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
            [[assets]]
            root = "Assets"
            pack = "assets.pck"
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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorInvalidValue, error.Code);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void MissingAssetsListFails()
    {
        using var directory = TempBakeDirectory.Create();

        BakeDiagnostic error = ParseError(directory, "schema = 1");

        Assert.Equal(BakeDiagnosticCodes.DescriptorMissingKey, error.Code);
        Assert.Contains("assets", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAssetsListFails()
    {
        using var directory = TempBakeDirectory.Create();

        BakeDiagnostic error = ParseError(directory, "schema = 1\nassets = []");

        Assert.Equal(BakeDiagnosticCodes.DescriptorInvalidValue, error.Code);
        Assert.Contains("at least one", error.Message, StringComparison.Ordinal);
        Assert.Contains("[[assets]]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleAssetsTableIsRejectedWithTheArrayOfTablesRule()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [assets]
            root = "Assets"
            pack = "assets.pck"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorInvalidValue, error.Code);
        Assert.Contains("[[assets]]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRootOrPackInAnEntryFails()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic missingRoot = ParseError(
            directory,
            """
            schema = 1

            [[assets]]
            pack = "assets.pck"
            """);
        Assert.Equal(BakeDiagnosticCodes.DescriptorMissingKey, missingRoot.Code);
        Assert.Contains("assets[0].root", missingRoot.Message, StringComparison.Ordinal);

        BakeDiagnostic missingPack = ParseError(
            directory,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            """);
        Assert.Equal(BakeDiagnosticCodes.DescriptorMissingKey, missingPack.Code);
        Assert.Contains("assets[0].pack", missingPack.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetsRootMustExistAndBeADirectory()
    {
        using var directory = TempBakeDirectory.Create();
        string filePath = directory.WriteFile("Assets", "not a directory");

        BakeDiagnostic error = ParseError(directory, MinimalDescriptor);

        Assert.Equal(BakeDiagnosticCodes.DescriptorAssetsRoot, error.Code);
        Assert.Contains(filePath, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRootIsRejected()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

            [[assets]]
            root = "Assets"
            pack = "dlc1.pck"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorDuplicateRoot, error.Code);
        Assert.Contains("more than one", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatePackPathIsRejected()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        directory.CreateDirectory("DLC1");

        BakeDiagnostic error = ParseError(
            directory,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

            [[assets]]
            root = "DLC1"
            pack = "assets.pck"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorDuplicatePack, error.Code);
        Assert.Contains("unique", error.Message, StringComparison.Ordinal);
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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            unexpected = true
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorUnknownKey, error.Code);
        Assert.Contains("unexpected", error.Message, StringComparison.Ordinal);
        Assert.Equal(6, error.Line);
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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

            [textures]
            filter = "cubic"
            """);

        Assert.Equal(BakeDiagnosticCodes.DescriptorTextureSetting, error.Code);
        Assert.Equal(8, error.Line);
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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

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

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
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
