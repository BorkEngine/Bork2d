using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Bork2d.Render.TwoDimensional;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic, GPU-free tests for the ordered render-pass registry: registration order governs
/// planning and recording, every pass contributes to the aggregate, a planning round resets the
/// aggregate and each pass, and adding a pass needs no registry or frame-loop change.
/// </summary>
public sealed class RenderPassRegistryTests
{
    [Fact]
    public void PassesAreAddedByRegistrationAloneAndPlanInOrder()
    {
        var events = new List<string>();
        var registry = new RenderPassRegistry(
            new RecordingPass("2D", events),
            new RecordingPass("Custom", events));

        registry.Plan(
            new FrameSnapshot(),
            320,
            240,
            new FakeInstanceBuffer(),
            new FakeLineBuffer(),
            new FakeGlobalUniformBuffer(),
            SceneTarget.ColorFormat);

        Assert.Equal(
            new[] { "2D", "Custom" },
            registry.Passes.Select(pass => pass.Name));
        Assert.Equal(new[] { "plan:2D", "plan:Custom" }, events);
    }

    [Fact]
    public void PlanForwardsTheSinksAndSceneFormatToEveryPass()
    {
        var globals = new FakeGlobalUniformBuffer();
        var lines = new FakeLineBuffer();
        var first = new RecordingPass("2D", []);
        var second = new RecordingPass("Custom", []);
        var registry = new RenderPassRegistry(first, second);

        registry.Plan(
            new FrameSnapshot(),
            320,
            240,
            new FakeInstanceBuffer(),
            lines,
            globals,
            SceneTarget.ColorFormat);

        Assert.Equal(SceneTarget.ColorFormat, first.SceneFormat);
        Assert.Equal(SceneTarget.ColorFormat, second.SceneFormat);
        Assert.Same(globals, first.Globals);
        Assert.Same(globals, second.Globals);
        Assert.Same(lines, first.Lines);
        Assert.Same(lines, second.Lines);
    }

    [Fact]
    public void PlanAggregatesEveryPassCount()
    {
        var registry = new RenderPassRegistry(
            new RecordingPass(
                "First",
                [],
                cameras: 2,
                cameraPlans: 2,
                drawCalls: 1,
                nonEmptyCameras: 1,
                instances: 5,
                lines: 2,
                skippedSprites: 7,
                skippedLines: 3,
                culledSprites: 3,
                culledLines: 1),
            new RecordingPass(
                "Second",
                [],
                cameras: 1,
                cameraPlans: 0,
                drawCalls: 2,
                nonEmptyCameras: 2,
                instances: 4,
                lines: 6,
                skippedSprites: 1,
                skippedLines: 2,
                culledSprites: 6,
                culledLines: 4));

        registry.Plan(
            new FrameSnapshot(),
            320,
            240,
            new FakeInstanceBuffer(),
            new FakeLineBuffer(),
            new FakeGlobalUniformBuffer(),
            SceneTarget.ColorFormat);

        RenderPassStats stats = registry.Stats;
        Assert.Equal(3, stats.CameraCount);
        Assert.Equal(2, stats.CameraPlanCount);
        Assert.Equal(3, stats.DrawCalls);
        Assert.Equal(3, stats.NonEmptyCameras);
        Assert.Equal(9, stats.InstanceCount);
        Assert.Equal(8, stats.LineCount);
        Assert.Equal(8, stats.SkippedSprites);
        Assert.Equal(5, stats.SkippedLines);
        Assert.Equal(9, stats.CulledSprites);
        Assert.Equal(5, stats.CulledLines);
    }

    [Fact]
    public void RecordRunsPassesInRegistrationOrder()
    {
        var events = new List<string>();
        var first = new RecordingPass("2D", events);
        var second = new RecordingPass("Custom", events);
        var registry = new RenderPassRegistry(first, second);

        registry.Record(default);

        Assert.Equal(new[] { "record:2D", "record:Custom" }, events);
        Assert.Equal(1, first.RecordCount);
        Assert.Equal(1, second.RecordCount);
    }

