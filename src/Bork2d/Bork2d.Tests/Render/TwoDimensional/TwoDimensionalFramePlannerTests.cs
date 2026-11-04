using System.Numerics;
using Bork2d.Bake;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render.Shaders;
using Bork2d.Render.Sprites;
using Bork2d.Render.Textures;
using Bork2d.Render.TwoDimensional;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.TwoDimensional;

/// <summary>
/// Deterministic tests for the unified 2D frame planner: per-camera draw order, sprite/line
/// interleaving, run grouping, buffer ranges, conservative bounds culling, skip classification,
/// clears, throttled diagnostics, global uniforms, and the allocation-free warm path (M8 §6.4–§6.8).
/// </summary>
public sealed class TwoDimensionalFramePlannerTests
{
    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private static readonly Vector2[] _openLinePoints = [new(0f, 0f), new(10f, 0f)];

    [Fact]
    public void ZIndexIsThePrimaryDrawOrder()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 10f, y: -5f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 20f, y: 5f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(new[] { 10f, 20f }, InstanceXs(fixture.Instances, plan.InstanceCount));
    }

    [Fact]
    public void EqualZIndexOrdersByScreenYAscendingSoTheLowerRecordDrawsLater()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 10f, y: 5f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 20f, y: -5f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        // World +Y is up and the framebuffer origin is top-left, so y=+5 has the smaller screen Y.
        Assert.Equal(new[] { 10f, 20f }, InstanceXs(fixture.Instances, plan.InstanceCount));
    }

    [Fact]
    public void EqualZIndexAndScreenYOrdersByScreenXAscending()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 5f, y: 0f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: -5f, y: 0f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(new[] { -5f, 5f }, InstanceXs(fixture.Instances, plan.InstanceCount));
    }

    [Fact]
    public void LayerIsOnlyACullingBitAndNeverChangesTheOrder()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b1111u);

        // Captured in the "wrong" layer order: if Layer participated, the layer-1 record would draw
        // first. Screen Y must win, so the upper layer-4 record draws first.
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 20f, y: -5f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 4u, zIndex: 0, x: 10f, y: 5f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(new[] { 10f, 20f }, InstanceXs(fixture.Instances, plan.InstanceCount));
        Assert.Equal(1, plan.DrawCalls);
    }

    [Fact]
    public void LayerCullingStillFiltersWhichCamerasSeeARecord()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 1u);
        AddCamera(snapshot, zIndex: 1, mask: 2u, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 10f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, x: 20f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(new[] { 10f, 20f }, InstanceXs(fixture.Instances, plan.InstanceCount));
        Assert.Equal(0, plan.CulledSprites);
    }

    [Fact]
    public void ARotatedCameraReversesTheScreenYOrderOfTheSameRecords()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(
            snapshot,
            zIndex: 0,
            mask: 1u,
            clear: ClearKind.None,
            viewport: new Vector4(0f, 0f, 0.5f, 1f));
        AddCamera(
            snapshot,
            zIndex: 1,
            mask: 1u,
            clear: ClearKind.None,
            viewport: new Vector4(0.5f, 0f, 0.5f, 1f),
            world: Matrix3x2.CreateRotation(MathF.PI));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 10f, y: 5f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 20f, y: -5f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(0, plan.CulledSprites);

        // The unrotated camera draws the world +Y record first; the 180-degree camera sees the same
        // records upside down, so their screen-Y order is exactly reversed.
        CameraDrawPlan unrotated = plan.Cameras[0];
        CameraDrawPlan rotated = plan.Cameras[1];
        Assert.Equal(
            new[] { 10f, 20f },
            InstanceXs(fixture.Instances, unrotated.FirstInstance, unrotated.InstanceCount));
        Assert.Equal(
            new[] { 20f, 10f },
            InstanceXs(fixture.Instances, rotated.FirstInstance, rotated.InstanceCount));
    }

    [Fact]
    public void SpritesAndLinesInterleaveInDrawOrder()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b11u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 0f);
        AddLine(snapshot, _openLinePoints, layer: 2u, zIndex: 1, world: Matrix3x2.CreateTranslation(1f, 0f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2, x: 2f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(3, plan.RunCount);
        Assert.Equal(DrawKind.Sprite, plan.Runs[0].Kind);
        Assert.Equal(DrawKind.Line, plan.Runs[1].Kind);
        Assert.Equal(DrawKind.Sprite, plan.Runs[2].Kind);
        Assert.Equal(new[] { 0f, 2f }, InstanceXs(fixture.Instances, plan.InstanceCount));
        Assert.Equal(1, plan.LineCount);
        Assert.Equal(1f, fixture.Lines.GetVertex(0).Position.X, 5);
    }

    [Fact]
    public void ContiguousSameKindAndShaderGroupsFormOneRunPerGroup()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData first = fixture.Shaders.Register(
            CustomShader("materials/first"),
            ShaderBindingState.Ready);
        ShaderRenderData second = fixture.Shaders.Register(
            CustomShader("materials/second"),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 10f, shader: first);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 20f, shader: first);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2, x: 30f, shader: second);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 3, x: 40f, shader: first);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(3, plan.RunCount);
        Assert.Equal(3, plan.DrawCalls);
        Assert.Equal(0, plan.Runs[0].First);
        Assert.Equal(2, plan.Runs[0].Count);
        Assert.Equal(fixture.Shaders.EntryIdFor(first), plan.Runs[0].ShaderEntryId);
        Assert.Equal(2, plan.Runs[1].First);
        Assert.Equal(1, plan.Runs[1].Count);
        Assert.Equal(fixture.Shaders.EntryIdFor(second), plan.Runs[1].ShaderEntryId);
        Assert.Equal(3, plan.Runs[2].First);
        Assert.Equal(1, plan.Runs[2].Count);
        Assert.Equal(fixture.Shaders.EntryIdFor(first), plan.Runs[2].ShaderEntryId);
    }

    [Fact]
    public void DifferentKindsNeverMergeEvenWhenTheyResolveTheSameShaderEntry()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData shared = fixture.Shaders.Register(
            CustomShader("materials/shared"),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b11u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 0f, shader: shared);
        AddLine(
            snapshot,
            _openLinePoints,
            layer: 2u,
            zIndex: 1,
            world: Matrix3x2.CreateTranslation(1f, 0f),
            shader: shared);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.RunCount);
        Assert.Equal(DrawKind.Sprite, plan.Runs[0].Kind);
        Assert.Equal(DrawKind.Line, plan.Runs[1].Kind);
        Assert.Equal(plan.Runs[0].ShaderEntryId, plan.Runs[1].ShaderEntryId);
        Assert.Equal(fixture.Shaders.EntryIdFor(shared), plan.Runs[0].ShaderEntryId);
        Assert.Equal(0, plan.Runs[0].First);
        Assert.Equal(1, plan.Runs[0].Count);
        Assert.Equal(0, plan.Runs[1].First);
        Assert.Equal(6, plan.Runs[1].Count);
    }

    [Fact]
    public void DefaultOnlySceneRemainsExactlyOneRunPerCamera()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.RunCount);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(1, plan.Cameras[0].RunCount);
        Assert.Equal(1, plan.Cameras[1].RunCount);
        Assert.Equal(3, plan.Runs[0].Count);
        Assert.Equal(3, plan.Runs[1].Count);
        Assert.Equal(fixture.Shaders.DefaultEntryId, plan.Runs[0].ShaderEntryId);
        Assert.Equal(fixture.Shaders.DefaultEntryId, plan.Runs[1].ShaderEntryId);
    }

    [Fact]
    public void FrameCountsCoverInstancesLinesSkipsCullsAndNonEmptyCameras()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData pendingLine = fixture.Shaders.Register(
            CustomShader("materials/pending-line"),
            ShaderBindingState.Pending);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(
            snapshot,
            zIndex: 0,
            mask: 0b11u,
            clear: ClearKind.None,
            viewport: new Vector4(0f, 0f, 0.5f, 1f));
        AddCamera(
            snapshot,
            zIndex: 1,
            mask: 0b11u,
            clear: ClearKind.None,
            viewport: new Vector4(0.5f, 0f, 0.5f, 1f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 0f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, x: 100f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 0u);
        AddLine(snapshot, _openLinePoints, layer: 1u);
        AddLine(snapshot, _openLinePoints, layer: 2u, shader: pendingLine);
        AddLine(
            snapshot,
            _openLinePoints,
            layer: 1u,
            world: Matrix3x2.CreateTranslation(100f, 0f),
            bounds: Box(100f, 0f, 5f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.CameraCount);
        Assert.Equal(2, plan.CameraPlanCount);
        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(4, plan.DrawCalls);
        Assert.Equal(2, plan.InstanceCount);
        Assert.Equal(2, plan.LineCount);
        Assert.Equal(8, plan.LineVertexCount);
        Assert.Equal(12, plan.LineIndexCount);
        Assert.Equal(1, plan.SkippedSprites);
        Assert.Equal(1, plan.SkippedLines);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(1, plan.CulledLines);
    }

    [Fact]
    public void EmptyFrameHasNoDrawsBuffersOrRuns()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.CameraCount);
        Assert.Equal(0, plan.CameraPlanCount);
        Assert.Equal(0, plan.DrawCalls);
        Assert.Equal(0, plan.NonEmptyCameras);
        Assert.Equal(0, plan.InstanceCount);
        Assert.Equal(0, plan.LineCount);
        Assert.Equal(0, plan.RunCount);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(0, plan.SkippedLines);
        Assert.Equal(0, plan.CulledSprites);
        Assert.Equal(0, plan.CulledLines);
        Assert.Equal(0, fixture.Instances.WriteCount);
        Assert.Equal(0, fixture.Lines.VertexWriteCount);
        Assert.Equal(0, fixture.Lines.IndexWriteCount);
        Assert.Equal(0, fixture.Instances.GrowthCount);
        Assert.Equal(0, fixture.Globals.WriteCount);
    }

    [Fact]
    public void NullLineGeometryIsSkippedAndLoggedOncePerRenderer()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddLine(snapshot);

        TwoDimensionalFramePlan first = fixture.Plan(snapshot, 320, 240);
        TwoDimensionalFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, first.SkippedLines);
        Assert.Equal(1, second.SkippedLines);
        Assert.Equal(0, first.LineCount);
        Assert.Equal(0, fixture.Lines.VertexWriteCount);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("no drawable geometry", StringComparison.Ordinal)));
    }

    [Fact]
    public void InvalidLineLayerIsSkippedAndLoggedOncePerRenderer()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b1111u);
        AddLine(snapshot, _openLinePoints, layer: 0u);
        AddLine(snapshot, _openLinePoints, layer: 3u);

        TwoDimensionalFramePlan first = fixture.Plan(snapshot, 320, 240);
        TwoDimensionalFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, first.SkippedLines);
        Assert.Equal(2, second.SkippedLines);
        Assert.Equal(0, first.LineCount);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("Line layer", StringComparison.Ordinal)));
    }

    [Fact]
    public void PendingLineShaderIsSkippedWithoutAWarning()
    {
        using var fixture = new PlanningFixture();
        ShaderAsset asset = CustomShader("materials/line-pending");
        ShaderRenderData metadata = fixture.Shaders.Register(asset, ShaderBindingState.Pending);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddLine(snapshot, _openLinePoints, shader: metadata);

        TwoDimensionalFramePlan pending = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, pending.SkippedLines);
        Assert.Equal(0, pending.LineCount);
        Assert.Equal(0, pending.DrawCalls);
        Assert.DoesNotContain(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("materials/line-pending", StringComparison.Ordinal));

        int entryId = fixture.Shaders.EntryIdFor(metadata);
        _ = fixture.Shaders.Register(asset, ShaderBindingState.Ready, entryId);
        TwoDimensionalFramePlan ready = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, ready.SkippedLines);
        Assert.Equal(1, ready.LineCount);
        Assert.Equal(1, ready.DrawCalls);
    }

    [Fact]
    public void FailedLineShaderFallsBackToTheLineDefaultAndIsNotSkipped()
    {
        using var fixture = new PlanningFixture();
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader("materials/line-failed"),
            ShaderBindingState.Failed);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddLine(snapshot, _openLinePoints, shader: metadata);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.SkippedLines);
        Assert.Equal(1, plan.LineCount);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(fixture.Shaders.LineDefaultEntryId, plan.Runs[0].ShaderEntryId);
    }

    [Fact]
    public void ReadyLineShaderUsesItsResolvedEntry()
    {
        using var fixture = new PlanningFixture();
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader("materials/line-ready"),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddLine(snapshot, _openLinePoints, shader: metadata);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.SkippedLines);
        Assert.Equal(fixture.Shaders.EntryIdFor(metadata), plan.Runs[0].ShaderEntryId);
    }

    [Fact]
    public void LineWithoutAShaderUsesTheLineDefaultAndSeedsItsDeclaredUniformDefaults()
    {
        using var fixture = new PlanningFixture();
        fixture.Shaders.SetLineDefaultMetadata(
            CustomShader(
                ShaderStore.LineSpirVPath,
                instance:
                [
                    new ShaderUniform(
                        "uDefault",
                        ShaderUniformType.Vector4,
                        0,
                        new Vector4(2f, 3f, 4f, 5f)),
                ]));
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddLine(
            snapshot,
            _openLinePoints,
            uniform0: new Vector4(9f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Null(snapshot.Lines[0].ShaderData);
        Assert.Equal(fixture.Shaders.LineDefaultEntryId, plan.Runs[0].ShaderEntryId);
        LineVertex vertex = fixture.Lines.GetVertex(0);
        Assert.Equal(new Vector4(2f, 3f, 4f, 5f), vertex.Params0);
        Assert.Equal(Vector4.Zero, vertex.Params1);
    }

    [Fact]
    public void CustomLineUniformLanesAreWrittenToEveryVertex()
    {
        using var fixture = new PlanningFixture();
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/line-uniforms",
                instance:
                [
                    new ShaderUniform("u0", ShaderUniformType.Vector4, 0, Vector4.Zero),
                    new ShaderUniform("u1", ShaderUniformType.Float, 4, Vector4.Zero),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddLine(
            snapshot,
            _openLinePoints,
            shader: metadata,
            uniform0: new Vector4(1f, 2f, 3f, 4f),
            uniform1: new Vector4(5f, 0f, 0f, 0f),
            uniform2: new Vector4(6f, 0f, 0f, 0f),
            uniform3: new Vector4(7f, 0f, 0f, 0f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(4, plan.LineVertexCount);
        for (var index = 0; index < plan.LineVertexCount; index++)
        {
            LineVertex vertex = fixture.Lines.GetVertex(index);
            Assert.Equal(new Vector4(1f, 2f, 3f, 4f), vertex.Params0);
            Assert.Equal(new Vector4(5f, 0f, 0f, 0f), vertex.Params1);
            Assert.Equal(new Vector4(6f, 0f, 0f, 0f), vertex.Params2);
            Assert.Equal(new Vector4(7f, 0f, 0f, 0f), vertex.Params3);
        }
    }

    [Fact]
    public void LineGeometryIsWorldTransformedAndIndicesAreAbsolute()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddLine(
            snapshot,
            _openLinePoints,
            antiAlias: true,
            world: Matrix3x2.CreateTranslation(5f, 7f),
            color: new Color(1f, 0.5f, 0.25f, 0.75f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(4, plan.LineVertexCount);
        Assert.Equal(6, plan.LineIndexCount);
        Assert.Equal(4, fixture.Lines.LastRequiredVertexCount);
        Assert.Equal(6, fixture.Lines.LastRequiredIndexCount);
        Assert.Equal(4, fixture.Lines.VertexWriteCount);
        Assert.Equal(6, fixture.Lines.IndexWriteCount);
        Assert.Equal(0, fixture.Lines.GrowthCount);

        LineVertex[] vertices =
        [
            fixture.Lines.GetVertex(0),
            fixture.Lines.GetVertex(1),
            fixture.Lines.GetVertex(2),
            fixture.Lines.GetVertex(3),
        ];
        Assert.All(vertices, vertex => Assert.Equal(1f, vertex.Misc.X));
        Assert.All(
            vertices,
            vertex => Assert.Equal(new Vector4(1f, 0.5f, 0.25f, 0.75f), vertex.Color));
        Assert.Equal([5f, 5f, 15f, 15f], vertices.Select(vertex => vertex.Position.X));
        Assert.Equal([8f, 6f, 8f, 6f], vertices.Select(vertex => vertex.Position.Y));
        Assert.Equal([0f, 1f, 0f, 1f], vertices.Select(vertex => vertex.LineUv.X));
        Assert.Equal([0u, 1u, 2u, 1u, 3u, 2u], Enumerable.Range(0, 6).Select(fixture.Lines.GetIndex));
    }

    [Fact]
    public void AntiAliasFlagIsWrittenToTheMiscLane()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b11u);
        AddLine(snapshot, _openLinePoints, layer: 1u, antiAlias: true);
        AddLine(
            snapshot,
            _openLinePoints,
            layer: 2u,
            zIndex: 1,
            antiAlias: false,
            world: Matrix3x2.CreateTranslation(0f, 8f));

        _ = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1f, fixture.Lines.GetVertex(0).Misc.X);
        Assert.Equal(0f, fixture.Lines.GetVertex(4).Misc.X);
    }

    [Fact]
    public void TwoLinesWriteContiguousAbsoluteIndices()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b11u);
        AddLine(snapshot, _openLinePoints, layer: 1u, zIndex: 0);
        AddLine(
            snapshot,
            _openLinePoints,
            layer: 2u,
            zIndex: 1,
            world: Matrix3x2.CreateTranslation(0f, 4f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.LineCount);
        Assert.Equal(8, plan.LineVertexCount);
        Assert.Equal(12, plan.LineIndexCount);
        Assert.Equal([0u, 1u, 2u, 1u, 3u, 2u], Enumerable.Range(0, 6).Select(fixture.Lines.GetIndex));
        Assert.Equal(
            [4u, 5u, 6u, 5u, 7u, 6u],
            Enumerable.Range(6, 6).Select(fixture.Lines.GetIndex));
    }

    [Fact]
    public void CulledLinesUseWorldBoundsAndEmptyBoundsNeverCull()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b11u);
        AddLine(
            snapshot,
            _openLinePoints,
            layer: 1u,
            world: Matrix3x2.CreateTranslation(100f, 0f),
            bounds: Box(100f, 0f, 5f));
        AddLine(
            snapshot,
            _openLinePoints,
            layer: 2u,
            zIndex: 1,
            world: Matrix3x2.CreateTranslation(1000f, 0f),
            bounds: Bounds.Empty);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.SkippedLines);
        Assert.Equal(1, plan.CulledLines);
        Assert.Equal(1, plan.LineCount);
        Assert.Equal(
            [1000f, 1000f, 1010f, 1010f],
            Enumerable.Range(0, 4)
                .Select(index => fixture.Lines.GetVertex(index).Position.X));
    }

    [Fact]
    public void MultiCameraRangesCoverInstancesVerticesAndIndicesInDrawOrder()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(
            snapshot,
            zIndex: 0,
            mask: 0b11u,
            clear: ClearKind.None,
            viewport: new Vector4(0f, 0f, 0.5f, 1f));
        AddCamera(
            snapshot,
            zIndex: 1,
            mask: 0b11u,
            clear: ClearKind.None,
            viewport: new Vector4(0.5f, 0f, 0.5f, 1f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 1f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 2f);
        AddLine(snapshot, _openLinePoints, layer: 2u, zIndex: 2);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.NonEmptyCameras);
        Assert.Equal(4, plan.DrawCalls);
        Assert.Equal(4, plan.InstanceCount);
        Assert.Equal(2, plan.LineCount);
        Assert.Equal(8, plan.LineVertexCount);
        Assert.Equal(12, plan.LineIndexCount);

        CameraDrawPlan first = plan.Cameras[0];
        Assert.Equal(0, first.FirstInstance);
        Assert.Equal(2, first.InstanceCount);
        Assert.Equal(0, first.FirstVertex);
        Assert.Equal(4, first.VertexCount);
        Assert.Equal(0, first.FirstIndex);
        Assert.Equal(6, first.IndexCount);
        Assert.Equal(0, first.FirstRun);
        Assert.Equal(2, first.RunCount);

        CameraDrawPlan second = plan.Cameras[1];
        Assert.Equal(2, second.FirstInstance);
        Assert.Equal(2, second.InstanceCount);
        Assert.Equal(4, second.FirstVertex);
        Assert.Equal(4, second.VertexCount);
        Assert.Equal(6, second.FirstIndex);
        Assert.Equal(6, second.IndexCount);
        Assert.Equal(2, second.FirstRun);
        Assert.Equal(2, second.RunCount);

        Assert.Equal(new[] { 1f, 2f, 1f, 2f }, InstanceXs(fixture.Instances, plan.InstanceCount));
        Assert.Equal(8, fixture.Lines.LastRequiredVertexCount);
        Assert.Equal(12, fixture.Lines.LastRequiredIndexCount);
    }

    [Fact]
    public void InvalidCamerasAreSkippedIncludingTheirClearAndLogOncePerReason()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, size: 0f);
        AddCamera(snapshot, zIndex: 1, viewport: new Vector4(0f, 0f, 0f, 1f));
        AddCamera(snapshot, zIndex: 2, world: new Matrix3x2(0f, 0f, 0f, 0f, 0f, 0f));
        AddCamera(snapshot, zIndex: 3, world: Matrix3x2.CreateScale(2f));
        AddCamera(snapshot, zIndex: 4);

        TwoDimensionalFramePlan first = fixture.Plan(snapshot, 320, 240);
        TwoDimensionalFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(5, first.CameraCount);
        Assert.Equal(1, first.CameraPlanCount);
        Assert.Equal(1, second.CameraPlanCount);
        Assert.Equal(
            4,
            fixture.Logs.Entries.Count(
                entry => entry.Level == LogLevel.Warning
                    && entry.Message.Contains("Camera", StringComparison.Ordinal)));
    }

    [Fact]
    public void EmptyColorCameraRecordsAClearOnlyPassAndEmptyDepthCameraDoesNot()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.Color);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.Depth);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.CameraPlanCount);
        Assert.Equal(0, plan.DrawCalls);
        Assert.Equal(0, plan.InstanceCount);
        Assert.True(RecordsPass(plan.Cameras[0]));
        Assert.False(RecordsPass(plan.Cameras[1]));
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("ClearKind.Depth", StringComparison.Ordinal));
    }

    [Fact]
    public void PendingSpritesAreSkippedUntilTheirPageIsReady()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/pending"), layer: 1u);

        TwoDimensionalFramePlan pending = fixture.Plan(snapshot, 320, 240);
        Assert.Equal(1, pending.SkippedSprites);
        Assert.Equal(0, pending.InstanceCount);

        fixture.MakeReadyPage("tex/pending", 8, 8);
        TwoDimensionalFramePlan ready = fixture.Plan(snapshot, 320, 240);
        Assert.Equal(0, ready.SkippedSprites);
        Assert.Equal(1, ready.InstanceCount);
        Assert.Equal(1, fixture.Instances.WriteCount);
    }

    [Fact]
    public void MalformedSpriteFieldsAreFatalAndNameTheField()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);

        SpriteAsset nullUvs = Sprite("tex/a");
        nullUvs.Uvs = null;
        AssertFatal(fixture, nullUvs, "Uvs");
        AssertFatal(fixture, Sprite("tex/a", uvs: [new(0f, 0f)]), "Uvs");
        AssertFatal(
            fixture,
            Sprite(
                "tex/a",
                uvs: [new(float.NaN, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)]),
            "Uvs");
        AssertFatal(fixture, Sprite("tex/a", scale: new Vector2(0f)), "Scale");
        AssertFatal(fixture, Sprite("tex/a", scale: new Vector2(float.NaN)), "Scale");
        AssertFatal(fixture, Sprite("tex/a", pivot: new Vector2(1.1f, 0.5f)), "Pivot");
        AssertFatal(fixture, Sprite("tex/a", topology: (SpriteTopology)7), "Topology");
        AssertFatal(fixture, Sprite("tex/a", indices: [0, 1, 2]), "Indices");
    }

    [Fact]
    public void MalformedFieldsAreFatalEvenWhileThePageIsPending()
    {
        using var fixture = new PlanningFixture();

        // The page never loads: the malformed-field rule is unconditional and must run before the
        // pending skip.
        AssertFatal(fixture, Sprite("tex/pending-bad", scale: new Vector2(0f)), "Scale");
        AssertFatal(
            fixture,
            Sprite("tex/pending-bad", pivot: new Vector2(-0.1f, 0.5f)),
            "Pivot");
        SpriteAsset nullUvs = Sprite("tex/pending-bad");
        nullUvs.Uvs = null;
        AssertFatal(fixture, nullUvs, "Uvs");
        AssertFatal(fixture, Sprite("tex/pending-bad", indices: [0, 1, 2]), "Indices");

        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/pending-bad"), layer: 1u);

        TwoDimensionalFramePlan validPending = fixture.Plan(snapshot, 320, 240);
        Assert.Equal(1, validPending.SkippedSprites);
        Assert.Equal(0, validPending.InstanceCount);
    }

    [Fact]
    public void NonQuadTopologyIsSkippedWithOneDiagnosticPerAsset()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a", topology: SpriteTopology.TriangleStrip), layer: 1u);
        AddSprite(snapshot, Sprite("tex/a", topology: SpriteTopology.Triangles), layer: 1u);

        TwoDimensionalFramePlan first = fixture.Plan(snapshot, 320, 240);
        TwoDimensionalFramePlan second = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, first.SkippedSprites);
        Assert.Equal(2, second.SkippedSprites);
        Assert.Equal(0, first.InstanceCount);
        Assert.Equal(0, fixture.Assets.LoadAssetCount);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("topology", StringComparison.Ordinal)));
    }

    [Fact]
    public void FailedAndAbsentTexturesDrawThePlaceholderWithTheAssetWorldScale()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeFailedPage("tex/failed", 32, 16);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/failed", scale: new Vector2(10f, 4f)), layer: 1u, zIndex: 0);
        AddSprite(snapshot, Sprite(texture: null, scale: new Vector2(10f, 4f)), layer: 1u, zIndex: 1);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.InstanceCount);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(10f, fixture.Instances.Get(0).Transform1.Z, 3);
        Assert.Equal(4f, fixture.Instances.Get(0).Transform1.W, 3);
        Assert.Equal(0f, fixture.Instances.Get(0).Misc.Z);
        Assert.Equal(0f, fixture.Instances.Get(1).Misc.Z);
    }

    [Fact]
    public void FlipsEncodeSignedSizeKeepUvsAndKeepFlagsZero()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        Vector2[] uvs =
        [
            new(0.25f, 0.1f),
            new(0.75f, 0.1f),
            new(0.75f, 0.9f),
            new(0.25f, 0.9f),
        ];
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a", scale: new Vector2(8f), uvs: uvs, pivot: new Vector2(0.25f, 0.75f)),
            layer: 1u,
            flipX: true,
            flipY: true);

        _ = fixture.Plan(snapshot, 320, 240);

        SpriteInstance instance = fixture.Instances.Get(0);
        Assert.Equal(-4f, instance.Transform1.Z, 5);
        Assert.Equal(-6.4f, instance.Transform1.W, 5);
        Assert.Equal(0.25f, instance.Misc.X);
        Assert.Equal(0.75f, instance.Misc.Y);
        Assert.Equal(0f, instance.Misc.W);
        Assert.Equal(0.25f, instance.Uv0.X);
        Assert.Equal(0.75f, instance.Uv0.Z);
        Assert.Equal(0.75f, instance.Uv1.X);
        Assert.Equal(0.25f, instance.Uv1.Z);
    }

    [Fact]
    public void PlanWithNoRunsWritesNoGlobalBlock()
    {
        using var fixture = new PlanningFixture();
        var snapshot = new FrameSnapshot
        {
            Sequence = 4,
            Time = 12.5,
            DeltaTime = 0.016,
        };
        AddCamera(snapshot, zIndex: 0);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.DrawCalls);
        Assert.Equal(0, plan.RunCount);
        Assert.Equal(1, fixture.Globals.EnsureCapacityCallCount);
        Assert.Equal(0, fixture.Globals.LastRequiredBlocks);
        Assert.Equal(0, fixture.Globals.WriteCount);
    }

    [Fact]
    public void PlanWritesOneGlobalBlockPerRunWithTheFrameTimeAndShaderDefaults()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/globals",
                globals:
                [
                    new ShaderUniform(
                        "gColor",
                        ShaderUniformType.Color,
                        0,
                        new Vector4(0.1f, 0.2f, 0.3f, 0.4f)),
                    new ShaderUniform(
                        "gScale",
                        ShaderUniformType.Float,
                        4,
                        new Vector4(2f, 0f, 0f, 0f)),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot
        {
            Sequence = 4,
            Time = 12.5,
            DeltaTime = 0.016,
        };
        snapshot.AddGlobalUniform(
            new GlobalUniformSnapshot(
                "gColor",
                ShaderUniformType.Color,
                new Vector4(0.9f, 0.8f, 0.7f, 0.6f)));
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, shader: metadata);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.RunCount);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(1, fixture.Globals.EnsureCapacityCallCount);
        Assert.Equal(1, fixture.Globals.LastRequiredBlocks);
        Assert.Equal(1, fixture.Globals.WriteCount);
        Assert.Equal(0, fixture.Globals.LastWrittenIndex);

        GlobalUniformBlock block = fixture.Globals.Get(0);
        Assert.Equal(new Vector4(12.5f, 0f, 0f, 0f), block.Time);
        Assert.Equal(new Vector4(0.9f, 0.8f, 0.7f, 0.6f), block.Global0);
        Assert.Equal(new Vector4(2f, 0f, 0f, 0f), block.Global1);
        Assert.Equal(Vector4.Zero, block.Global2);
        Assert.Equal(0u, plan.Runs[0].GlobalBlockOffset);
        Assert.Equal(144, fixture.Globals.BlockStride);
        Assert.Equal(144, GlobalUniformBlock.SizeInBytes);
    }

    [Fact]
    public void EachSpriteAndLineRunOwnsItsGlobalBlockAndDynamicOffset()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData spriteShader = fixture.Shaders.Register(
            CustomShader(
                "materials/first",
                globals:
                [
                    new ShaderUniform(
                        "gA",
                        ShaderUniformType.Vector2,
                        0,
                        new Vector4(3f, 4f, 0f, 0f)),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1, Time = 2f };
        snapshot.AddGlobalUniform(
            new GlobalUniformSnapshot("gA", ShaderUniformType.Vector2, new Vector4(5f, 6f, 0f, 0f)));
        AddCamera(snapshot, zIndex: 0, mask: 0b11u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, shader: spriteShader);
        AddLine(snapshot, _openLinePoints, layer: 2u, zIndex: 1);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.RunCount);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(2, fixture.Globals.WriteCount);
        Assert.Equal(0u, plan.Runs[0].GlobalBlockOffset);
        Assert.Equal((uint)fixture.Globals.BlockStride, plan.Runs[1].GlobalBlockOffset);
        Assert.Equal(new Vector4(5f, 6f, 0f, 0f), fixture.Globals.Get(0).Global0);
        Assert.Equal(Vector4.Zero, fixture.Globals.Get(1).Global0);
        Assert.Equal(new Vector4(2f, 0f, 0f, 0f), fixture.Globals.Get(0).Time);
        Assert.Equal(new Vector4(2f, 0f, 0f, 0f), fixture.Globals.Get(1).Time);
    }

    [Fact]
    public void PendingCustomSpriteShaderSkipsThenDrawsTheFrameAfterItBecomesReady()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderAsset asset = CustomShader("materials/pending-sprite");
        ShaderRenderData metadata = fixture.Shaders.Register(asset, ShaderBindingState.Pending);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, shader: metadata);

        TwoDimensionalFramePlan pending = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, pending.SkippedSprites);
        Assert.Equal(0, pending.InstanceCount);
        Assert.Equal(0, pending.DrawCalls);
        Assert.Equal(0, fixture.Instances.WriteCount);

        int entryId = fixture.Shaders.EntryIdFor(metadata);
        _ = fixture.Shaders.Register(asset, ShaderBindingState.Ready, entryId);
        TwoDimensionalFramePlan ready = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, ready.SkippedSprites);
        Assert.Equal(1, ready.InstanceCount);
        Assert.Equal(1, ready.DrawCalls);
        Assert.Equal(entryId, ready.Runs[0].ShaderEntryId);
    }

    [Fact]
    public void FailedCustomSpriteShaderDrawsWithTheSpriteDefaultEntryAndIsNotSkipped()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader("materials/failed-sprite"),
            ShaderBindingState.Failed);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, shader: metadata);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(fixture.Shaders.DefaultEntryId, plan.Runs[0].ShaderEntryId);
        Assert.Equal(Vector4.Zero, fixture.Instances.Get(0).InstanceParams0);
    }

    [Fact]
    public void ExtraTexturesResolveDeclaredSlotsAndUseThePlaceholderForFailedOrAbsentRefs()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        fixture.MakeReadyPage("tex/extra", 8, 8);
        fixture.MakeFailedPage("tex/bad", 8, 8);
        int extraIndex = fixture.Store.EnsurePage("tex/extra").Index;
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader(
                "materials/textures",
                slots:
                [
                    new ShaderTextureSlot("t0"),
                    new ShaderTextureSlot("t1"),
                    new ShaderTextureSlot("t2"),
                    new ShaderTextureSlot("t3"),
                ]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            shader: metadata,
            extraTexture0: "tex/extra",
            extraTexture1: "tex/bad",
            extraTexture3: "tex/extra",
            additionalTextureCount: 4);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        SpriteInstance instance = fixture.Instances.Get(0);
        Assert.Equal(extraIndex, instance.ExtraTextureIndices.X);
        Assert.Equal(0f, instance.ExtraTextureIndices.Y);
        Assert.Equal(0f, instance.ExtraTextureIndices.Z);
        Assert.Equal(extraIndex, instance.ExtraTextureIndices.W);
    }

    [Fact]
    public void PendingExtraTextureSkipsTheWholeSpriteUntilThePageIsReady()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        ShaderRenderData metadata = fixture.Shaders.Register(
            CustomShader("materials/textures", slots: [new ShaderTextureSlot("t0")]),
            ShaderBindingState.Ready);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(
            snapshot,
            Sprite("tex/a"),
            layer: 1u,
            shader: metadata,
            extraTexture0: "tex/pending-extra",
            additionalTextureCount: 1);

        TwoDimensionalFramePlan pending = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, pending.SkippedSprites);
        Assert.Equal(0, pending.InstanceCount);

        fixture.MakeReadyPage("tex/pending-extra", 8, 8);
        TwoDimensionalFramePlan ready = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(0, ready.SkippedSprites);
        Assert.Equal(1, ready.InstanceCount);
        Assert.NotEqual(0f, fixture.Instances.Get(0).ExtraTextureIndices.X);
    }

    [Fact]
    public void BoundsCullingSkipsRecordsOutsideTheCameraAndKeepsUnknownBounds()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b11u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 0f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 100f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 2, x: 13f, bounds: Box(13f, 0f, 5f));

        // No bounds means "unknown": a far sprite with empty bounds stays conservative and draws.
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 3, x: 1000f);

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.CameraCount);
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(3, plan.InstanceCount);
        Assert.Equal(3, fixture.Instances.WriteCount);
        Assert.Equal(3, fixture.Instances.LastRequiredInstances);
    }

    [Fact]
    public void LayerFilteringRunsBeforeBoundsCulling()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 1u);
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, x: 100f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 100f, bounds: Box(100f, 0f, 5f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        // The layer-mismatched sprite was never a bounds candidate, so only the layer-matched
        // offscreen sprite counts as culled.
        Assert.Equal(0, plan.SkippedSprites);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(0, plan.InstanceCount);
        Assert.Equal(0, fixture.Instances.WriteCount);
    }

    [Fact]
    public void ARecordCulledByOneCameraIsStillDrawnByAnother()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(
            snapshot,
            zIndex: 1,
            clear: ClearKind.None,
            world: Matrix3x2.CreateTranslation(100f, 0f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 100f, bounds: Box(100f, 0f, 5f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
        Assert.Equal(0, plan.Cameras[0].InstanceCount);
        Assert.Equal(1, plan.Cameras[1].InstanceCount);
    }

    [Fact]
    public void CulledRecordsCountOnceAcrossOverlappingCameras()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 0f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, x: 100f, bounds: Box(100f, 0f, 5f));
        AddLine(snapshot, _openLinePoints, layer: 1u);
        AddLine(
            snapshot,
            _openLinePoints,
            layer: 1u,
            zIndex: 1,
            world: Matrix3x2.CreateTranslation(100f, 0f),
            bounds: Box(100f, 0f, 5f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(2, plan.InstanceCount);
        Assert.Equal(2, plan.LineCount);
        Assert.Equal(1, plan.CulledSprites);
        Assert.Equal(1, plan.CulledLines);
        Assert.Equal(2, fixture.Instances.WriteCount);
    }

    [Fact]
    public void FrameInstanceTotalBeyondIntMaxValueIsAFatalBork2dException()
    {
        long tooMany = (long)int.MaxValue + 1;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => TwoDimensionalFramePlanner.ValidateInstanceCount(tooMany));

        Assert.Contains(tooMany.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(int.MaxValue.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("No instance is truncated", exception.Message, StringComparison.Ordinal);
        Assert.Equal(int.MaxValue, TwoDimensionalFramePlanner.ValidateInstanceCount(int.MaxValue));
        Assert.Equal(0, TwoDimensionalFramePlanner.ValidateInstanceCount(0));
    }

    [Fact]
    public void SharedVisibilityRuleAgreesWithThePlannerOnLayerAndBoundsCases()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, mask: 0b0011u, clear: ClearKind.None);
        AddCamera(snapshot, zIndex: 1, mask: 0b1001u, clear: ClearKind.None);
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 0, x: 0f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 1, x: 100f, bounds: Box(100f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 2u, zIndex: 2, x: 2f, bounds: Box(2f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 1u, zIndex: 3, x: 1000f);
        AddSprite(snapshot, Sprite("tex/a"), layer: 8u, zIndex: 4, x: 50f, bounds: Box(0f, 0f, 5f));
        AddSprite(snapshot, Sprite("tex/a"), layer: 4u, zIndex: 5, x: 7f, bounds: Box(7f, 0f, 5f));

        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        for (var cameraIndex = 0; cameraIndex < plan.CameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = plan.Cameras[cameraIndex];
            var actual = new List<float>();
            for (var index = 0; index < camera.InstanceCount; index++)
            {
                actual.Add(fixture.Instances.Get(camera.FirstInstance + index).Transform1.X);
            }

            var expected = new List<float>();
            for (var spriteIndex = 0; spriteIndex < snapshot.SpriteCount; spriteIndex++)
            {
                SpriteSnapshot sprite = snapshot.Sprites[spriteIndex];
                if (TwoDimensionalPlanning.IsVisible(
                    camera.CullingMask,
                    camera.WorldBounds,
                    sprite.Layer,
                    sprite.Bounds))
                {
                    expected.Add(sprite.WorldMatrix.M31);
                }
            }

            actual.Sort();
            expected.Sort();
            Assert.Equal(expected, actual);
        }

        var culled = 0;
        for (var spriteIndex = 0; spriteIndex < snapshot.SpriteCount; spriteIndex++)
        {
            SpriteSnapshot sprite = snapshot.Sprites[spriteIndex];
            var matchesAnyMask = false;
            var visibleToAnyCamera = false;
            for (var cameraIndex = 0; cameraIndex < plan.CameraPlanCount; cameraIndex++)
            {
                CameraDrawPlan camera = plan.Cameras[cameraIndex];
                if (!TwoDimensionalPlanning.LayerMatches(camera.CullingMask, sprite.Layer))
                {
                    continue;
                }

                matchesAnyMask = true;
                visibleToAnyCamera |= TwoDimensionalPlanning.IsVisible(
                    camera.CullingMask,
                    camera.WorldBounds,
                    sprite.Layer,
                    sprite.Bounds);
            }

            if (matchesAnyMask && !visibleToAnyCamera)
            {
                culled++;
            }
        }

        Assert.Equal(culled, plan.CulledSprites);
        Assert.Equal(0, plan.SkippedSprites);
    }

    [Fact]
    public void ProductionBoundsKeepRotatedCameraSpritesVisible()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(320f, 240f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;
        camera.LocalRotation = 30f;
        camera.CullingMask = GameLayer.World;
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = Sprite("tex/a", scale: new Vector2(4f));
        sprite.Layer = GameLayer.World;

        // The view-space top-right corner (13, 9.5) is inside the rotated view. Unrotated camera
        // bounds would end at y = 10 and wrongly cull it.
        float cosine = MathF.Cos(MathF.PI / 6f);
        float sine = MathF.Sin(MathF.PI / 6f);
        var viewCorner = new Vector2(13f, 9.5f);
        sprite.LocalPosition = new Vector2(
            (viewCorner.X * cosine) - (viewCorner.Y * sine),
            (viewCorner.X * sine) + (viewCorner.Y * cosine));

        FrameSnapshot snapshot = CaptureEngine(engine);
        TwoDimensionalFramePlan plan = fixture.Plan(snapshot, 320, 240);

        Assert.Equal(1, plan.InstanceCount);
        Assert.Equal(0, plan.CulledSprites);
    }

    [Fact]
    public void WarmPlanAllocatesNothingIncludingLines()
    {
        using var fixture = new PlanningFixture();
        fixture.MakeReadyPage("tex/a", 8, 8);
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0, viewport: new Vector4(0f, 0f, 0.5f, 1f), mask: 0b11u);
        AddCamera(snapshot, zIndex: 1, viewport: new Vector4(0.5f, 0f, 0.5f, 1f), mask: 0b11u);
        for (var index = 0; index < 100; index++)
        {
            AddSprite(
                snapshot,
                Sprite("tex/a"),
                layer: 1u,
                zIndex: index,
                x: (index % 10) * 0.5f,
                y: -((index / 10) * 0.1f),
                bounds: Box(0f, 0f, 5f));
        }

        for (var index = 0; index < 50; index++)
        {
            AddLine(
                snapshot,
                _openLinePoints,
                layer: 2u,
                zIndex: index,
                world: Matrix3x2.CreateTranslation(0f, index * 0.05f),
                bounds: Box(0f, 0f, 20f));
        }

        int expectedInstances = fixture.Plan(snapshot, 320, 240).InstanceCount;
        Assert.Equal(200, expectedInstances);
        for (var warmup = 0; warmup < 5; warmup++)
        {
            _ = fixture.Plan(snapshot, 320, 240);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            total += fixture.Plan(snapshot, 320, 240).InstanceCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(100 * expectedInstances, total);
    }

    private static Bounds Box(float x, float y, float halfExtent)
        => Bounds.FromMinMax(
            new Vector2(x - halfExtent, y - halfExtent),
            new Vector2(x + halfExtent, y + halfExtent));

    private static ShaderAsset CustomShader(
        string spirVPath,
        ShaderUniform[]? instance = null,
        ShaderUniform[]? globals = null,
        ShaderTextureSlot[]? slots = null,
        uint layoutVersion = ShaderLayout.CurrentVersion,
        string entryPoint = "main")
        => new()
        {
            SpirV = spirVPath,
            LayoutVersion = layoutVersion,
            EntryPoint = entryPoint,
            InstanceUniforms = instance ?? [],
            GlobalUniforms = globals ?? [],
            TextureSlots = slots ?? [],
        };

    private static float[] InstanceXs(FakeInstanceBuffer instances)
        => [.. Enumerable.Range(0, instances.WriteCount).Select(index => instances.Get(index).Transform1.X)];

    private static float[] InstanceXs(FakeInstanceBuffer instances, int count)
        => InstanceXs(instances, 0, count);

    private static float[] InstanceXs(FakeInstanceBuffer instances, int first, int count)
        => [.. Enumerable.Range(first, count).Select(index => instances.Get(index).Transform1.X)];

    private static (GameEngine<GameLayer> Engine, FakeWindow Window) CreateEngine(Vector2 size)
    {
        var window = new FakeWindow { Size = size };
        return (new GameEngine<GameLayer>(new FakeTime(), window, new FakeAssets()), window);
    }

    private static FrameSnapshot CaptureEngine(GameEngine<GameLayer> engine)
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, 1);
        return snapshot;
    }

    private static void AssertFatal(PlanningFixture fixture, SpriteAsset asset, string field)
    {
        var snapshot = new FrameSnapshot { Sequence = 1 };
        AddCamera(snapshot, zIndex: 0);
        AddSprite(snapshot, asset, layer: 1u);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => fixture.Plan(snapshot, 320, 240));
        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
    }

    private static bool RecordsPass(in CameraDrawPlan camera)
        => camera.RunCount > 0 || camera.Clear == ClearKind.Color;

    private static void AddCamera(
        FrameSnapshot snapshot,
        int zIndex,
        float size = 10f,
        Vector4? viewport = null,
        uint mask = 1u,
        ClearKind clear = ClearKind.Color,
        Matrix3x2? world = null)
        => snapshot.AddCamera(
            new CameraSnapshot(
                zIndex,
                world ?? Matrix3x2.Identity,
                size,
                viewport ?? new Vector4(0f, 0f, 1f, 1f),
                mask,
                clear,
                new Color(0f, 0f, 0f, 1f)));

    private static void AddSprite(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        uint layer,
        int zIndex = 0,
        bool flipX = false,
        bool flipY = false,
        float x = 0f,
        float y = 0f,
        Bounds? bounds = null,
        ShaderRenderData? shader = null,
        Vector4? uniform0 = null,
        AssetRef<TextureDescriptor> extraTexture0 = default,
        AssetRef<TextureDescriptor> extraTexture1 = default,
        AssetRef<TextureDescriptor> extraTexture2 = default,
        AssetRef<TextureDescriptor> extraTexture3 = default,
        int additionalTextureCount = 0)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, y),
                Color.White,
                layer,
                zIndex,
                flipX,
                flipY)
            {
                Bounds = bounds ?? Bounds.Empty,
                ShaderData = shader,
                Uniform0 = uniform0 ?? Vector4.Zero,
                ExtraTexture0 = extraTexture0,
                ExtraTexture1 = extraTexture1,
                ExtraTexture2 = extraTexture2,
                ExtraTexture3 = extraTexture3,
                AdditionalTextureCount = additionalTextureCount,
            });

    private static void AddLine(
        FrameSnapshot snapshot,
        Vector2[]? points = null,
        uint layer = 1u,
        int zIndex = 0,
        float width = 2f,
        bool closed = false,
        bool antiAlias = true,
        Matrix3x2? world = null,
        Bounds? bounds = null,
        ShaderRenderData? shader = null,
        Color? color = null,
        Vector4? uniform0 = null,
        Vector4? uniform1 = null,
        Vector4? uniform2 = null,
        Vector4? uniform3 = null)
    {
        Matrix3x2 transform = world ?? Matrix3x2.Identity;
        LineGeometryData? geometry = points is null
            ? null
            : LineGeometryBuilder.Build(points, closed, width);
        snapshot.AddLine(
            new LineSnapshot(
                geometry,
                transform,
                color ?? Color.White,
                layer,
                zIndex,
                antiAlias)
            {
                Bounds = bounds ?? LineBounds.CreateWorldBounds(geometry, transform),
                ShaderData = shader,
                Uniform0 = uniform0 ?? Vector4.Zero,
                Uniform1 = uniform1 ?? Vector4.Zero,
                Uniform2 = uniform2 ?? Vector4.Zero,
                Uniform3 = uniform3 ?? Vector4.Zero,
            });
    }

    private static SpriteAsset Sprite(
        string? texture,
        Vector2? scale = null,
        Vector2[]? uvs = null,
        Vector2? pivot = null,
        SpriteTopology topology = SpriteTopology.Quad,
        ushort[]? indices = null)
        => new()
        {
            Texture = texture,
            Uvs = uvs ?? _fullUvs,
            Scale = scale ?? new Vector2(10f),
            Pivot = pivot ?? new Vector2(0.5f, 0.5f),
            Topology = topology,
            Indices = indices,
        };

    private sealed class PlanningFixture : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;

        internal PlanningFixture()
        {
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(Logs);
            });
            ILogger logger = _loggerFactory.CreateLogger("Bork2d.Render");
            Store = new TexturePageStore(Assets, Gpu, TexturePageCapacity.Initial, 16384, logger);
            BakedTexture placeholder = Ktx2TextureFactory.BakeSolid(
                TextureFormat.Rgba8,
                TexturePlaceholder.Size,
                TexturePlaceholder.Size);
            Store.Start(
                Ktx2TextureFactory.Descriptor(
                    TexturePlaceholder.PayloadPath,
                    placeholder,
                    TexturePlaceholder.Size,
                    TexturePlaceholder.Size,
                    TextureFilter.Nearest),
                placeholder.Payload.ToArray());
            Planner = new TwoDimensionalFramePlanner(Store, Shaders, logger);
        }

        internal FakeTextureAssets Assets { get; } = new();

        internal FakeTexturePageGpu Gpu { get; } = new();

        internal CapturingLoggerProvider Logs { get; } = new();

        internal TexturePageStore Store { get; }

        internal FakeShaderFrameSource Shaders { get; } = new();

        internal TwoDimensionalFramePlanner Planner { get; }

        internal FakeInstanceBuffer Instances { get; } = new();

        internal FakeLineBuffer Lines { get; } = new();

        internal FakeGlobalUniformBuffer Globals { get; } = new();

        internal TwoDimensionalFramePlan Plan(FrameSnapshot snapshot, int width, int height)
            => Planner.Plan(
                snapshot,
                width,
                height,
                Instances,
                Lines,
                Globals,
                Format.R8G8B8A8Srgb);

        internal void MakeReadyPage(string path, uint width, uint height)
        {
            BakedTexture baked = Ktx2TextureFactory.BakeSolid(
                TextureFormat.Rgba8,
                (int)width,
                (int)height);
            TextureDescriptor descriptor = Ktx2TextureFactory.Descriptor(
                $"data/{path}",
                baked,
                width,
                height,
                TextureFilter.Nearest);
            Assets.CompleteDescriptor(path, descriptor);
            Assets.CompletePayload($"data/{path}", baked.Payload.ToArray());
            _ = Store.EnsurePage(path);
            Store.Pump();
        }

        internal void MakeFailedPage(string path, uint width, uint height)
        {
            var descriptor = new TextureDescriptor
            {
                Width = width,
                Height = height,
                Format = TextureFormat.Rgba8,
                Filter = TextureFilter.Nearest,
                Wrap = TextureWrap.Clamp,
                Payload = DataRef.None,
            };
            Assets.CompleteDescriptor(path, descriptor);
            _ = Store.EnsurePage(path);
            Store.Pump();
        }

        public void Dispose()
        {
            Store.Destroy();
            _loggerFactory.Dispose();
        }
    }
}
