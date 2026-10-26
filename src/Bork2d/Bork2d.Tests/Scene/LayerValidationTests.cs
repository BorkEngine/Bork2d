using Bork2d.Api;
using Bork2d.Common.Scene;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Scene;

public sealed class LayerValidationTests
{
    [Fact]
    public void GameLayerIsValidAndAllReturnsEveryDeclaredFlag()
    {
        LayerValidation.Validate<GameLayer>();

        Assert.True(LayerValidation.IsValid<GameLayer>());
        Assert.Equal(GameLayer.World | GameLayer.Player | GameLayer.Ui, LayerValidation.All<GameLayer>());
    }

    [Fact]
    public void SparseSingleBitFlagsAreValid()
    {
        LayerValidation.Validate<SparseLayers>();

        Assert.True(LayerValidation.IsValid<SparseLayers>());
        Assert.Equal(5, (int)LayerValidation.All<SparseLayers>());
    }

    [Fact]
    public void FlagsEnumWithoutNonZeroBitsIsInvalid()
    {
        var exception = AssertInvalid<EmptyLayers>();

        Assert.Contains("no non-zero single-bit value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeNonZeroValueIsInvalid()
    {
        var exception = AssertInvalid<CompositeLayers>();

        Assert.Contains("composite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFlagsAttributeIsInvalid()
    {
        var exception = AssertInvalid<NotFlagsLayers>();

        Assert.Contains("missing [Flags]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonIntBackedEnumsAreInvalid()
    {
        AssertInvalid<LongLayers>();
        AssertInvalid<ByteLayers>();
    }

    [Fact]
    public void EngineCreationValidatesTheLayerEnum()
    {
        var exception = Assert.Throws<Bork2dException>(() =>
        {
            _ = new Engine<CompositeLayers>(
                new FakeTime(),
                new FakeWindow(),
                new FakeAssets());
        });

        Assert.Contains(nameof(CompositeLayers), exception.Message, StringComparison.Ordinal);
    }

    private static Bork2dException AssertInvalid<TLayer>()
        where TLayer : struct, Enum
    {
        var exception = Assert.Throws<Bork2dException>(() => { LayerValidation.Validate<TLayer>(); });

        Assert.False(LayerValidation.IsValid<TLayer>());
        Assert.Contains(typeof(TLayer).Name, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    [Flags]
    private enum SparseLayers
    {
        None = 0,
        A = 1,
        C = 4,
    }

    [Flags]
    private enum EmptyLayers
    {
        None = 0,
    }

    [Flags]
    private enum CompositeLayers
    {
        None = 0,
        A = 1,
        B = 2,
        Combined = A | B,
    }

    private enum NotFlagsLayers
    {
        None = 0,
        A = 1,
    }

    [Flags]
    private enum LongLayers : long
    {
        None = 0,
        A = 1,
    }

    [Flags]
    private enum ByteLayers : byte
    {
        None = 0,
        A = 1,
    }
}
