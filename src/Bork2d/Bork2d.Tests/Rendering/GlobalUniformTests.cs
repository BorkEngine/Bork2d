using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for engine-wide user shader uniforms: set/update/type-conflict/reset rules,
/// snapshot capture, snapshot reuse, and value-only capture of every overload.
/// </summary>
public sealed class GlobalUniformTests
{
    [Fact]
    public void SetGlobalUniformCapturesEveryType()
    {
        var engine = TestEngineFactory.Create();

        engine.SetGlobalUniform("f", 1.25f);
        engine.SetGlobalUniform("v2", new Vector2(2f, 3f));
        engine.SetGlobalUniform("v3", new Vector3(4f, 5f, 6f));
        engine.SetGlobalUniform("v4", new Vector4(7f, 8f, 9f, 10f));
        engine.SetGlobalUniform("c", new Color(0.1f, 0.2f, 0.3f, 0.4f));

        FrameSnapshot snapshot = Capture(engine);

        Assert.Equal(5, snapshot.GlobalUniformCount);
        Dictionary<string, GlobalUniformSnapshot> captured = ByName(snapshot);
        AssertUniform(captured["f"], ShaderUniformType.Float, new Vector4(1.25f, 0f, 0f, 0f));
        AssertUniform(captured["v2"], ShaderUniformType.Vector2, new Vector4(2f, 3f, 0f, 0f));
        AssertUniform(captured["v3"], ShaderUniformType.Vector3, new Vector4(4f, 5f, 6f, 0f));
        AssertUniform(captured["v4"], ShaderUniformType.Vector4, new Vector4(7f, 8f, 9f, 10f));
        AssertUniform(captured["c"], ShaderUniformType.Color, new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
    }

    [Fact]
    public void SettingTheSameNameWithTheSameTypeReplacesTheValue()
    {
        var engine = TestEngineFactory.Create();
        engine.SetGlobalUniform("scale", 1f);

        engine.SetGlobalUniform("scale", 2f);

        FrameSnapshot snapshot = Capture(engine);
        Assert.Equal(1, snapshot.GlobalUniformCount);
        AssertUniform(
            ByName(snapshot)["scale"],
            ShaderUniformType.Float,
            new Vector4(2f, 0f, 0f, 0f));
    }

    [Fact]
    public void SettingTheSameNameWithADifferentTypeThrowsAndKeepsTheOriginal()
    {
        var engine = TestEngineFactory.Create();
        engine.SetGlobalUniform("scale", 1f);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => engine.SetGlobalUniform("scale", new Vector2(2f, 3f)));

        Assert.Contains("scale", error.Message, StringComparison.Ordinal);
        Assert.Contains(ShaderUniformType.Float.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains(ShaderUniformType.Vector2.ToString(), error.Message, StringComparison.Ordinal);

        FrameSnapshot snapshot = Capture(engine);
        AssertUniform(
            ByName(snapshot)["scale"],
            ShaderUniformType.Float,
            new Vector4(1f, 0f, 0f, 0f));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyGlobalNamesThrow(string? name)
    {
        var engine = TestEngineFactory.Create();

        Assert.ThrowsAny<ArgumentException>(() => engine.SetGlobalUniform(name!, 1f));
    }

    [Fact]
    public void ResetGlobalUniformsClearsEveryStoredValue()
    {
        var engine = TestEngineFactory.Create();
        engine.SetGlobalUniform("a", 1f);
        engine.SetGlobalUniform("b", new Vector2(2f, 3f));
        Assert.Equal(2, Capture(engine).GlobalUniformCount);

        engine.ResetGlobalUniforms();

        Assert.Equal(0, Capture(engine).GlobalUniformCount);
        Assert.True(Capture(engine).GlobalUniforms.IsEmpty);
    }

    [Fact]
    public void ReusingASnapshotDropsGlobalsClearedBetweenCaptures()
    {
        var engine = TestEngineFactory.Create();
        engine.SetGlobalUniform("a", 1f);
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);
        Assert.Equal(1, snapshot.GlobalUniformCount);

        engine.ResetGlobalUniforms();
        engine.CaptureSnapshot(snapshot, sequence: 2);

        Assert.Equal(0, snapshot.GlobalUniformCount);
        Assert.True(snapshot.GlobalUniforms.IsEmpty);
    }

    [Fact]
    public void FrameSnapshotResetClearsTheGlobalBuffer()
    {
        var snapshot = new FrameSnapshot();
        snapshot.AddGlobalUniform(
            new GlobalUniformSnapshot("a", ShaderUniformType.Float, Vector4.One));
        Assert.Equal(1, snapshot.GlobalUniformCount);

        snapshot.Reset();

        Assert.Equal(0, snapshot.GlobalUniformCount);
        Assert.True(snapshot.GlobalUniforms.IsEmpty);
    }

    [Fact]
    public void GlobalsAndSpritesAreCapturedInTheSameSnapshot()
    {
        var engine = TestEngineFactory.Create();
        engine.SetGlobalUniform("timeScale", 2f);
        engine.CreateSpriteRender(engine.Root);

        FrameSnapshot snapshot = Capture(engine);

        Assert.Equal(1, snapshot.SpriteCount);
        Assert.Equal(1, snapshot.GlobalUniformCount);
    }

    [Fact]
    public async Task ConcurrentSetCallsStoreEveryDistinctName()
    {
        var engine = TestEngineFactory.Create();
        string[] names = [.. Enumerable.Range(0, 32).Select(index => $"uniform{index}")];

        Task[] setters = names
            .Select(name => Task.Run(() => engine.SetGlobalUniform(name, 1f)))
            .ToArray();
        await Task.WhenAll(setters);

        FrameSnapshot snapshot = Capture(engine);
        Assert.Equal(names.Length, snapshot.GlobalUniformCount);
        Dictionary<string, GlobalUniformSnapshot> captured = ByName(snapshot);
        foreach (string name in names)
        {
            Assert.True(captured.ContainsKey(name), $"Missing global '{name}'.");
        }
    }

    private static FrameSnapshot Capture(GameEngine<GameLayer> engine)
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);
        return snapshot;
    }

    private static Dictionary<string, GlobalUniformSnapshot> ByName(FrameSnapshot snapshot)
    {
        var captured = new Dictionary<string, GlobalUniformSnapshot>(StringComparer.Ordinal);
        foreach (GlobalUniformSnapshot uniform in snapshot.GlobalUniforms)
        {
            captured.Add(uniform.Name, uniform);
        }

        return captured;
    }

    private static void AssertUniform(
        GlobalUniformSnapshot uniform,
        ShaderUniformType type,
        Vector4 value)
    {
        Assert.Equal(type, uniform.Type);
        Assert.Equal(value, uniform.Value);
    }
}