    [Fact]
    public void PlanResetsTheAggregateAndEachPassBeforePlanning()
    {
        var events = new List<string>();
        var pass = new RecordingPass(
            "2D",
            events,
            cameras: 1,
            cameraPlans: 1,
            drawCalls: 1,
            nonEmptyCameras: 1,
            instances: 2,
            lines: 3,
            skippedSprites: 3,
            skippedLines: 4,
            culledSprites: 4,
            culledLines: 5);
        var registry = new RenderPassRegistry(pass);
        var snapshot = new FrameSnapshot();
        var instances = new FakeInstanceBuffer();
        var lines = new FakeLineBuffer();
        var globals = new FakeGlobalUniformBuffer();

        registry.Plan(snapshot, 320, 240, instances, lines, globals, SceneTarget.ColorFormat);
        registry.Plan(snapshot, 320, 240, instances, lines, globals, SceneTarget.ColorFormat);

        Assert.Equal(2, pass.ResetCount);
        Assert.Equal(2, pass.PlanCount);
        RenderPassStats stats = registry.Stats;
        Assert.Equal(1, stats.CameraCount);
        Assert.Equal(1, stats.CameraPlanCount);
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(1, stats.NonEmptyCameras);
        Assert.Equal(2, stats.InstanceCount);
        Assert.Equal(3, stats.LineCount);
        Assert.Equal(3, stats.SkippedSprites);
        Assert.Equal(4, stats.SkippedLines);
        Assert.Equal(4, stats.CulledSprites);
        Assert.Equal(5, stats.CulledLines);
    }

    /// <summary>
    /// A pass that records its calls and adds fixed counts to the aggregate, so a test can assert
    /// order and totals without Vulkan.
    /// </summary>
    private sealed class RecordingPass : IRenderPass
    {
        private readonly List<string> _events;
        private readonly int _cameras;
        private readonly int _cameraPlans;
        private readonly int _drawCalls;
        private readonly int _nonEmptyCameras;
        private readonly int _instances;
        private readonly int _lines;
        private readonly long _skippedSprites;
        private readonly long _skippedLines;
        private readonly int _culledSprites;
        private readonly int _culledLines;

        internal RecordingPass(
            string name,
            List<string> events,
            int cameras = 0,
            int cameraPlans = 0,
            int drawCalls = 0,
            int nonEmptyCameras = 0,
            int instances = 0,
            int lines = 0,
            long skippedSprites = 0,
            long skippedLines = 0,
            int culledSprites = 0,
            int culledLines = 0)
        {
            Name = name;
            _events = events;
            _cameras = cameras;
            _cameraPlans = cameraPlans;
            _drawCalls = drawCalls;
            _nonEmptyCameras = nonEmptyCameras;
            _instances = instances;
            _lines = lines;
            _skippedSprites = skippedSprites;
            _skippedLines = skippedLines;
            _culledSprites = culledSprites;
            _culledLines = culledLines;
        }

        public string Name { get; }

        internal int ResetCount { get; private set; }

        internal int PlanCount { get; private set; }

        internal int RecordCount { get; private set; }

        internal Format SceneFormat { get; private set; }

        internal IGlobalUniformBuffer? Globals { get; private set; }

        internal ILineBufferSink? Lines { get; private set; }

        public void Plan(
            FrameSnapshot snapshot,
            int framebufferWidth,
            int framebufferHeight,
            Bork2d.Render.Sprites.IInstanceBuffer instances,
            ILineBufferSink lines,
            IGlobalUniformBuffer globals,
            Format sceneFormat,
            RenderPassStats stats)
        {
            PlanCount++;
            SceneFormat = sceneFormat;
            Globals = globals;
            Lines = lines;
            _events.Add($"plan:{Name}");
            stats.CameraCount += _cameras;
            stats.CameraPlanCount += _cameraPlans;
            stats.DrawCalls += _drawCalls;
            stats.NonEmptyCameras += _nonEmptyCameras;
            stats.InstanceCount += _instances;
            stats.LineCount += _lines;
            stats.SkippedSprites += _skippedSprites;
            stats.SkippedLines += _skippedLines;
            stats.CulledSprites += _culledSprites;
            stats.CulledLines += _culledLines;
        }

        public void Record(in RenderPassRecordContext context)
        {
            RecordCount++;
            _events.Add($"record:{Name}");
        }

        public void Reset() => ResetCount++;
    }
}
