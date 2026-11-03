using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Toml;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Bake.Toml;

/// <summary>
/// The scalar-or-array number leaf the shader sidecar uses for uniform defaults: a scalar binds as
/// one component, an array keeps its components in source order, and anything else is the normal
/// <c>BK2004</c> type mismatch.
/// </summary>
public sealed class TomlNumberOrArrayTests
{
    [Fact]
    public void ScalarNumberBindsAsOneComponent()
    {
        NumberOrArraySet set = BinderTestHarness.BindOk<NumberOrArraySet>("value = 1.5\n");

        Assert.NotNull(set.Value);
        Assert.Equal(new[] { 1.5 }, set.Value.Values);
    }

    [Fact]
    public void IntegerScalarBindsAsOneComponent()
    {
        NumberOrArraySet set = BinderTestHarness.BindOk<NumberOrArraySet>("value = 2\n");

        Assert.NotNull(set.Value);
        Assert.Equal(new[] { 2.0 }, set.Value.Values);
    }

    [Fact]
    public void ArrayKeepsSourceOrder()
    {
        NumberOrArraySet set = BinderTestHarness.BindOk<NumberOrArraySet>(
            "value = [1, 2.5, -3.0]\n");

        Assert.NotNull(set.Value);
        Assert.Equal(new[] { 1.0, 2.5, -3.0 }, set.Value.Values);
    }

    [Fact]
    public void EmptyArrayBindsWithNoComponents()
    {
        NumberOrArraySet set = BinderTestHarness.BindOk<NumberOrArraySet>("value = []\n");

        Assert.NotNull(set.Value);
        Assert.Empty(set.Value.Values);
    }

    [Fact]
    public void WrongTypeIsBK2004()
    {
        BindResult result = BinderTestHarness.Bind<NumberOrArraySet>("value = \"text\"\n");

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            result,
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Contains("a number or an array of numbers", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonNumberArrayElementIsBK2004()
    {
        BindResult result = BinderTestHarness.Bind<NumberOrArraySet>("value = [1.0, \"text\"]\n");

        BakeDiagnostic diagnostic = BinderTestHarness.Single(
            result,
            BakeDiagnosticCodes.BinderTypeMismatch);
        Assert.Contains("value[1]", diagnostic.Message, StringComparison.Ordinal);
    }
}

/// <summary>A binder root whose only member is the scalar-or-array number leaf.</summary>
[GenerateShape]
internal sealed partial class NumberOrArraySet
{
    public TomlNumberOrArray? Value { get; set; }
}
