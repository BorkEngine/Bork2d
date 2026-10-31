using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Textures;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bork2d.Tests.Render.Textures;

/// <summary>
/// Deterministic tests for the texture-page state machine with a fake <see cref="IAssets"/> and a
/// fake GPU seam (M8 §6.5): page-key dedupe, pending skip, content validation, the placeholder and
/// its sizing rule, no retry after failure, capacity policy, counters, and allocation-free lookups.
/// </summary>
public sealed class TexturePageStoreTests
{
    private const int DefaultLimit = 4096;
    private const int DefaultMaxDimension = 16384;

    [Fact]
    public void StartUploadsThePlaceholderAtIndexZero()
    {
        Harness harness = CreateHarness();

        Assert.True(harness.Store.IsPlaceholderReady);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(new[] { TexturePageCapacity.Initial }, harness.Gpu.CapacityRequests);
        Assert.Equal(1, harness.Gpu.ResidentPageCount);

        TexturePageUpload upload = Assert.Single(harness.Gpu.Uploads);
        Assert.Equal(TexturePageCapacity.PlaceholderIndex, upload.Index);
        Assert.Equal(TexturePlaceholder.Size, upload.Width);
        Assert.Equal(TexturePlaceholder.Size, upload.Height);
        Assert.Equal(TextureFormat.Rgba8, upload.Format);
        Assert.Equal(TextureFilter.Nearest, upload.Filter);
        Assert.Equal(TextureWrap.Clamp, upload.Wrap);
        Assert.Equal(TexturePlaceholder.PayloadBytes, upload.PayloadLength);
        Assert.Equal(0, harness.Assets.LoadAssetCount);
        Assert.Equal(0, harness.Assets.LoadDataCount);
    }

