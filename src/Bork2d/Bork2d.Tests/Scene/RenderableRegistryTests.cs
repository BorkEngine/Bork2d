using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Tests for the renderable registry: reserved core slots, deterministic producer order, attached
/// creation, and a test-only custom renderable captured through <see cref="SnapshotCapture"/> with
/// no core switch change.
/// </summary>
public sealed class RenderableRegistryTests
{
    [Fact]
    public void CoreDefinitionsFillTheReservedSlotsInDeterministicOrder()
    {
        var structuralLock = new Lock();
        var cameraDefinition = new CameraRenderableDefinition<GameLayer>(
            structuralLock,
            new FakeWindow());
        var spriteDefinition = new SpriteRenderableDefinition<GameLayer>(structuralLock);

        var registry = new RenderableRegistry(
            FrameSnapshot.ReservedSlotCount,
            cameraDefinition,
            spriteDefinition);

        Assert.Equal(FrameSnapshot.ReservedSlotCount, registry.ReservedSlots);
        Assert.Equal(new[] { FrameSnapshot.CameraSlot, FrameSnapshot.SpriteSlot }, Slots(registry));
        Assert.Equal(new[] { "Camera", "Sprite" }, Names(registry));
        Assert.Same(cameraDefinition.Producer, registry.Producers[0]);
        Assert.Same(spriteDefinition.Producer, registry.Producers[1]);
    }

    [Fact]
    public void CustomDefinitionGetsTheNextSlotAfterTheReservedCount()
    {
        var structuralLock = new Lock();
        var customDefinition = new TestRenderableDefinition(structuralLock);

        var registry = new RenderableRegistry(
            FrameSnapshot.ReservedSlotCount,
            new CameraRenderableDefinition<GameLayer>(structuralLock, new FakeWindow()),
            new SpriteRenderableDefinition<GameLayer>(structuralLock),
            customDefinition);

        Assert.Equal(
            new[] { FrameSnapshot.CameraSlot, FrameSnapshot.SpriteSlot, FrameSnapshot.ReservedSlotCount },
            Slots(registry));
        Assert.Equal(FrameSnapshot.ReservedSlotCount, customDefinition.Producer.Slot);
    }

    [Fact]
    public void CoreDefinitionsCreateAttachedNodes()
    {
        var structuralLock = new Lock();
        var root = new SceneRoot(structuralLock);
        var registry = new RenderableRegistry(
            FrameSnapshot.ReservedSlotCount,
            new CameraRenderableDefinition<GameLayer>(structuralLock, new FakeWindow()),
            new SpriteRenderableDefinition<GameLayer>(structuralLock));

        var camera = Assert.IsType<Camera<GameLayer>>(
            registry.Create(typeof(Camera<GameLayer>), root));
        var sprite = Assert.IsType<SpriteRender<GameLayer>>(
            registry.Create(typeof(SpriteRender<GameLayer>), root));

        Assert.Same(root, camera.Parent);
        Assert.Same(root, sprite.Parent);
        Assert.Equal(2, ((ISceneNode)root).ChildCountCore);
    }

    [Fact]
    public void CustomDefinitionCreatesAnAttachedNodeAndCapturesTypedData()
    {
        var structuralLock = new Lock();
        var root = new SceneRoot(structuralLock);
        var customDefinition = new TestRenderableDefinition(structuralLock);
        var registry = new RenderableRegistry(
            FrameSnapshot.ReservedSlotCount,
            new CameraRenderableDefinition<GameLayer>(structuralLock, new FakeWindow()),
            new SpriteRenderableDefinition<GameLayer>(structuralLock),
            customDefinition);
        var capture = new SnapshotCapture(structuralLock, root, registry.Producers);

        var parent = new Transform(structuralLock);
        parent.AttachAsNewChild(root);
        parent.LocalPosition = new Vector2(10f, 20f);
        var custom = Assert.IsType<TestRenderable>(registry.Create(typeof(TestRenderable), parent));
        custom.LocalPosition = new Vector2(1f, 2f);
        custom.Value = 42;
        _ = registry.Create(typeof(Camera<GameLayer>), root);
        _ = registry.Create(typeof(SpriteRender<GameLayer>), root);

        var snapshot = new FrameSnapshot();
        capture.Capture(snapshot, sequence: 1, time: 0d, deltaTime: 0d);

        RenderDataBuffer<TestRenderData> buffer = snapshot.GetBuffer<TestRenderData>(
            customDefinition.Producer.Slot);
        Assert.Equal(1, buffer.Count);
        Assert.Equal(42, buffer.Items[0].Value);
        Assert.Equal(new Vector2(11f, 22f), buffer.Items[0].WorldMatrix.Translation);
        Assert.Equal(1, snapshot.CameraCount);
        Assert.Equal(1, snapshot.SpriteCount);
        Assert.Same(parent, custom.Parent);
    }

