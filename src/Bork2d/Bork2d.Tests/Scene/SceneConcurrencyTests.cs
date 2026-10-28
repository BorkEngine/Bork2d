using System.Numerics;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

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

        var created = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var worker = 0; worker < WorkerCount; worker++)
        {
            Assert.NotNull(transforms[worker]);
            Assert.Same(engine.Root, cameras[worker].Parent);
            Assert.Same(engine.Root, sprites[worker].Parent);

            foreach (var transform in transforms[worker])
            {
                Assert.True(created.Add(transform), "Every created transform must be a distinct instance.");
            }

            Assert.True(created.Add(cameras[worker]), "Every created camera must be a distinct instance.");
            Assert.True(created.Add(sprites[worker]), "Every created sprite must be a distinct instance.");

            for (var item = 0; item < TransformsPerWorker; item++)
            {
                var transform = transforms[worker][item];
                TestAssertions.Close(new Vector2(worker, item), transform.LocalPosition);
                TestAssertions.Close((item % 8) * 15f, transform.LocalRotation);
                TestAssertions.Close(new Vector2(worker + 1f, item + 1f), transform.LocalScale);
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

        // No IDs or ordering exist. Membership is proven exactly by each object's parent state:
        // odd-indexed transforms, every camera, and every sprite are rooted; even ones are not.
        Assert.Equal(WorkerCount * (TransformsPerWorker + 2), created.Count);
    }

    private static void RunWorker(
        GameEngine<GameLayer> engine,
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
            transform.LocalRotation = (index % 8) * 15f;
            transform.LocalScale = new Vector2(worker + 1f, index + 1f);
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