    [Fact]
    public void StartRejectsALimitBelowTheInitialCapacity()
    {
        var exception = Assert.Throws<Bork2dException>(
            () => new TexturePageStore(
                new FakeTextureAssets(),
                new FakeTexturePageGpu(),
                TexturePageCapacity.Initial - 1,
                DefaultMaxDimension,
                NullLogger.Instance));

        Assert.Contains("256", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentPathMapsStraightToThePlaceholderWithoutALoad(string? path)
    {
        Harness harness = CreateHarness();

        TexturePageLookup lookup = harness.Store.EnsurePage(path);

        Assert.Equal(TexturePageLookupKind.Placeholder, lookup.Kind);
        Assert.Equal(TexturePageCapacity.PlaceholderIndex, lookup.Index);
        Assert.Equal(TexturePlaceholder.Size, lookup.Width);
        Assert.Equal(TexturePlaceholder.Size, lookup.Height);
        Assert.Equal(0, harness.Assets.LoadAssetCount);
        Assert.Equal(0, harness.Store.PendingLoadCount);
    }

    [Fact]
    public void FirstSightingStartsExactlyOneLoadPerPathAndReturnsPending()
    {
        Harness harness = CreateHarness();

        TexturePageLookup first = harness.Store.EnsurePage("textures/player");
        TexturePageLookup second = harness.Store.EnsurePage("textures/player");

        Assert.Equal(TexturePageLookupKind.Pending, first.Kind);
        Assert.Equal(TexturePageLookupKind.Pending, second.Kind);
        Assert.Equal(1, harness.Assets.LoadAssetCount);
        Assert.Equal(["textures/player"], harness.Assets.LoadAssetPaths);
        Assert.Equal(1, harness.Store.PendingLoadCount);
        Assert.Equal(1, harness.Store.TexturePageCount);
    }

    [Fact]
    public void APageIsSkippedUntilBothTheDescriptorAndThePayloadComplete()
    {
        Harness harness = CreateHarness();
        harness.Store.EnsurePage("textures/player");

        harness.Assets.CompleteDescriptor(
            "textures/player",
            Descriptor(2, 2, "textures/player-raw"));
        harness.Store.Pump();

        Assert.Equal(
            TexturePageLookupKind.Pending,
            harness.Store.EnsurePage("textures/player").Kind);
        Assert.Equal(1, harness.Assets.LoadDataCount);
        Assert.Equal(1, harness.Store.PendingLoadCount);

        harness.Assets.CompletePayload("textures/player-raw", Payload(2, 2));
        harness.Store.Pump();

        TexturePageLookup lookup = harness.Store.EnsurePage("textures/player");
        Assert.Equal(TexturePageLookupKind.Ready, lookup.Kind);
        Assert.Equal(1, lookup.Index);
        Assert.Equal(2, lookup.Width);
        Assert.Equal(2, lookup.Height);
        Assert.Equal(2, harness.Store.TexturePageCount);
        Assert.Equal(0, harness.Store.PendingLoadCount);
    }

    [Fact]
    public void AReadyPageKeepsItsDescriptorDimensionsAndIndexAndIsLoadedOnce()
    {
        Harness harness = CreateHarness();

        LoadPage(harness, "textures/atlas", Descriptor(4, 2, "textures/atlas-raw"), Payload(4, 2));

        for (var index = 0; index < 5; index++)
        {
            TexturePageLookup lookup = harness.Store.EnsurePage("textures/atlas");
            Assert.Equal(TexturePageLookupKind.Ready, lookup.Kind);
            Assert.Equal(1, lookup.Index);
            Assert.Equal(4, lookup.Width);
            Assert.Equal(2, lookup.Height);
        }

        Assert.Equal(1, harness.Assets.LoadAssetCount);
        Assert.Equal(1, harness.Assets.LoadDataCount);
        Assert.Equal(2, harness.Store.TexturePageCount);
    }

    [Fact]
    public void AnUnsupportedFormatFailsThePathAndDrawsThePlaceholder()
    {
        Harness harness = CreateHarness();
        TextureDescriptor descriptor = Descriptor(2, 2, "textures/bad-raw");
        descriptor.Format = (TextureFormat)99;

        AssertContentFailure(
            harness,
            "textures/bad",
            descriptor,
            Payload(2, 2),
            TexturePageFailure.UnsupportedFormat,
            expectedWidth: 2,
            expectedHeight: 2);
    }

    [Fact]
    public void ZeroDimensionsFailThePathAndUseThePlaceholderSize()
    {
        Harness harness = CreateHarness();

        AssertContentFailure(
            harness,
            "textures/zero",
            Descriptor(0, 0, "textures/zero-raw"),
            [],
            TexturePageFailure.InvalidDimensions,
            expectedWidth: TexturePlaceholder.Size,
            expectedHeight: TexturePlaceholder.Size);
    }

    [Fact]
    public void DimensionsOverTheDeviceLimitFailUsingThePlaceholderSize()
    {
        Harness harness = CreateHarness(maxDimension: 64);

        AssertContentFailure(
            harness,
            "textures/huge",
            Descriptor(65, 1, "textures/huge-raw"),
            [],
            TexturePageFailure.DimensionLimit,
            expectedWidth: TexturePlaceholder.Size,
            expectedHeight: TexturePlaceholder.Size);
    }

    [Fact]
    public void APayloadLengthMismatchKeepsTheDeclaredDimensions()
    {
        Harness harness = CreateHarness();

        AssertContentFailure(
            harness,
            "textures/short",
            Descriptor(4, 2, "textures/short-raw"),
            new byte[7],
            TexturePageFailure.PayloadLength,
            expectedWidth: 4,
            expectedHeight: 2);
    }

    [Fact]
    public void AMissingPayloadKeepsTheDeclaredDimensions()
    {
        Harness harness = CreateHarness();
        TextureDescriptor descriptor = Descriptor(3, 5, "textures/no-payload-raw");
        descriptor.Payload = DataRef.None;

        AssertContentFailure(
            harness,
            "textures/no-payload",
            descriptor,
            payload: [],
            TexturePageFailure.MissingPayload,
            expectedWidth: 3,
            expectedHeight: 5,
            expectPayloadLoad: false);
    }

    [Fact]
    public void AFaultedDescriptorLoadFailsThePathUsingThePlaceholderSize()
    {
        Harness harness = CreateHarness();

        AssertContentFailure(
            harness,
            "textures/missing",
            descriptor: null,
            payload: [],
            TexturePageFailure.MissingDescriptor,
            expectedWidth: TexturePlaceholder.Size,
            expectedHeight: TexturePlaceholder.Size,
            expectPayloadLoad: false);
    }

    [Fact]
    public void AFaultedPayloadLoadKeepsTheDeclaredDimensions()
    {
        Harness harness = CreateHarness();
        TextureDescriptor descriptor = Descriptor(7, 3, "textures/faulted-raw");
        harness.Store.EnsurePage("textures/faulted");
        harness.Assets.CompleteDescriptor("textures/faulted", descriptor);
        harness.Store.Pump();
        harness.Assets.PayloadSource("textures/faulted-raw").SetException(
            new InvalidOperationException("decode failed"));

        harness.Store.Pump();

        AssertFailedPath(
            harness,
            "textures/faulted",
            TexturePageFailure.MissingPayload,
            expectedWidth: 7,
            expectedHeight: 3);
    }

    [Fact]
    public void ASynchronousDescriptorLoadFailureIsAContentFailure()
    {
        Harness harness = CreateHarness();
        harness.Assets.DescriptorLoadException = new InvalidOperationException("disposed");

        TexturePageLookup lookup = harness.Store.EnsurePage("textures/sync");

        Assert.Equal(TexturePageLookupKind.Placeholder, lookup.Kind);
        Assert.Equal(TexturePlaceholder.Size, lookup.Width);
        AssertFailedPath(
            harness,
            "textures/sync",
            TexturePageFailure.LoadFault,
            expectedWidth: TexturePlaceholder.Size,
            expectedHeight: TexturePlaceholder.Size);
        Assert.Equal(0, harness.Store.PendingLoadCount);
    }

    [Fact]
    public void AFailedPathIsNeverRetried()
    {
        Harness harness = CreateHarness();
        harness.Assets.CompleteDescriptor("textures/broken", Descriptor(2, 2, "textures/broken-raw"));
        harness.Assets.CompletePayload("textures/broken-raw", []);

        harness.Store.EnsurePage("textures/broken");
        harness.Store.Pump();
        AssertFailedPath(
            harness,
            "textures/broken",
            TexturePageFailure.PayloadLength,
            expectedWidth: 2,
            expectedHeight: 2);
        Assert.Equal(1, harness.Assets.LoadAssetCount);

        for (var index = 0; index < 5; index++)
        {
            TexturePageLookup lookup = harness.Store.EnsurePage("textures/broken");
            Assert.Equal(TexturePageLookupKind.Placeholder, lookup.Kind);
            Assert.Equal(TexturePageCapacity.PlaceholderIndex, lookup.Index);
        }

        harness.Store.Pump();

        Assert.Equal(1, harness.Assets.LoadAssetCount);
        Assert.Equal(1, harness.Assets.LoadDataCount);
        Assert.Equal(1, harness.Store.FailedPathCount);
    }

    [Fact]
    public void FailedPathsDoNotConsumeCapacityOrTriggerGrowth()
    {
        Harness harness = CreateHarness(limit: TexturePageCapacity.Initial);
        harness.Assets.DescriptorLoadException = new InvalidOperationException("missing");

        const int failed = 300;
        for (var index = 0; index < failed; index++)
        {
            harness.Store.EnsurePage($"textures/failed-{index}");
            harness.Store.Pump();
        }

        Assert.Equal(failed, harness.Store.FailedPathCount);
        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(new[] { TexturePageCapacity.Initial }, harness.Gpu.CapacityRequests);
        Assert.Equal(0, harness.Store.GrowthCount);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.Single(harness.Gpu.Uploads);
    }

    [Fact]
    public void CapacityDoublesWhenTheNextRealPageNeedsAnIndex()
    {
        Harness harness = CreateHarness(limit: 512);

        for (var index = 0; index < TexturePageCapacity.InitialRealPages; index++)
        {
            LoadPage(
                harness,
                $"textures/page-{index}",
                Descriptor(1, 1, $"textures/page-{index}-raw"),
                Payload(1, 1));
        }

        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(0, harness.Store.GrowthCount);
        Assert.Equal(new[] { TexturePageCapacity.Initial }, harness.Gpu.CapacityRequests);
        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCount);

        LoadPage(
            harness,
            "textures/page-256",
            Descriptor(1, 1, "textures/page-256-raw"),
            Payload(1, 1));

        Assert.Equal(512, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(1, harness.Store.GrowthCount);
        Assert.Equal(new[] { TexturePageCapacity.Initial, 512 }, harness.Gpu.CapacityRequests);
        Assert.Equal(TexturePageCapacity.Initial + 1, harness.Store.TexturePageCount);
        Assert.Equal(256, harness.Store.EnsurePage("textures/page-256").Index);
    }

    [Fact]
    public void CapacityReachingTheDeviceLimitIsAnExplicitFailureNamingIt()
    {
        Harness harness = CreateHarness(limit: TexturePageCapacity.Initial);

        for (var index = 0; index < TexturePageCapacity.InitialRealPages; index++)
        {
            LoadPage(
                harness,
                $"textures/page-{index}",
                Descriptor(1, 1, $"textures/page-{index}-raw"),
                Payload(1, 1));
        }

        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCount);
        harness.Store.EnsurePage("textures/overflow");
        harness.Assets.CompleteDescriptor(
            "textures/overflow",
            Descriptor(1, 1, "textures/overflow-raw"));
        harness.Assets.CompletePayload("textures/overflow-raw", Payload(1, 1));

        var exception = Assert.Throws<Bork2dException>(harness.Store.Pump);

        Assert.Contains("256", exception.Message, StringComparison.Ordinal);
        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.Store.FailedPathCount);
    }