    [Fact]
    public void UnknownNodeTypeIsRejected()
    {
        var structuralLock = new Lock();
        var registry = new RenderableRegistry(
            FrameSnapshot.ReservedSlotCount,
            new CameraRenderableDefinition<GameLayer>(structuralLock, new FakeWindow()),
            new SpriteRenderableDefinition<GameLayer>(structuralLock));

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => registry.Create(typeof(TestRenderable), new SceneRoot(structuralLock)));

        Assert.Contains(typeof(TestRenderable).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateNodeTypeIsRejected()
    {
        var structuralLock = new Lock();

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => new RenderableRegistry(
                FrameSnapshot.ReservedSlotCount,
                new CameraRenderableDefinition<GameLayer>(structuralLock, new FakeWindow()),
                new CameraRenderableDefinition<GameLayer>(structuralLock, new FakeWindow())));

        Assert.Contains(typeof(Camera<GameLayer>).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservedSlotsMustBeFilledByTheLeadingDefinitions()
    {
        var structuralLock = new Lock();

        Assert.Throws<ArgumentException>(
            () => new RenderableRegistry(
                FrameSnapshot.ReservedSlotCount,
                new CameraRenderableDefinition<GameLayer>(structuralLock, new FakeWindow())));
    }

    private static int[] Slots(RenderableRegistry registry)
    {
        var slots = new int[registry.Producers.Count];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = registry.Producers[index].Slot;
        }

        return slots;
    }

    private static string[] Names(RenderableRegistry registry)
    {
        var names = new string[registry.Producers.Count];
        for (var index = 0; index < names.Length; index++)
        {
            names[index] = registry.Producers[index].Name;
        }

        return names;
    }

    /// <summary>A test-only renderable type that the core engine does not know about.</summary>
    private sealed class TestRenderable : Transform
    {
        internal TestRenderable(Lock structuralLock)
            : base(structuralLock)
        {
        }

        internal int Value { get; set; }
    }

    /// <summary>The typed record the test-only producer writes.</summary>
    private readonly record struct TestRenderData(int Value, Matrix3x2 WorldMatrix);

    /// <summary>A test-only producer: no core switch knows this type.</summary>
    private sealed class TestRenderableProducer : RenderDataProducer<TestRenderable>
    {
        internal TestRenderableProducer()
            : base("TestRenderable")
        {
        }

        protected override bool Capture(
            TestRenderable node,
            in Matrix3x2 parentWorld,
            FrameSnapshot snapshot,
            out Matrix3x2 world)
        {
            Matrix3x2 local = node.CaptureLocalMatrix();
            world = local * parentWorld;
            snapshot.GetBuffer<TestRenderData>(Slot).Add(new TestRenderData(node.Value, world));
            return true;
        }
    }

    /// <summary>A test-only definition: creation and capture live in the test assembly.</summary>
    private sealed class TestRenderableDefinition : IRenderableDefinition
    {
        private readonly Lock _structuralLock;

        internal TestRenderableDefinition(Lock structuralLock)
        {
            _structuralLock = structuralLock;
            Producer = new TestRenderableProducer();
        }

        public Type NodeType => typeof(TestRenderable);

        public IRenderDataProducer Producer { get; }

        public Transform Create(ISceneNode parent)
        {
            ArgumentNullException.ThrowIfNull(parent);
            var node = new TestRenderable(_structuralLock);
            node.AttachAsNewChild(parent);
            return node;
        }
    }
}
