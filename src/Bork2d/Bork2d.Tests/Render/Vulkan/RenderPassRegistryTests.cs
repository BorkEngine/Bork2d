using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic, GPU-free tests for the ordered render-pass registry: registration order governs
/// planning and recording, every pass contributes to the aggregate stats, a planning round resets
/// the aggregate and each pass, and adding a pass needs no registry or frame-loop change.
/// </summary>
public sealed class RenderPassRegistryTests
{
    [Fact]
    public void PassesAreAddedByRegistrationAloneAndPlanInOrder()
    {
        var events = new List<string>();
        var registry = new RenderPassRegistry(
            new RecordingPass("Sprite", events),
            new RecordingPass("Custom", events));

        registry.Plan(new FrameSnapshot(), 320, 240, new FakeInstanceBuffer());

        Assert.Equal(
            new[] { "Sprite", "Custom" },
            registry.Passes.Select(pass => pass.Name));
        Assert.Equal(new[] { "plan:Sprite", "plan:Custom" }, events);
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
                skippedSprites: 7,
                culledSprites: 3),
            new RecordingPass(
                "Second",
                [],
                cameras: 1,
                cameraPlans: 0,
                drawCalls: 2,
                nonEmptyCameras: 2,
                instances: 4,
                skippedSprites: 1,
                culledSprites: 6));

        registry.Plan(new FrameSnapshot(), 320, 240, new FakeInstanceBuffer());

        RenderPassStats stats = registry.Stats;
        Assert.Equal(3, stats.CameraCount);
        Assert.Equal(2, stats.CameraPlanCount);
        Assert.Equal(3, stats.DrawCalls);
        Assert.Equal(3, stats.NonEmptyCameras);
        Assert.Equal(9, stats.InstanceCount);
        Assert.Equal(8, stats.SkippedSprites);
        Assert.Equal(9, stats.CulledSprites);
    }

    [Fact]
    public void RecordRunsPassesInRegistrationOrder()
    {
        var events = new List<string>();
        var first = new RecordingPass("Sprite", events);
        var second = new RecordingPass("Custom", events);
        var registry = new RenderPassRegistry(first, second);

        registry.Record(default);

        Assert.Equal(new[] { "record:Sprite", "record:Custom" }, events);
        Assert.Equal(1, first.RecordCount);
        Assert.Equal(1, second.RecordCount);
    }

    [Fact]
    public void PlanResetsTheAggregateAndEachPassBeforePlanning()
    {
        var events = new List<string>();
        var pass = new RecordingPass(
            "Sprite",
            events,
            cameras: 1,
            cameraPlans: 1,
            drawCalls: 1,
            nonEmptyCameras: 1,
            instances: 2,
            skippedSprites: 3,
            culledSprites: 4);
        var registry = new RenderPassRegistry(pass);
        var snapshot = new FrameSnapshot();
        var instances = new FakeInstanceBuffer();

        registry.Plan(snapshot, 320, 240, instances);
        registry.Plan(snapshot, 320, 240, instances);

        Assert.Equal(2, pass.ResetCount);
        Assert.Equal(2, pass.PlanCount);
        RenderPassStats stats = registry.Stats;
        Assert.Equal(1, stats.CameraCount);
        Assert.Equal(1, stats.CameraPlanCount);
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(1, stats.NonEmptyCameras);
        Assert.Equal(2, stats.InstanceCount);
        Assert.Equal(3, stats.SkippedSprites);
        Assert.Equal(4, stats.CulledSprites);
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
        private readonly long _skippedSprites;
        private readonly int _culledSprites;

        internal RecordingPass(
            string name,
            List<string> events,
            int cameras = 0,
            int cameraPlans = 0,
            int drawCalls = 0,
            int nonEmptyCameras = 0,
            int instances = 0,
            long skippedSprites = 0,
            int culledSprites = 0)
        {
            Name = name;
            _events = events;
            _cameras = cameras;
            _cameraPlans = cameraPlans;
            _drawCalls = drawCalls;
            _nonEmptyCameras = nonEmptyCameras;
            _instances = instances;
            _skippedSprites = skippedSprites;
            _culledSprites = culledSprites;
        }

        public string Name { get; }

        internal int ResetCount { get; private set; }

        internal int PlanCount { get; private set; }

        internal int RecordCount { get; private set; }

        public void Plan(
            FrameSnapshot snapshot,
            int framebufferWidth,
            int framebufferHeight,
            IInstanceBuffer instances,
            RenderPassStats stats)
        {
            PlanCount++;
            _events.Add($"plan:{Name}");
            stats.CameraCount += _cameras;
            stats.CameraPlanCount += _cameraPlans;
            stats.DrawCalls += _drawCalls;
            stats.NonEmptyCameras += _nonEmptyCameras;
            stats.InstanceCount += _instances;
            stats.SkippedSprites += _skippedSprites;
            stats.CulledSprites += _culledSprites;
        }

        public void Record(in RenderPassRecordContext context)
        {
            RecordCount++;
            _events.Add($"record:{Name}");
        }

        public void Reset() => ResetCount++;
    }
}
