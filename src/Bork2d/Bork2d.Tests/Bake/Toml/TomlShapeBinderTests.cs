using System.Numerics;
using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Xunit;

namespace Bork2d.Tests.Bake.Toml;

/// <summary>
/// The §7.3 TOML mapping matrix and the §16.2/§16.3 binding diagnostics, driven through the
/// production parser and binder.
/// </summary>
public sealed class TomlShapeBinderTests
{
    private const string LeafToml = """
        flag = true
        i8 = -8
        u8 = 200
        i16 = -1600
        u16 = 60000
        i32 = -32000
        u32 = 4000000000
        i64 = -6400000000
        u64 = 6400000000
        nativeInt = 12
        nativeUInt = 13
        f32 = 1.5
        f64 = 2
        name = "probe"
        optionalName = "opt"
        id = "0f8fad5b-d9cb-469f-a165-70867728950e"
        timestamp = 1979-05-27T07:32:00Z
        offset = 1979-05-27T00:32:00.999999-07:00
        v2 = [1.0, 2.0]
        v3 = [1.0, 2.0, 3.0]
        v4 = [1.0, 2.0, 3.0, 4.0]
        tint = [1.0, 0.5, 0.25, 1.0]
        mood = "battleFury"
        texture = "textures/player"
        payload = "textures/raw/player"
        optional = 5
        optionalTint = [0.0, 1.0, 0.0, 1.0]
        tags = ["a", "b"]
        numbers = [1, 2, 3]
        names = ["x"]

        [counts]
        a = 1
        b = 2
        """;

