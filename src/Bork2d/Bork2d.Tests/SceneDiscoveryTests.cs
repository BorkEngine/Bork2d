using Bork2d.Common;
using Xunit;

namespace Bork2d.Tests;

public sealed class SceneDiscoveryTests
{
    [Fact]
    public void RootedTransformsAreDiscoveredInEntityIdOrder()
    {
        var engine = TestEngineFactory.Create();
        var first = engine.CreateTransform(engine.Root);
        var nested = engine.CreateTransform(first);
        var camera = engine.CreateCamera(engine.Root);
        var standalone = engine.CreateTransform();
        var sprite = engine.CreateSpriteRender(nested);
        var last = engine.CreateTransform(engine.Root);

        var rooted = Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder();

        var ids = rooted.Select(transform => transform.Id).ToArray();
        Assert.Equal(5, ids.Length);
        Assert.Equal(ids.OrderBy(id => id).ToArray(), ids);
        Assert.DoesNotContain(rooted, transform => ReferenceEquals(transform, standalone));

        Assert.Equal(new[] { ((Transform)camera).Id }, rooted.OfType<Camera<GameLayer>>().Select(c => c.Id).ToArray());
        Assert.Equal(new[] { ((Transform)sprite).Id }, rooted.OfType<SpriteRender<GameLayer>>().Select(s => s.Id).ToArray());

        Assert.Contains(rooted, transform => ReferenceEquals(transform, first));
        Assert.Contains(rooted, transform => ReferenceEquals(transform, nested));
        Assert.Contains(rooted, transform => ReferenceEquals(transform, last));
    }

    [Fact]
    public void EntityIdsAreMonotonicAndNeverReused()
    {
        var engine = TestEngineFactory.Create();
        var root = Assert.IsType<SceneRoot>(engine.Root);
        var first = (Transform)engine.CreateTransform();
        var second = (Transform)engine.CreateTransform();

        Assert.Equal(0u, root.Id);
        Assert.Equal(1u, first.Id);
        Assert.Equal(2u, second.Id);

        var detached = engine.CreateTransform(engine.Root);
        var detachedId = ((Transform)detached).Id;
        detached.Parent = null;

        var camera = (Transform)engine.CreateCamera(engine.Root);
        var sprite = (Transform)engine.CreateSpriteRender(engine.Root);
        var transform = (Transform)engine.CreateTransform(engine.Root);

        Assert.True(camera.Id > detachedId);
        Assert.True(sprite.Id > camera.Id);
        Assert.True(transform.Id > sprite.Id);
        Assert.NotEqual(detachedId, transform.Id);
        Assert.DoesNotContain(root.GetRootedTransformsInIdOrder(), rooted => rooted.Id == detachedId);
    }

    [Fact]
    public void DetachedSubtreesAreNotDiscovered()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        var child = engine.CreateTransform(parent);

        parent.Parent = null;

        var rooted = Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder();
        Assert.DoesNotContain(rooted, transform => ReferenceEquals(transform, parent));
        Assert.DoesNotContain(rooted, transform => ReferenceEquals(transform, child));
        Assert.Same(parent, child.Parent);
    }
}