    [Fact]
    public void AnUploadingPageIsSkippedUntilItsFenceSignals()
    {
        Harness harness = CreateHarness();
        harness.Gpu.UploadsCompleteImmediately = false;

        harness.Store.EnsurePage("textures/slow");
        harness.Assets.CompleteDescriptor("textures/slow", Descriptor(2, 2, "textures/slow-raw"));
        harness.Assets.CompletePayload("textures/slow-raw", Payload(2, 2));
        harness.Store.Pump();

        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage("textures/slow").Kind);
        Assert.Equal(1, harness.Store.UploadingCount);
        Assert.Equal(1, harness.Store.TexturePageCount);

        harness.Gpu.SignalUpload(1);
        harness.Store.Pump();

        TexturePageLookup lookup = harness.Store.EnsurePage("textures/slow");
        Assert.Equal(TexturePageLookupKind.Ready, lookup.Kind);
        Assert.Equal(1, lookup.Index);
        Assert.Equal(0, harness.Store.UploadingCount);
        Assert.Equal(2, harness.Store.TexturePageCount);
    }

    [Fact]
    public void APageOnlyCountsOnceItsDescriptorAndPayloadAreValid()
    {
        Harness harness = CreateHarness();

        LoadPage(harness, "textures/a", Descriptor(1, 1, "textures/a-raw"), Payload(1, 1));
        LoadPage(harness, "textures/b", Descriptor(2, 1, "textures/b-raw"), Payload(2, 1));

        Assert.Equal(3, harness.Store.TexturePageCount);
        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(0, harness.Store.FailedPathCount);
        Assert.Equal(0, harness.Store.PendingLoadCount);
        Assert.Equal(0, harness.Store.UploadingCount);
    }

    [Fact]
    public void TheSteadyStateLookupAllocatesNothing()
    {
        Harness harness = CreateHarness();
        LoadPage(harness, "textures/steady", Descriptor(2, 2, "textures/steady-raw"), Payload(2, 2));

        // Warm the static paths and the tiered JIT before measuring.
        for (var index = 0; index < 32; index++)
        {
            _ = harness.Store.EnsurePage("textures/steady");
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var index = 0; index < 10_000; index++)
        {
            TexturePageLookup lookup = harness.Store.EnsurePage("textures/steady");
            total += lookup.Index;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(10_000, total);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void DestroyStopsTheGpuAndIgnoresLaterLoadCompletions()
    {
        Harness harness = CreateHarness();
        harness.Store.EnsurePage("textures/pending");
        Assert.Equal(1, harness.Store.PendingLoadCount);

        harness.Store.Destroy();
        harness.Store.Destroy();

        Assert.Equal(1, harness.Gpu.DestroyCount);
        Assert.Equal(0, harness.Store.PendingLoadCount);

        // The loader may still complete after the renderer joined; the store ignores it.
        harness.Assets.CompleteDescriptor(
            "textures/pending",
            Descriptor(2, 2, "textures/pending-raw"));
        harness.Assets.PayloadSource("textures/pending-raw").SetException(
            new InvalidOperationException("stopped"));

        Assert.Equal(1, harness.Store.TexturePageCount);
    }

    private static Harness CreateHarness(
        int limit = DefaultLimit,
        int maxDimension = DefaultMaxDimension)
    {
        var assets = new FakeTextureAssets();
        var gpu = new FakeTexturePageGpu();
        var store = new TexturePageStore(assets, gpu, limit, maxDimension, NullLogger.Instance);
        store.Start();
        return new Harness(store, assets, gpu);
    }

    private static void LoadPage(
        Harness harness,
        string path,
        TextureDescriptor descriptor,
        byte[] payload)
    {
        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage(path).Kind);
        harness.Assets.CompleteDescriptor(path, descriptor);
        harness.Store.Pump();
        harness.Assets.CompletePayload(descriptor.Payload.Path!, payload);
        harness.Store.Pump();
        Assert.Equal(TexturePageLookupKind.Ready, harness.Store.EnsurePage(path).Kind);
    }

    private static void AssertContentFailure(
        Harness harness,
        string path,
        TextureDescriptor? descriptor,
        byte[] payload,
        TexturePageFailure expectedFailure,
        int expectedWidth,
        int expectedHeight,
        bool expectPayloadLoad = true)
    {
        harness.Store.EnsurePage(path);
        if (descriptor is null)
        {
            harness.Assets.DescriptorSource(path).SetException(
                new InvalidOperationException("missing descriptor"));
        }
        else
        {
            harness.Assets.CompleteDescriptor(path, descriptor);
            harness.Store.Pump();
            if (expectPayloadLoad)
            {
                harness.Assets.CompletePayload(descriptor.Payload.Path!, payload);
            }
        }

        harness.Store.Pump();

        AssertFailedPath(harness, path, expectedFailure, expectedWidth, expectedHeight);
        Assert.DoesNotContain(
            harness.Gpu.Uploads,
            upload => upload.Index != TexturePageCapacity.PlaceholderIndex);
    }

    private static void AssertFailedPath(
        Harness harness,
        string path,
        TexturePageFailure expectedFailure,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.True(
            harness.Store.TryGetPageState(path, out TexturePageState state, out TexturePageFailure failure),
            $"The store has no page record for '{path}'.");
        Assert.Equal(TexturePageState.Failed, state);
        Assert.Equal(expectedFailure, failure);
        Assert.Equal(1, harness.Store.FailedPathCount);
        Assert.Equal(1, harness.Store.TexturePageCount);

        TexturePageLookup lookup = harness.Store.EnsurePage(path);
        Assert.Equal(TexturePageLookupKind.Placeholder, lookup.Kind);
        Assert.Equal(TexturePageCapacity.PlaceholderIndex, lookup.Index);
        Assert.Equal(expectedWidth, lookup.Width);
        Assert.Equal(expectedHeight, lookup.Height);
    }

    private static TextureDescriptor Descriptor(uint width, uint height, string payloadPath) => new()
    {
        Width = width,
        Height = height,
        Format = TextureFormat.Rgba8,
        Filter = TextureFilter.Linear,
        Wrap = TextureWrap.Clamp,
        Payload = payloadPath,
    };

    private static byte[] Payload(uint width, uint height) => new byte[width * height * 4];

    private sealed class Harness
    {
        internal Harness(TexturePageStore store, FakeTextureAssets assets, FakeTexturePageGpu gpu)
        {
            Store = store;
            Assets = assets;
            Gpu = gpu;
        }

        internal TexturePageStore Store { get; }

        internal FakeTextureAssets Assets { get; }

        internal FakeTexturePageGpu Gpu { get; }
    }
}
