using System.Collections.Concurrent;
using Bork2d.Api;
using Bork2d.Assets;
using Xunit;

namespace Bork2d.Tests;

public sealed class AssetFileSystemConcurrencyTests
{
    [Fact]
    public void ConcurrentMountsAndLookupsObserveConsistentSnapshots()
    {
        const int packCount = 8;
        const int readerCount = 4;
        const int lookupIterations = 250;

        using var directory = TempPackDirectory.CreateUnderTemp();
        string[] packPaths = new string[packCount];
        for (int index = 0; index < packCount; index++)
        {
            int captured = index;
            packPaths[index] = directory.WritePack(
                $"pack-{index}.pck",
                builder =>
                {
                    builder.AddRaw($"data/pack{captured}", [(byte)captured]);
                    builder.AddRaw("data/shared", [(byte)captured]);
                });
        }

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(packPaths[0]);

        using var start = new ManualResetEventSlim(false);
        var failures = new ConcurrentQueue<Exception>();
        var workers = new List<Thread>(packCount - 1 + readerCount);

        for (int index = 1; index < packCount; index++)
        {
            string path = packPaths[index];
            var worker = new Thread(() =>
            {
                try
                {
                    start.Wait();
                    fileSystem.LoadPack(path);
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            })
            {
                IsBackground = true,
                Name = $"mount-{index}",
            };
            worker.Start();
            workers.Add(worker);
        }

        for (int reader = 0; reader < readerCount; reader++)
        {
            var worker = new Thread(() =>
            {
                try
                {
                    start.Wait();
                    for (int iteration = 0; iteration < lookupIterations; iteration++)
                    {
                        IReadOnlyList<AssetInfo> visible = fileSystem.Browse();
                        Assert.InRange(visible.Count, 2, packCount + 1);

                        string[] paths = visible.Select(info => info.Path).ToArray();
                        Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
                        Assert.True(
                            paths.SequenceEqual(
                                paths.OrderBy(name => name, StringComparer.Ordinal)),
                            "Browse results must stay sorted by path ordinal.");

                        foreach (AssetInfo info in visible)
                        {
                            Assert.True(fileSystem.Exists(info.Path));
                        }

                        Assert.True(fileSystem.Exists("data/pack0"));
                    }
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            })
            {
                IsBackground = true,
                Name = $"reader-{reader}",
            };
            worker.Start();
            workers.Add(worker);
        }

        start.Set();
        foreach (Thread worker in workers)
        {
            Assert.True(
                worker.Join(TimeSpan.FromSeconds(30)),
                $"Worker '{worker.Name}' did not finish in time.");
        }

        Assert.Empty(failures);

        IReadOnlyList<AssetInfo> final = fileSystem.Browse();
        Assert.Equal(packCount + 1, final.Count);
        for (int index = 0; index < packCount; index++)
        {
            Assert.True(fileSystem.Exists($"data/pack{index}"));
        }

        Assert.True(fileSystem.Exists("data/shared"));
    }
}
