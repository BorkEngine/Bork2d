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
/// its sizing rule, failure until a mount retries it, catalog-aware retirement, the per-frame upload
/// budget and its priority order, capacity policy, counters, and allocation-free lookups.
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
    public void AFailedPathIsNotRetriedWithinTheSameCatalog()
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
    public void AStartupCapacityLargerThanTheBaseSetsTheInitialTableSize()
    {
        Harness harness = CreateHarness(
            limit: 8192,
            initialCapacity: TexturePageCapacity.StartupReservation);

        Assert.Equal(
            TexturePageCapacity.StartupReservation,
            harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(
            new[] { TexturePageCapacity.StartupReservation },
            harness.Gpu.CapacityRequests);
        Assert.Equal(0, harness.Store.GrowthCount);
        Assert.Equal(1, harness.Store.TexturePageCount);
    }

    [Fact]
    public void GrowthDoublesFromTheStartupCapacity()
    {
        const int startupCapacity = 512;
        const int pagesThatFit = startupCapacity - 1;
        Harness harness = CreateHarness(limit: 4096, initialCapacity: startupCapacity);

        for (var index = 0; index < pagesThatFit; index++)
        {
            LoadPage(
                harness,
                $"textures/page-{index}",
                Descriptor(1, 1, $"textures/page-{index}-raw"),
                Payload(1, 1));
        }

        Assert.Equal(startupCapacity, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(0, harness.Store.GrowthCount);
        Assert.Equal(new[] { startupCapacity }, harness.Gpu.CapacityRequests);

        LoadPage(
            harness,
            "textures/page-overflow",
            Descriptor(1, 1, "textures/page-overflow-raw"),
            Payload(1, 1));

        Assert.Equal(1024, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(1, harness.Store.GrowthCount);
        Assert.Equal(new[] { startupCapacity, 1024 }, harness.Gpu.CapacityRequests);
    }

    [Fact]
    public void AStartupCapacityAboveTheDeviceLimitIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TexturePageStore(
                new FakeTextureAssets(),
                new FakeTexturePageGpu(),
                512,
                DefaultMaxDimension,
                NullLogger.Instance,
                initialCapacity: 1024));
    }

    [Fact]
    public void AStartupCapacityBelowTheBaseCapacityIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TexturePageStore(
                new FakeTextureAssets(),
                new FakeTexturePageGpu(),
                DefaultLimit,
                DefaultMaxDimension,
                NullLogger.Instance,
                initialCapacity: TexturePageCapacity.Initial - 1));
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
    public void TheUploadBudgetStartsOnlyTheAllowedBytesAndALaterPumpContinuesTheQueue()
    {
        Harness harness = CreateHarness(uploadBudget: 128);
        StagePage(harness, "textures/a", Descriptor(4, 4, "textures/a-raw"), Payload(4, 4));
        StagePage(harness, "textures/b", Descriptor(4, 4, "textures/b-raw"), Payload(4, 4));
        StagePage(harness, "textures/c", Descriptor(4, 4, "textures/c-raw"), Payload(4, 4));
        StagePage(harness, "textures/d", Descriptor(4, 4, "textures/d-raw"), Payload(4, 4));

        harness.Store.Pump();

        // Four 64-byte pages with a 128-byte budget: exactly two uploads start.
        Assert.Equal(3, harness.Gpu.Uploads.Count);
        Assert.Equal(2, harness.Store.PendingUploadCount);
        Assert.Equal(128, harness.Store.PendingUploadBytes);
        Assert.Equal(3, harness.Store.TexturePageCount);
        Assert.Equal(TexturePageLookupKind.Ready, harness.Store.EnsurePage("textures/a").Kind);
        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage("textures/c").Kind);

        harness.Store.Pump();

        Assert.Equal(5, harness.Gpu.Uploads.Count);
        Assert.Equal(0, harness.Store.PendingUploadCount);
        Assert.Equal(0, harness.Store.PendingUploadBytes);
        Assert.Equal(5, harness.Store.TexturePageCount);
        Assert.All(
            new[] { "textures/a", "textures/b", "textures/c", "textures/d" },
            path => Assert.Equal(
                TexturePageLookupKind.Ready,
                harness.Store.EnsurePage(path).Kind));
    }

    [Fact]
    public void AnOversizedFirstUploadIsAllowedOnce()
    {
        Harness harness = CreateHarness(uploadBudget: 16);
        StagePage(harness, "textures/large", Descriptor(4, 4, "textures/large-raw"), Payload(4, 4));
        StagePage(harness, "textures/next", Descriptor(4, 4, "textures/next-raw"), Payload(4, 4));

        harness.Store.Pump();

        // The first queued page is 64 bytes against a 16-byte budget and still starts; the second
        // does not, because a later upload must fit.
        Assert.Equal(2, harness.Gpu.Uploads.Count);
        Assert.Equal(1, harness.Store.PendingUploadCount);
        Assert.Equal(64, harness.Store.PendingUploadBytes);

        harness.Store.Pump();

        Assert.Equal(3, harness.Gpu.Uploads.Count);
        Assert.Equal(0, harness.Store.PendingUploadCount);
        Assert.Equal(0, harness.Store.PendingUploadBytes);
    }

    [Fact]
    public void TheMostRecentlySeenQueuedPageStartsFirst()
    {
        Harness harness = CreateHarness(uploadBudget: 40);
        StagePage(harness, "textures/a", Descriptor(1, 1, "textures/a-raw"), Payload(1, 1));
        StagePage(harness, "textures/b", Descriptor(2, 2, "textures/b-raw"), Payload(2, 2));
        StagePage(harness, "textures/c", Descriptor(3, 3, "textures/c-raw"), Payload(3, 3));
        StagePage(harness, "textures/d", Descriptor(4, 4, "textures/d-raw"), Payload(4, 4));

        harness.Store.Pump();

        // 4 + 16 + 36 > 40, so only the first two start; c and d stay queued.
        Assert.Equal(3, harness.Gpu.Uploads.Count);
        Assert.Equal(2, harness.Store.PendingUploadCount);

        // The planner marks d as seen in this frame; c keeps its older marker.
        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage("textures/d").Kind);
        harness.Store.Pump();

        // d is now the first upload of the frame (allowed even though 64 exceeds 40); c waits.
        Assert.Equal(4, harness.Gpu.Uploads.Count);
        Assert.Equal(4, harness.Gpu.Uploads[^1].Width);
        Assert.Equal(1, harness.Store.PendingUploadCount);

        harness.Store.Pump();

        Assert.Equal(5, harness.Gpu.Uploads.Count);
        Assert.Equal(3, harness.Gpu.Uploads[^1].Width);
        Assert.Equal(0, harness.Store.PendingUploadCount);
    }

    [Fact]
    public void ACatalogChangeReloadsAPathAndTheOldPageIsNotReturned()
    {
        Harness harness = CreateHarness();
        LoadPage(
            harness,
            "textures/reloaded",
            Descriptor(2, 2, "textures/reloaded-v1-raw"),
            Payload(2, 2));
        Assert.Equal(1, harness.Store.EnsurePage("textures/reloaded").Index);

        harness.Assets.CatalogVersion++;
        harness.Assets.ResetDescriptor("textures/reloaded");

        // The first sighting after the mount retires the old page and starts a fresh load.
        Assert.Equal(
            TexturePageLookupKind.Pending,
            harness.Store.EnsurePage("textures/reloaded").Kind);
        Assert.Equal(2, harness.Assets.LoadAssetCount);

        harness.Assets.CompleteDescriptor(
            "textures/reloaded",
            Descriptor(3, 1, "textures/reloaded-v2-raw"));
        harness.Assets.CompletePayload("textures/reloaded-v2-raw", Payload(3, 1));
        harness.Store.Pump();

        TexturePageLookup reloaded = harness.Store.EnsurePage("textures/reloaded");
        Assert.Equal(TexturePageLookupKind.Ready, reloaded.Kind);
        Assert.Equal(2, reloaded.Index);
        Assert.Equal(3, reloaded.Width);
        Assert.Equal(1, reloaded.Height);
        Assert.Equal(2, harness.Store.TexturePageCount);
    }

    [Fact]
    public void ACatalogChangeRetriesAPathThatFailedBeforeTheMount()
    {
        Harness harness = CreateHarness();
        harness.Assets.DescriptorLoadException = new InvalidOperationException("missing");

        TexturePageLookup failed = harness.Store.EnsurePage("textures/retried");
        Assert.Equal(TexturePageLookupKind.Placeholder, failed.Kind);
        Assert.Equal(1, harness.Store.FailedPathCount);

        harness.Assets.DescriptorLoadException = null;
        harness.Assets.CatalogVersion++;
        harness.Assets.ResetDescriptor("textures/retried");

        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage("textures/retried").Kind);
        harness.Assets.CompleteDescriptor(
            "textures/retried",
            Descriptor(2, 2, "textures/retried-raw"));
        harness.Assets.CompletePayload("textures/retried-raw", Payload(2, 2));
        harness.Store.Pump();

        Assert.Equal(0, harness.Store.FailedPathCount);
        TexturePageLookup lookup = harness.Store.EnsurePage("textures/retried");
        Assert.Equal(TexturePageLookupKind.Ready, lookup.Kind);
        Assert.Equal(1, lookup.Index);
        Assert.Equal(2, harness.Store.TexturePageCount);
    }

    [Fact]
    public void ARetiredInFlightUploadFinishesWithoutBecomingReadyOrVisible()
    {
        Harness harness = CreateHarness();
        harness.Gpu.UploadsCompleteImmediately = false;

        harness.Store.EnsurePage("textures/retired");
        harness.Assets.CompleteDescriptor(
            "textures/retired",
            Descriptor(2, 2, "textures/retired-raw"));
        harness.Assets.CompletePayload("textures/retired-raw", Payload(2, 2));
        harness.Store.Pump();

        Assert.Equal(1, harness.Store.UploadingCount);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage("textures/retired").Kind);

        harness.Assets.CatalogVersion++;
        harness.Store.Pump();

        Assert.Equal(0, harness.Store.PendingUploadCount);
        Assert.Equal(1, harness.Store.UploadingCount);
        Assert.False(
            harness.Store.TryGetPageState(
                "textures/retired",
                out _,
                out _),
            "A retired uploading page must leave the page map.");

        harness.Gpu.SignalUpload(1);
        harness.Store.Pump();

        Assert.Equal(0, harness.Store.UploadingCount);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.False(harness.Store.TryGetPageState("textures/retired", out _, out _));

        // The retired upload still completed so its staging and command resources were released;
        // it just never became visible or counted as ready.
        Assert.Equal(2, harness.Gpu.ResidentPageCount);

        TexturePageLookup fresh = harness.Store.EnsurePage("textures/retired");
        Assert.Equal(TexturePageLookupKind.Pending, fresh.Kind);
        Assert.Equal(2, harness.Assets.LoadAssetCount);
    }

    [Fact]
    public void ACatalogChangeRetiresQueuedUploads()
    {
        Harness harness = CreateHarness(uploadBudget: 64);
        StagePage(harness, "textures/a", Descriptor(4, 4, "textures/a-raw"), Payload(4, 4));
        StagePage(harness, "textures/b", Descriptor(4, 4, "textures/b-raw"), Payload(4, 4));

        harness.Store.Pump();
        Assert.Equal(1, harness.Store.PendingUploadCount);
        Assert.Equal(64, harness.Store.PendingUploadBytes);

        harness.Assets.CatalogVersion++;
        harness.Store.Pump();

        Assert.Equal(0, harness.Store.PendingUploadCount);
        Assert.Equal(0, harness.Store.PendingUploadBytes);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage("textures/b").Kind);
    }

    [Fact]
    public void NonPositiveBudgetsAreRejectedByTheStore()
    {
        var assets = new FakeTextureAssets();
        var gpu = new FakeTexturePageGpu();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TexturePageStore(
                assets,
                gpu,
                DefaultLimit,
                DefaultMaxDimension,
                NullLogger.Instance,
                assets,
                uploadBudgetBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TexturePageStore(
                assets,
                gpu,
                DefaultLimit,
                DefaultMaxDimension,
                NullLogger.Instance,
                assets,
                residencyBudgetBytes: 0));
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

    [Fact]
    public void DestroyObservesAFaultedCompletedLoadThatPumpNeverAdvanced()
    {
        Harness harness = CreateHarness();
        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage("textures/faulted").Kind);

        // The load completes with a fault between frames; Pump never advances it, so the page and
        // its completed-faulted task are still pending when Destroy runs.
        harness.Assets.DescriptorSource("textures/faulted").SetException(
            new InvalidOperationException("load faulted"));
        Assert.Equal(1, harness.Store.PendingLoadCount);
        Assert.Equal(0, harness.Store.ObservedTeardownFaults);

        harness.Store.Destroy();

        Assert.Equal(1, harness.Store.ObservedTeardownFaults);
        Assert.Equal(0, harness.Store.PendingLoadCount);
    }

    [Fact]
    public void DestroyDoesNotCountSuccessfulOrInFlightLoadsAsTeardownFaults()
    {
        Harness harness = CreateHarness();
        harness.Store.EnsurePage("textures/in-flight");
        harness.Store.EnsurePage("textures/completed");
        harness.Assets.CompleteDescriptor(
            "textures/completed",
            Descriptor(2, 2, "textures/completed-raw"));
        Assert.Equal(2, harness.Store.PendingLoadCount);

        harness.Store.Destroy();

        // One load is still in flight (a continuation is attached) and one completed
        // successfully; neither is a teardown fault.
        Assert.Equal(0, harness.Store.ObservedTeardownFaults);
    }

    [Fact]
    public void ASmallResidencyBudgetEvictsTheLeastRecentlySeenReadyPageFirst()
    {
        const int pageBytes = 4 * 4 * 4;
        Harness harness = CreateHarness(residencyBudget: 3 * pageBytes);

        LoadPage(
            harness,
            "textures/first",
            Descriptor(4, 4, "textures/first-raw"),
            Payload(4, 4));
        LoadPage(
            harness,
            "textures/second",
            Descriptor(4, 4, "textures/second-raw"),
            Payload(4, 4));
        LoadPage(
            harness,
            "textures/third",
            Descriptor(4, 4, "textures/third-raw"),
            Payload(4, 4));
        LoadPage(
            harness,
            "textures/fourth",
            Descriptor(4, 4, "textures/fourth-raw"),
            Payload(4, 4));

        Assert.Equal(4 * pageBytes, harness.Store.ResidentPayloadBytes);
        Assert.Equal(0, harness.Store.EvictionCount);

        harness.Store.Pump();

        // The budget needs one page reclaimed; the least recently seen ready page goes first even
        // though three pages are old enough to be safe.
        Assert.Equal(1, harness.Store.EvictionCount);
        Assert.Equal(new[] { 1 }, harness.Gpu.RetiredPageIndices);
        Assert.Equal(3 * pageBytes, harness.Store.ResidentPayloadBytes);
        Assert.Equal(4, harness.Store.TexturePageCount);
        Assert.False(
            harness.Store.TryGetPageState("textures/first", out _, out _),
            "The evicted path must leave the page map.");
        Assert.Equal(
            TexturePageLookupKind.Ready,
            harness.Store.EnsurePage("textures/second").Kind);
        Assert.Equal(
            TexturePageLookupKind.Ready,
            harness.Store.EnsurePage("textures/third").Kind);
        Assert.Equal(
            TexturePageLookupKind.Ready,
            harness.Store.EnsurePage("textures/fourth").Kind);
    }

    [Fact]
    public void APageInTheReclaimSafetyAgeIsNeverEvictedAndIsEvictedOnceItAgesOut()
    {
        const int pageBytes = 4 * 4 * 4;
        Harness harness = CreateHarness(residencyBudget: pageBytes - 1);

        LoadPage(
            harness,
            "textures/visible",
            Descriptor(4, 4, "textures/visible-raw"),
            Payload(4, 4));

        // The single ready page is already over budget. While the planner keeps seeing it, it stays
        // inside the safety age and the over-budget set is kept for a later pump.
        PumpKeepingVisible(harness, "textures/visible");
        PumpKeepingVisible(harness, "textures/visible");
        PumpKeepingVisible(harness, "textures/visible");

        Assert.Equal(pageBytes, harness.Store.ResidentPayloadBytes);
        Assert.Equal(0, harness.Store.EvictionCount);
        Assert.Equal(2, harness.Store.TexturePageCount);

        // Age == the safety age is still safe; the pump after it no longer is.
        harness.Store.Pump();
        Assert.Equal(0, harness.Store.EvictionCount);
        harness.Store.Pump();

        Assert.Equal(1, harness.Store.EvictionCount);
        Assert.Equal(new[] { 1 }, harness.Gpu.RetiredPageIndices);
        Assert.Equal(0, harness.Store.ResidentPayloadBytes);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.True(harness.Store.IsPlaceholderReady);
        Assert.Equal(1, harness.Gpu.ResidentPageCount);
        Assert.DoesNotContain(TexturePageCapacity.PlaceholderIndex, harness.Gpu.RetiredPageIndices);
    }

    [Fact]
    public void AnEvictedPathReloadsOnItsNextEnsurePageAndReusesItsSlot()
    {
        const int pageBytes = 4 * 4 * 4;
        Harness harness = CreateHarness(residencyBudget: 3 * pageBytes);

        LoadPage(
            harness,
            "textures/first",
            Descriptor(4, 4, "textures/first-raw"),
            Payload(4, 4));
        LoadPage(
            harness,
            "textures/second",
            Descriptor(4, 4, "textures/second-raw"),
            Payload(4, 4));
        LoadPage(
            harness,
            "textures/third",
            Descriptor(4, 4, "textures/third-raw"),
            Payload(4, 4));
        LoadPage(
            harness,
            "textures/fourth",
            Descriptor(4, 4, "textures/fourth-raw"),
            Payload(4, 4));
        harness.Store.Pump();

        Assert.Equal(1, harness.Store.EvictionCount);
        Assert.Equal(1, harness.Store.FreeDescriptorSlotCount);
        int loads = harness.Assets.LoadAssetCount;

        // The evicted path starts a fresh descriptor/payload load on its next sighting.
        Assert.Equal(
            TexturePageLookupKind.Pending,
            harness.Store.EnsurePage("textures/first").Kind);
        Assert.Equal(loads + 1, harness.Assets.LoadAssetCount);

        harness.Store.Pump();

        TexturePageLookup reloaded = harness.Store.EnsurePage("textures/first");
        Assert.Equal(TexturePageLookupKind.Ready, reloaded.Kind);
        Assert.Equal(1, reloaded.Index);
        Assert.Equal(1, harness.Store.DescriptorSlotReuseCount);
        Assert.Equal(0, harness.Store.FreeDescriptorSlotCount);
        Assert.Equal(4 * pageBytes, harness.Store.ResidentPayloadBytes);
    }

    [Fact]
    public void AFreedDescriptorSlotIsReusedBeforeTheTableGrows()
    {
        // Each 1x1 page is 4 payload bytes; the budget fits 254 pages, so the 255th page leaves the
        // 256-entry table exactly full and one byte over the budget.
        const int pageBytes = 4;
        Harness harness = CreateHarness(residencyBudget: 254 * pageBytes);

        for (var index = 0; index < TexturePageCapacity.InitialRealPages; index++)
        {
            LoadPage(
                harness,
                $"textures/page-{index}",
                Descriptor(1, 1, $"textures/page-{index}-raw"),
                Payload(1, 1));
        }

        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCount);
        Assert.Equal(0, harness.Store.GrowthCount);

        harness.Store.Pump();

        Assert.Equal(1, harness.Store.EvictionCount);
        Assert.Equal(1, harness.Store.FreeDescriptorSlotCount);
        Assert.Equal(254 * pageBytes, harness.Store.ResidentPayloadBytes);

        LoadPage(
            harness,
            "textures/page-after-eviction",
            Descriptor(1, 1, "textures/page-after-eviction-raw"),
            Payload(1, 1));

        Assert.Equal(0, harness.Store.GrowthCount);
        Assert.Equal(TexturePageCapacity.Initial, harness.Store.TexturePageCapacityInEntries);
        Assert.Equal(1, harness.Store.DescriptorSlotReuseCount);
        Assert.Equal(
            1,
            harness.Store.EnsurePage("textures/page-after-eviction").Index);
    }

    [Fact]
    public void ACatalogChangeReclaimsRetiredPagesAfterTheSafetyAge()
    {
        const int pageBytes = 4 * 4 * 4;
        Harness harness = CreateHarness();

        LoadPage(
            harness,
            "textures/a",
            Descriptor(4, 4, "textures/a-raw"),
            Payload(4, 4));
        LoadPage(
            harness,
            "textures/b",
            Descriptor(4, 4, "textures/b-raw"),
            Payload(4, 4));
        Assert.Equal(2 * pageBytes, harness.Store.ResidentPayloadBytes);

        harness.Assets.CatalogVersion++;
        harness.Store.Pump();

        // Both ready pages become retired: they stay GPU-resident but stop counting as resident.
        Assert.Equal(0, harness.Store.ResidentPayloadBytes);
        Assert.Equal(2, harness.Store.RetiredPageCount);
        Assert.Equal(0, harness.Store.ReclaimedRetiredPageCount);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.Equal(3, harness.Gpu.ResidentPageCount);

        harness.Store.Pump();
        harness.Store.Pump();

        Assert.Equal(2, harness.Store.RetiredPageCount);
        Assert.Equal(0, harness.Store.ReclaimedRetiredPageCount);
        Assert.Equal(3, harness.Gpu.ResidentPageCount);

        harness.Store.Pump();

        // Past the safety age the descriptor slots and image memory return: the GPU holds only the
        // placeholder and both indices wait for reuse.
        Assert.Equal(0, harness.Store.RetiredPageCount);
        Assert.Equal(2, harness.Store.ReclaimedRetiredPageCount);
        Assert.Equal(1, harness.Gpu.ResidentPageCount);
        Assert.Equal(2, harness.Store.FreeDescriptorSlotCount);
        Assert.Equal(0, harness.Store.ResidentPayloadBytes);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.Equal(0, harness.Store.EvictionCount);

        // A fresh sighting of a retired path reloads it into a reclaimed slot.
        harness.Store.EnsurePage("textures/a");
        harness.Store.Pump();

        TexturePageLookup reloaded = harness.Store.EnsurePage("textures/a");
        Assert.Equal(TexturePageLookupKind.Ready, reloaded.Kind);
        Assert.InRange(reloaded.Index, 1, 2);
        Assert.Equal(pageBytes, harness.Store.ResidentPayloadBytes);
        Assert.True(harness.Store.DescriptorSlotReuseCount >= 1);
    }

    [Fact]
    public void ResidentBytesPagesAndEvictionsStayConsistentWhileEvicting()
    {
        const int pageBytes = 4 * 4 * 4;
        Harness harness = CreateHarness(residencyBudget: 2 * pageBytes);

        for (var index = 0; index < 8; index++)
        {
            LoadPage(
                harness,
                $"textures/page-{index}",
                Descriptor(4, 4, $"textures/page-{index}-raw"),
                Payload(4, 4));
        }

        for (var pump = 0; pump < 4; pump++)
        {
            harness.Store.Pump();
        }

        var ready = 0;
        for (var index = 0; index < 8; index++)
        {
            if (harness.Store.TryGetPageState(
                $"textures/page-{index}",
                out TexturePageState state,
                out _))
            {
                Assert.Equal(TexturePageState.Ready, state);
                ready++;
            }
        }

        Assert.True(harness.Store.EvictionCount > 0, "No page was evicted under the small budget.");
        Assert.Equal(ready + 1, harness.Store.TexturePageCount);
        Assert.Equal((long)ready * pageBytes, harness.Store.ResidentPayloadBytes);
        Assert.Equal(harness.Store.EvictionCount, harness.Gpu.RetiredPageIndices.Count);
        Assert.Equal(
            harness.Store.EvictionCount - harness.Store.DescriptorSlotReuseCount,
            harness.Store.FreeDescriptorSlotCount);
        Assert.Equal(0, harness.Store.RetiredPageCount);
        Assert.True(
            harness.Store.ResidentPayloadBytes <= 2 * pageBytes,
            $"Resident bytes {harness.Store.ResidentPayloadBytes} exceed the budget.");
        Assert.All(
            harness.Gpu.RetiredPageIndices,
            index => Assert.InRange(index, 1, TexturePageCapacity.InitialRealPages));
    }

    [Fact]
    public void ThePlaceholderIsNeverEvicted()
    {
        Harness harness = CreateHarness(residencyBudget: 1);

        LoadPage(
            harness,
            "textures/large",
            Descriptor(4, 4, "textures/large-raw"),
            Payload(4, 4));
        Assert.Equal(4 * 4 * 4, harness.Store.ResidentPayloadBytes);

        for (var pump = 0; pump < 4; pump++)
        {
            harness.Store.Pump();
        }

        Assert.Equal(1, harness.Store.EvictionCount);
        Assert.Equal(0, harness.Store.ResidentPayloadBytes);
        Assert.Equal(1, harness.Store.TexturePageCount);
        Assert.True(harness.Store.IsPlaceholderReady);
        Assert.Equal(1, harness.Gpu.ResidentPageCount);
        Assert.DoesNotContain(TexturePageCapacity.PlaceholderIndex, harness.Gpu.RetiredPageIndices);
    }

    private static Harness CreateHarness(
        int limit = DefaultLimit,
        int maxDimension = DefaultMaxDimension,
        int uploadBudget = TexturePageStore.DefaultUploadBudgetBytes,
        long residencyBudget = TexturePageStore.DefaultResidencyBudgetBytes,
        int initialCapacity = TexturePageCapacity.Initial)
    {
        var assets = new FakeTextureAssets();
        var gpu = new FakeTexturePageGpu();
        var store = new TexturePageStore(
            assets,
            gpu,
            limit,
            maxDimension,
            NullLogger.Instance,
            assets,
            uploadBudget,
            residencyBudget,
            initialCapacity);
        store.Start();
        return new Harness(store, assets, gpu);
    }

    private static void StagePage(
        Harness harness,
        string path,
        TextureDescriptor descriptor,
        byte[] payload)
    {
        Assert.Equal(TexturePageLookupKind.Pending, harness.Store.EnsurePage(path).Kind);
        harness.Assets.CompleteDescriptor(path, descriptor);
        harness.Assets.CompletePayload(descriptor.Payload.Path!, payload);
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

    /// <summary>Marks a ready page as seen for the frame, then pumps (keeps it inside the safety age).</summary>
    private static void PumpKeepingVisible(Harness harness, string path)
    {
        Assert.Equal(TexturePageLookupKind.Ready, harness.Store.EnsurePage(path).Kind);
        harness.Store.Pump();
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
