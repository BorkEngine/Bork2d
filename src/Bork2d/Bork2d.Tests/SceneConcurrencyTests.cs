using System.Numerics;
using Bork2d.Api;
using Bork2d.Common;
using Xunit;

namespace Bork2d.Tests;

public sealed class SceneConcurrencyTests
{
    private const int WorkerCount = 4;
    private const int TransformsPerWorker = 64;

    [Fact]
    public void ConcurrentCreationMutationAndDetachFromWorkersSucceeds()
    {
        var engine = TestEngineFactory.Create();
        var transforms = new ITransform[WorkerCount][];
        var cameras = new ICamera<GameLayer>[WorkerCount];
        var sprites = new ISpriteRender<GameLayer>[WorkerCount];
        var threads = new Thread[WorkerCount];

        for (var index = 0; index < WorkerCount; index++)
        {
            var worker = index;
            threads[index] = new Thread(() => RunWorker(engine, worker, transforms, cameras, sprites))
            {
                IsBackground = true,
                Name = $"scene-worker-{worker}",
            };
        }

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), $"Worker '{thread.Name}' did not finish.");
        }

        var created = transforms.SelectMany(items => items).ToArray();
        Assert.Equal(WorkerCount * TransformsPerWorker, created.Length);

        var allIds = created
            .Select(transform => ((Transform)transform).Id)
            .Concat(cameras.Select(camera => ((Transform)camera).Id))
            .Concat(sprites.Select(sprite => ((Transform)sprite).Id))
            .ToArray();
        Assert.Equal(allIds.Length, allIds.Distinct().Count());

        for (var worker = 0; worker < WorkerCount; worker++)
        {
            Assert.NotNull(transforms[worker]);
            Assert.Same(engine.Root, cameras[worker].Parent);
            Assert.Same(engine.Root, sprites[worker].Parent);

            for (var item = 0; item < TransformsPerWorker; item++)
            {
                var transform = transforms[worker][item];
                Assert.Equal(new Vector2(worker, item), transform.LocalPosition);
                Assert.Equal(item * 0.25f, transform.LocalRotation);
                Assert.Equal(new Vector2(worker, item), transform.LocalScale);
                if (item % 2 == 0)
                {
                    Assert.Null(transform.Parent);
                }
                else
                {
                    Assert.Same(engine.Root, transform.Parent);
                }
            }
        }

        var rooted = Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder();
        var rootedIds = rooted.Select(transform => transform.Id).ToArray();
        Assert.Equal((WorkerCount * TransformsPerWorker / 2) + (WorkerCount * 2), rootedIds.Length);
        Assert.Equal(rootedIds.OrderBy(id => id).ToArray(), rootedIds);
        Assert.All(rootedIds, id => Assert.Contains(id, allIds));
    }

    private static void RunWorker(
        Engine<GameLayer, TestGame<GameLayer>> engine,
        int worker,
        ITransform[][] transforms,
        ICamera<GameLayer>[] cameras,
        ISpriteRender<GameLayer>[] sprites)
    {
        var items = new ITransform[TransformsPerWorker];
        for (var index = 0; index < TransformsPerWorker; index++)
        {
            var transform = engine.CreateTransform(engine.Root);
            transform.LocalPosition = new Vector2(worker, index);
            transform.LocalRotation = index * 0.25f;
            transform.LocalScale = transform.LocalPosition;
            if (index % 2 == 0)
            {
                transform.Parent = null;
            }

            items[index] = transform;
        }

        var camera = engine.CreateCamera(engine.Root);
        camera.Size = worker + 1f;
        camera.CullingMask = GameLayer.World;
        cameras[worker] = camera;

        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Layer = GameLayer.Player;
        sprite.ZIndex = worker;
        sprites[worker] = sprite;

        transforms[worker] = items;
    }
}