    [Fact]
    public void EverySupportedLeafBinds()
    {
        LeafSet value = BinderTestHarness.BindOk<LeafSet>(LeafToml);

        Assert.True(value.Flag);
        Assert.Equal(-8, value.I8);
        Assert.Equal(200, value.U8);
        Assert.Equal(-1600, value.I16);
        Assert.Equal(60000, value.U16);
        Assert.Equal(-32000, value.I32);
        Assert.Equal(4000000000u, value.U32);
        Assert.Equal(-6400000000L, value.I64);
        Assert.Equal(6400000000UL, value.U64);
        Assert.Equal((nint)12, value.NativeInt);
        Assert.Equal((nuint)13, value.NativeUInt);
        Assert.Equal(1.5f, value.F32);
        Assert.Equal(2d, value.F64);
        Assert.Equal("probe", value.Name);
        Assert.Equal("opt", value.OptionalName);
        Assert.Equal(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), value.Id);
        Assert.Equal(DateTimeKind.Utc, value.Timestamp.Kind);
        Assert.Equal(new DateTime(1979, 5, 27, 7, 32, 0, DateTimeKind.Utc), value.Timestamp);
        Assert.Equal(TimeSpan.FromHours(-7), value.Offset.Offset);
        Assert.Equal(new Vector2(1f, 2f), value.V2);
        Assert.Equal(new Vector3(1f, 2f, 3f), value.V3);
        Assert.Equal(new Vector4(1f, 2f, 3f, 4f), value.V4);
        Assert.Equal(new Color(1f, 0.5f, 0.25f, 1f), value.Tint);
        Assert.Equal(Mood.BattleFury, value.Mood);
        Assert.Equal("textures/player", value.Texture.Path);
        Assert.Equal("textures/raw/player", value.Payload.Path);
        Assert.Equal(5, value.Optional);
        Assert.Equal(new Color(0f, 1f, 0f, 1f), value.OptionalTint);
        Assert.Equal(new[] { "a", "b" }, value.Tags!);
        Assert.Equal(new[] { 1, 2, 3 }, value.Numbers!);
        Assert.Equal(new[] { "x" }, value.Names!);
        Assert.Equal(2, value.Counts!["b"]);
    }

    [Fact]
    public void ReferencesAreRecordedWithExactValueSpans()
    {
        BindResult result = BinderTestHarness.Bind<LeafSet>(LeafToml);

        Assert.Empty(result.Diagnostics);
        Assert.Collection(
            result.References,
            site =>
            {
                Assert.Equal("texture", site.MemberPath);
                Assert.Equal("textures/player", site.ReferencePath);
                Assert.False(site.IsData);
                AssertLocation(LeafToml, "\"textures/player\"", site);
            },
            site =>
            {
                Assert.Equal("payload", site.MemberPath);
                Assert.Equal("textures/raw/player", site.ReferencePath);
                Assert.True(site.IsData);
                AssertLocation(LeafToml, "\"textures/raw/player\"", site);
            });
    }

    [Fact]
    public void AbsentMembersKeepInitializersAndDefaults()
    {
        LeafSet value = BinderTestHarness.BindOk<LeafSet>("i32 = 1\n");

        Assert.Equal("initial", value.Name);
        Assert.Null(value.OptionalName);
        Assert.Null(value.Tags);
        Assert.Null(value.Counts);
        Assert.Null(value.Optional);
        Assert.Equal(Mood.Calm, value.Mood);
        Assert.False(value.Texture.HasValue);
        Assert.False(value.Payload.HasValue);
    }

    [Fact]
    public void EmptyDocumentBindsAllDefaults()
    {
        LeafSet value = BinderTestHarness.BindOk<LeafSet>(string.Empty);

        Assert.Equal(0, value.I32);
        Assert.Equal("initial", value.Name);
    }

    [Fact]
    public void CamelCaseIsCaseInsensitiveOnRead()
    {
        LeafSet value = BinderTestHarness.BindOk<LeafSet>("I32 = 7\nName = \"x\"\n");

        Assert.Equal(7, value.I32);
        Assert.Equal("x", value.Name);
    }

    [Fact]
    public void NestedStructsCollectionsAndDictionariesBind()
    {
        const string toml = """
            name = "nested"

            [stats]
            attack = 1
            defense = 2

            [[history]]
            attack = 3
            defense = 4

            [[history]]
            attack = 5
            defense = 6

            [[log]]
            attack = 7
            defense = 8

            [map.apple]
            attack = 9
            defense = 10

            [maybe]
            attack = 11
            defense = 12

            [meta]
            icon = "data/icon"
            weight = 3

            [[items]]
            icon = "data/a"
            weight = 1

            [[items]]
            icon = "data/b"
            weight = 2
            """;

        NestedRoot value = BinderTestHarness.BindOk<NestedRoot>(toml);

        Assert.Equal("nested", value.Name);
        Assert.Equal(new Stats(1, 2), value.Stats);
        Assert.Equal([new Stats(3, 4), new Stats(5, 6)], value.History!);
        Assert.Equal([new Stats(7, 8)], value.Log!);
        Assert.Equal(new Stats(9, 10), value.Map!["apple"]);
        Assert.Equal(new Stats(11, 12), value.Maybe);
        Assert.Equal("data/icon", value.Meta!.Icon);
        Assert.Equal(3, value.Meta.Weight);
        Assert.Equal(2, value.Items!.Length);
        Assert.Equal("data/b", value.Items[1].Icon);
        Assert.Equal(2, value.Items[1].Weight);
    }

    [Fact]
    public void ReferencesInNestedTablesArraysAndDictionariesHaveMemberPathsAndSpans()
    {
        const string toml = """
            texture = "textures/player"

            [meta]
            icon = "data/icon"
            payload = "textures/raw/icon"

            [[items]]
            icon = "data/apple"
            payload = "textures/raw/apple"

            [[items]]
            icon = "data/pear"
            payload = "textures/raw/pear"

            [byName.first]
            icon = "data/first"
            payload = "textures/raw/first"
            """;

        BindResult result = BinderTestHarness.Bind<RefRoot>(toml);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            new[]
            {
                "texture",
                "meta.icon",
                "meta.payload",
                "items[0].icon",
                "items[0].payload",
                "items[1].icon",
                "items[1].payload",
                "byName[first].icon",
                "byName[first].payload",
            },
            result.References.Select(site => site.MemberPath));

        BakeReferenceSite pearIcon = result.References[5];
        Assert.Equal("data/pear", pearIcon.ReferencePath);
        Assert.False(pearIcon.IsData);
        AssertLocation(toml, "\"data/pear\"", pearIcon);

        BakeReferenceSite firstPayload = result.References[8];
        Assert.Equal("textures/raw/first", firstPayload.ReferencePath);
        Assert.True(firstPayload.IsData);
        AssertLocation(toml, "\"textures/raw/first\"", firstPayload);

        var value = (RefRoot)result.Value!;
        Assert.Equal("data/first", value.ByName!["first"].Icon.Path);
    }

    [Fact]
    public void InlineTablesAndArraysOfInlineTablesKeepExactSpans()
    {
        const string toml = """
            texture = "textures/player"

            meta = { icon = "data/icon", payload = "textures/raw/icon" }

            items = [
                { icon = "data/a", payload = "textures/raw/a" },
                { icon = "data/b", payload = "textures/raw/b" },
            ]
            """;

        BindResult result = BinderTestHarness.Bind<RefRoot>(toml);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            new[]
            {
                "texture",
                "meta.icon",
                "meta.payload",
                "items[0].icon",
                "items[0].payload",
                "items[1].icon",
                "items[1].payload",
            },
            result.References.Select(site => site.MemberPath));
        AssertLocation(toml, "\"data/b\"", result.References[5]);
        AssertLocation(toml, "\"textures/raw/b\"", result.References[6]);
    }

    [Fact]
    public void QuotedAndDottedKeysBindWithExactSpans()
    {
        const string toml = """
            "a.b" = 3
            nested.attack = 1
            nested.defense = 2
            """;

        KeyForms value = BinderTestHarness.BindOk<KeyForms>(toml);

        Assert.Equal(3, value.Quoted);
        Assert.Equal(new Stats(1, 2), value.Nested);

        const string broken = """
            "a.b" = "three"
            nested.attack = 1
            nested.defense = 2
            """;
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<KeyForms>(broken),
            BakeDiagnosticCodes.BinderTypeMismatch);
        AssertLocation(broken, "\"three\"", diagnostic);
    }

    [Fact]
    public void StructRootBindsThroughItsPrimaryConstructorAndKeepsInitializers()
    {
        const string toml = """
            attack = 10
            name = "root"
            tint = [0.1, 0.2, 0.3, 0.4]
            pivot = [0.5, 0.5]
            values = [1, 2, 3]

            [nested]
            attack = 8
            defense = 9
            """;

        StructRoot value = BinderTestHarness.BindOk<StructRoot>(toml);

        Assert.Equal(10, value.Attack);
        Assert.Equal("root", value.Name);
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), value.Tint);
        Assert.Equal(new Vector2(0.5f, 0.5f), value.Pivot);
        Assert.Equal(new Stats(8, 9), value.Nested);
        Assert.Equal(new[] { 1, 2, 3 }, value.Values);
        Assert.Equal(7, value.Bonus);
    }

    [Fact]
    public void PropertyNameIgnoreRequiredAndCSharpRequiredAreHonored()
    {
        const string toml = """
            custom_name = "renamed"
            must = "m"
            needed = "n"
            """;

        AttributeSet value = BinderTestHarness.BindOk<AttributeSet>(toml);

        Assert.Equal("renamed", value.CustomName);
        Assert.Equal("m", value.Must);
        Assert.Equal("n", value.Needed);
        Assert.Null(value.Optional);
        Assert.Equal("default", value.Ignored);
        Assert.Equal(3, value.Count);
    }

    [Fact]
    public void MissingJsonRequiredAndCSharpRequiredAreErrors()
    {
        BindResult result = BinderTestHarness.Bind<AttributeSet>("custom_name = \"x\"\n");

        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal(BakeDiagnosticCodes.BinderMissingRequired, diagnostic.Code));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.MemberPath == "must");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.MemberPath == "needed");
    }

    [Fact]
    public void IgnoredMemberKeyIsAnUnknownKeyError()
    {
        BindResult result = BinderTestHarness.Bind<AttributeSet>(
            "must = \"m\"\nneeded = \"n\"\nignored = \"value\"\n");

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            result,
            BakeDiagnosticCodes.BinderUnknownKey);
        Assert.Equal("ignored", diagnostic.MemberPath);
        Assert.Contains("ignored", diagnostic.Message, StringComparison.Ordinal);
        AssertLocation("must = \"m\"\nneeded = \"n\"\nignored = \"value\"\n", "ignored =", diagnostic);
    }

    [Fact]
    public void UnknownKeyIsAnErrorLocatedAtTheKey()
    {
        const string toml = "i32 = 1\nhp = 3\n";

        BindResult result = BinderTestHarness.Bind<LeafSet>(toml);

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            result,
            BakeDiagnosticCodes.BinderUnknownKey);
        Assert.Equal("hp", diagnostic.MemberPath);
        AssertLocation(toml, "hp =", diagnostic);
    }

    [Fact]
    public void TypeMismatchIsAnErrorLocatedAtTheValue()
    {
        const string toml = "i32 = \"seven\"\n";

        BindResult result = BinderTestHarness.Bind<LeafSet>(toml);

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            result,
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Equal("i32", diagnostic.MemberPath);
        Assert.Contains("must be an integer", diagnostic.Message, StringComparison.Ordinal);
        AssertLocation(toml, "\"seven\"", diagnostic);
    }

    [Fact]
    public void NumericRangeFailureNamesTheRangeAndTheValue()
    {
        const string toml = "u8 = 300\n";

        BindResult result = BinderTestHarness.Bind<LeafSet>(toml);

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            result,
            BakeDiagnosticCodes.BinderNumericRange);
        Assert.Contains("0 and 255", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("300", diagnostic.Message, StringComparison.Ordinal);
        AssertLocation(toml, "300", diagnostic);
    }

    [Fact]
    public void NegativeUnsignedValueIsARangeFailure()
    {
        BindResult result = BinderTestHarness.Bind<LeafSet>("u32 = -1\n");

        _ = BinderTestHarness.Single(result, BakeDiagnosticCodes.BinderNumericRange);
    }

    [Fact]
    public void FloatIsNeverTruncatedToAnInteger()
    {
        const string toml = "i32 = 1.5\n";

        BindResult result = BinderTestHarness.Bind<LeafSet>(toml);

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            result,
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Contains("never truncated", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatLeafAcceptsAnInteger()
    {
        LeafSet value = BinderTestHarness.BindOk<LeafSet>("f32 = 3\nf64 = 4\n");

        Assert.Equal(3f, value.F32);
        Assert.Equal(4d, value.F64);
    }

    [Fact]
    public void EnumRequiresTheExactCamelCaseMemberName()
    {
        Assert.Equal(
            Mood.BattleFury,
            BinderTestHarness.BindOk<LeafSet>("mood = \"battleFury\"\n").Mood);

        BakeDiagnostic wrongCase = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("mood = \"BattleFury\"\n"),
            BakeDiagnosticCodes.BinderEnumValue);
        Assert.Contains("battleFury", wrongCase.Message, StringComparison.Ordinal);

        BakeDiagnostic integer = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("mood = 1\n"),
            BakeDiagnosticCodes.BinderEnumValue);
        Assert.Contains("integer", integer.Message, StringComparison.Ordinal);

        _ = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("mood = \"battle_fury\"\n"),
            BakeDiagnosticCodes.BinderEnumValue);
    }

    [Fact]
    public void LocalDateTimeIsRejectedAndOffsetDateTimeIsConvertedToUtc()
    {
        BakeDiagnostic local = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("timestamp = 1979-05-27T07:32:00\n"),
            BakeDiagnosticCodes.BinderLocalDateTime);
        Assert.Equal("timestamp", local.MemberPath);

        LeafSet value = BinderTestHarness.BindOk<LeafSet>(
            "timestamp = 1979-05-27T09:32:00+02:00\n");
        Assert.Equal(new DateTime(1979, 5, 27, 7, 32, 0, DateTimeKind.Utc), value.Timestamp);
    }

    [Fact]
    public void DateTimeOffsetKeepsTheOffset()
    {
        LeafSet value = BinderTestHarness.BindOk<LeafSet>(
            "offset = 1979-05-27T09:32:00+02:00\n");

        Assert.Equal(TimeSpan.FromHours(2), value.Offset.Offset);
    }

    [Fact]
    public void InvalidGuidIsAnError()
    {
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("id = \"nope\"\n"),
            BakeDiagnosticCodes.BinderInvalidValue);
        Assert.Equal("id", diagnostic.MemberPath);
    }

    [Fact]
    public void VectorRequiresOneNumberPerComponent()
    {
        BakeDiagnostic shortVector = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("v2 = [1.0]\n"),
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Equal("v2", shortVector.MemberPath);

        BakeDiagnostic wrongElement = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("v2 = [\"a\", 1.0]\n"),
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Equal("v2[0]", wrongElement.MemberPath);
    }

    [Fact]
    public void ColorRequiresFourComponentsThroughItsMarshaler()
    {
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("tint = [1.0, 0.5, 0.25]\n"),
            BakeDiagnosticCodes.BinderInvalidValue);
        Assert.Equal("tint", diagnostic.MemberPath);
    }

    [Fact]
    public void UnsupportedLeavesAreDiagnosed()
    {
        const string toml = """
            anything = 1
            letter = "x"
            money = 1.5
            span = 100
            """;

        BindResult result = BinderTestHarness.Bind<UnsupportedSet>(toml);

        Assert.Equal(4, result.Diagnostics.Count);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal(BakeDiagnosticCodes.BinderUnsupportedLeaf, diagnostic.Code));
        Assert.Equal(
            new[] { "anything", "letter", "money", "span" },
            result.Diagnostics.Select(diagnostic => diagnostic.MemberPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NonStringDictionaryKeysAreRejected()
    {
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<IntDictionary>("[byId]\none = \"a\"\n"),
            BakeDiagnosticCodes.BinderUnsupportedCollection);
        Assert.Equal("byId", diagnostic.MemberPath);
    }

    [Fact]
    public void Utf8AndTomlEscapesBind()
    {
        LeafSet value = BinderTestHarness.BindOk<LeafSet>(
            "name = \"caf\u00e9\"\noptionalName = \"line\\nbreak\"\n");

        Assert.Equal("caf\u00e9", value.Name);
        Assert.Equal("line\nbreak", value.OptionalName);
    }

    [Fact]
    public void DuplicateKeysAreSyntaxErrors()
    {
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("i32 = 1\ni32 = 2\n"),
            BakeDiagnosticCodes.TomlSyntax);
        Assert.NotNull(diagnostic.Line);
    }

    [Fact]
    public void KeysThatDifferOnlyByCaseAreAmbiguousForOneMember()
    {
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("I32 = 1\ni32 = 2\n"),
            BakeDiagnosticCodes.BinderAmbiguousKey);
        Assert.Equal("i32", diagnostic.MemberPath);
    }

    [Fact]
    public void MissingCSharpRequiredMemberOnANestedTypeIsAnError()
    {
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<NestedRoot>("[meta]\nweight = 1\n"),
            BakeDiagnosticCodes.BinderMissingRequired);
        Assert.Equal("name", diagnostic.MemberPath);
    }

    [Fact]
    public void NonArrayForCollectionIsATypeMismatch()
    {
        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<LeafSet>("tags = \"not-an-array\"\n"),
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Equal("tags", diagnostic.MemberPath);
    }

    [Fact]
    public void ErrorInsideAnArrayElementKeepsTheElementPath()
    {
        const string toml = """
            name = "nested"
            [[history]]
            attack = "one"
            defense = 2
            """;

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            BinderTestHarness.Bind<NestedRoot>(toml),
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Equal("history[0].attack", diagnostic.MemberPath);
        AssertLocation(toml, "\"one\"", diagnostic);
    }

    [Fact]
    public void SyntaxErrorsAreReportedBeforeBinding()
    {
        BindResult result = BinderTestHarness.Bind<LeafSet>("i32 = \n");

        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, diagnostic.Code);
        Assert.Null(result.Value);
    }

    private static void AssertLocation(string toml, string needle, BakeDiagnostic diagnostic)
        => AssertLocation(toml, needle, diagnostic.Line, diagnostic.Column);

    private static void AssertLocation(string toml, string needle, BakeReferenceSite site)
        => AssertLocation(toml, needle, site.Line, site.Column);

    private static void AssertLocation(string toml, string needle, int? line, int? column)
    {
        int offset = toml.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(offset >= 0, $"'{needle}' is not part of the test source.");

        int expectedLine = 1;
        int lineStart = 0;
        for (int i = 0; i < offset; i++)
        {
            if (toml[i] == '\n')
            {
                expectedLine++;
                lineStart = i + 1;
            }
        }

        int expectedColumn = offset - lineStart + 1;
        Assert.Equal(expectedLine, line);
        Assert.Equal(expectedColumn, column);
    }
}
