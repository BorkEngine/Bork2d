using System.Buffers.Binary;
using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Shaders;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using PolyType;
using Silk.NET.Vulkan;
using Xunit;
using ControllableShaderAssets = Bork2d.Tests.TestDoubles.FakeShaderAssets;

namespace Bork2d.Tests.Render;

/// <summary>
/// GPU-free tests for the shader catalog: default-shader startup validation, asynchronous custom
/// loading, metadata and SPIR-V validation, fallback and one-time diagnostics, the engine-wide
/// global-name registry, content-hash sharing, catalog-remount reload/retry, and safety-age
/// retirement.
/// </summary>
public sealed class ShaderStoreTests
{
    private const uint SpirVMagic = 0x07230203;

    [Fact]
    public void ValidBuiltInShaderAndModuleLoadSuccessfully()
    {
        var assets = new FakeShaderAssets(CreateShader(), CreateSpirV());

        ShaderRenderData shader = ShaderStore.LoadDefaultShader(assets);
        byte[] spirV = ShaderStore.LoadSpirV(
            assets,
            ShaderStore.DefaultSpirVPath,
            "the built-in sprite fragment module");

        Assert.Equal(ShaderLayout.CurrentVersion, shader.LayoutVersion);
        Assert.Equal("main", shader.EntryPoint);
        Assert.Equal(ShaderStore.DefaultSpirVPath, shader.SpirV.Path);
        Assert.Equal(SpirVMagic, BinaryPrimitives.ReadUInt32LittleEndian(spirV));
    }

    [Fact]
    public void MissingShaderAssetIsAFatalStartupFailureNamingThePath()
    {
        var assets = new FakeShaderAssets(shader: null, spirV: CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullSpirVReferenceIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(
            CreateShader(spirV: DataRef.None),
            CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("SPIR-V", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedLayoutVersionIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(
            CreateShader(layoutVersion: ShaderLayout.CurrentVersion + 1),
            CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("layout version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutVersionTwoIsRejectedAndVersionThreeIsAccepted()
    {
        Assert.Equal(3u, ShaderLayout.CurrentVersion);

        ShaderRenderData legacy = ShaderRenderData.Freeze(
            CreateCustomShader("materials/legacy-spv", layoutVersion: 2));

        Assert.False(ShaderValidation.TryValidateMetadata(legacy, out string rejected));
        Assert.Contains("layout version 2", rejected, StringComparison.Ordinal);

        ShaderRenderData current = ShaderRenderData.Freeze(
            CreateCustomShader("materials/current-spv", layoutVersion: 3));

        Assert.True(ShaderValidation.TryValidateMetadata(current, out string accepted));
        Assert.Empty(accepted);
    }

    [Fact]
    public void NonMainEntryPointIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(
            CreateShader(entryPoint: "fragment"),
            CreateSpirV());

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadDefaultShader(assets));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("entry point", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingModuleIsAFatalStartupFailureNamingThePath()
    {
        var assets = new FakeShaderAssets(CreateShader(), spirV: null);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadSpirV(
                assets,
                ShaderStore.DefaultSpirVPath,
                "the built-in sprite fragment module"));

        Assert.Contains(ShaderStore.DefaultSpirVPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModuleWithoutTheSpirVMagicIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(CreateShader(), [1, 2, 3, 4]);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadSpirV(
                assets,
                ShaderStore.DefaultSpirVPath,
                "the built-in sprite fragment module"));

        Assert.Contains(ShaderStore.DefaultSpirVPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("SPIR-V", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedModuleIsAFatalStartupFailure()
    {
        var assets = new FakeShaderAssets(CreateShader(), [0x03, 0x02, 0x23]);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.LoadSpirV(
                assets,
                ShaderStore.DefaultSpirVPath,
                "the built-in sprite fragment module"));

        Assert.Contains(ShaderStore.DefaultSpirVPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("shorter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomShaderLoadsAsynchronouslyAndBecomesReadyOnALaterPump()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(CreateCustomShader("materials/pending"));

        ShaderBinding pending = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Pending, pending.State);
        Assert.Equal(1, fixture.Assets.LoadedDataPaths.Count(path => path == "materials/pending"));
        Assert.Equal(1, fixture.Factory.CreateModuleCount);

        // Resolving again while the load is outstanding starts no second load.
        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(1, fixture.Assets.LoadedDataPaths.Count(path => path == "materials/pending"));

        fixture.Assets.CompleteSpirV("materials/pending");
        fixture.Store.Pump();
        ShaderBinding ready = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);

        Assert.Equal(ShaderBindingState.Ready, ready.State);
        Assert.NotEqual(ShaderStore.DefaultEntryId, ready.EntryId);
        Assert.Equal(ShaderStore.DefaultEntryId + 1, ready.EntryId);
        Assert.Equal(2, fixture.Factory.CreateModuleCount);
        Assert.Single(fixture.Factory.CreatedPipelines);
        Assert.Equal(Format.R8G8B8A8Srgb, fixture.Factory.CreatedPipelines[0].Format);
    }

    [Fact]
    public void InvalidMetadataFallsBackWithOneWarningPerPathAndNeverStartsALoad()
    {
        AssertInvalid(CreateCustomShader("materials/layout", layoutVersion: 1), "layout version");
        AssertInvalid(CreateCustomShader("materials/entry", entryPoint: "fragment"), "entry point");
        AssertInvalid(
            CreateCustomShader(
                "materials/slots",
                slots:
                [
                    new ShaderTextureSlot("a"),
                    new ShaderTextureSlot("b"),
                    new ShaderTextureSlot("c"),
                    new ShaderTextureSlot("d"),
                    new ShaderTextureSlot("e"),
                ]),
            "texture slots");
        AssertInvalid(
            CreateCustomShader(
                "materials/slot-name",
                slots: [new ShaderTextureSlot("not a name")]),
            "GLSL");
        AssertInvalid(
            CreateCustomShader(
                "materials/instance-offset",
                instance:
                [
                    new ShaderUniform(
                        "u",
                        ShaderUniformType.Vector4,
                        2,
                        Vector4.Zero),
                ]),
            "aligned");
        AssertInvalid(
            CreateCustomShader(
                "materials/instance-overlap",
                instance:
                [
                    new ShaderUniform("a", ShaderUniformType.Vector4, 0, Vector4.Zero),
                    new ShaderUniform("b", ShaderUniformType.Float, 1, Vector4.Zero),
                ]),
            "overlaps");
        AssertInvalid(
            CreateCustomShader(
                "materials/duplicate",
                instance:
                [
                    new ShaderUniform("a", ShaderUniformType.Float, 0, Vector4.Zero),
                    new ShaderUniform("a", ShaderUniformType.Float, 4, Vector4.Zero),
                ]),
            "more than once");
        AssertInvalid(
            CreateCustomShader(
                "materials/cross-duplicate",
                instance:
                [
                    new ShaderUniform("a", ShaderUniformType.Float, 0, Vector4.Zero),
                ],
                globals:
                [
                    new ShaderUniform("a", ShaderUniformType.Float, 0, Vector4.Zero),
                ]),
            "more than once");
        AssertInvalid(
            CreateCustomShader(
                "materials/reserved",
                instance:
                [
                    new ShaderUniform("BORK2D_bad", ShaderUniformType.Float, 0, Vector4.Zero),
                ]),
            "reserved");
        AssertInvalid(
            CreateCustomShader(
                "materials/global-offset",
                globals:
                [
                    new ShaderUniform("g", ShaderUniformType.Vector4, 2, Vector4.Zero),
                ]),
            "aligned");
        AssertInvalid(
            CreateCustomShader(
                "materials/global-overlap",
                globals:
                [
                    new ShaderUniform("g0", ShaderUniformType.Float, 0, Vector4.Zero),
                    new ShaderUniform("g1", ShaderUniformType.Vector2, 0, Vector4.Zero),
                ]),
            "overlaps");
    }

    [Fact]
    public void InvalidSpirVPayloadFallsBackWithOneWarning()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(CreateCustomShader("materials/bad-spv"));

        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/bad-spv", [1, 2, 3, 4]);
        fixture.Store.Pump();

        ShaderBinding failed = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Failed, failed.State);
        Assert.Equal(ShaderStore.DefaultEntryId, failed.EntryId);
        Assert.Equal(1, fixture.Factory.CreateModuleCount);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("materials/bad-spv", StringComparison.Ordinal)));

        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("materials/bad-spv", StringComparison.Ordinal)));
    }

    [Fact]
    public void FaultedLoadFallsBackWithOneWarning()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(CreateCustomShader("materials/faulted"));

        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        fixture.Assets.FailSpirV("materials/faulted", new IOException("loader failed"));
        fixture.Store.Pump();

        ShaderBinding failed = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Failed, failed.State);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("loader failed", StringComparison.Ordinal));
        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("materials/faulted", StringComparison.Ordinal)));
    }

    [Fact]
    public void SynchronousDataLoadFailureFallsBackWithOneWarning()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(CreateCustomShader("materials/throw"));
        fixture.Assets.DataLoadException = new IOException("synchronous load failed");

        ShaderBinding first = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        ShaderBinding second = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);

        Assert.Equal(ShaderBindingState.Failed, first.State);
        Assert.Equal(ShaderBindingState.Failed, second.State);
        Assert.Contains("materials/throw", fixture.Assets.LoadedDataPaths);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("synchronous load failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void DefaultReloadSynchronousAssetLoadFailureKeepsThePreviousEntry()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData initial = fixture.Store.DefaultEntry.Metadata;
        fixture.Assets.AssetLoadException = new IOException("synchronous asset load failed");
        fixture.Assets.CatalogVersion++;

        fixture.Store.Pump();

        Assert.Same(initial, fixture.Store.DefaultEntry.Metadata);
        Assert.Contains(ShaderStore.DefaultShaderPath, fixture.Assets.LoadedAssetPaths);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("synchronous asset load failed", StringComparison.Ordinal));
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(null, Format.R8G8B8A8Srgb).State);
    }

    [Fact]
    public void MissingSpirVReferenceFallsBackWithOneRendererWarning()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(new ShaderAsset
        {
            LayoutVersion = ShaderLayout.CurrentVersion,
            EntryPoint = "main",
        });

        ShaderBinding first = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        ShaderBinding second = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);

        Assert.Equal(ShaderBindingState.Failed, first.State);
        Assert.Equal(ShaderBindingState.Failed, second.State);
        Assert.Single(fixture.Assets.LoadedDataPaths);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("no SPIR-V", StringComparison.Ordinal)));
    }

    [Fact]
    public void ModuleCreationFailureFallsBackWithOneWarning()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(CreateCustomShader("materials/module"));
        fixture.Factory.ModuleFailure = new Bork2dException("module creation failed");

        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/module");
        fixture.Store.Pump();

        ShaderBinding failed = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Failed, failed.State);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("module creation failed", StringComparison.Ordinal));
        Assert.Empty(fixture.Factory.CreatedPipelines);
    }

    [Fact]
    public void PipelineCreationFailureFallsBackWithOneWarning()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(CreateCustomShader("materials/pipeline"));

        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/pipeline");
        fixture.Store.Pump();
        fixture.Factory.PipelineFailure = new Bork2dException("pipeline creation failed");

        ShaderBinding failed = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Failed, failed.State);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("pipeline creation failed", StringComparison.Ordinal));

        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains("materials/pipeline", StringComparison.Ordinal)));
    }

    [Fact]
    public void GlobalNameTypeConflictRejectsTheLaterShader()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData first = fixture.Freeze(
            CreateCustomShader(
                "materials/global-first",
                globals:
                [
                    new ShaderUniform("gValue", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));
        ShaderRenderData conflicting = fixture.Freeze(
            CreateCustomShader(
                "materials/global-conflict",
                globals:
                [
                    new ShaderUniform("gValue", ShaderUniformType.Vector2, 0, Vector4.Zero),
                ]));
        ShaderRenderData compatible = fixture.Freeze(
            CreateCustomShader(
                "materials/global-compatible",
                globals:
                [
                    new ShaderUniform("gValue", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));

        _ = fixture.Store.Resolve(first, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/global-first");
        fixture.Store.Pump();
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(first, Format.R8G8B8A8Srgb).State);

        _ = fixture.Store.Resolve(conflicting, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/global-conflict");
        fixture.Store.Pump();
        ShaderBinding rejected = fixture.Store.Resolve(conflicting, Format.R8G8B8A8Srgb);

        Assert.Equal(ShaderBindingState.Failed, rejected.State);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("already registered", StringComparison.Ordinal));

        _ = fixture.Store.Resolve(compatible, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/global-compatible");
        fixture.Store.Pump();
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(compatible, Format.R8G8B8A8Srgb).State);
    }

    [Fact]
    public void SharedGlobalNameKeepsItsClaimWhenAnotherOwnerRetires()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData first = fixture.Freeze(
            CreateCustomShader(
                "materials/owner-first",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));
        ShaderRenderData second = fixture.Freeze(
            CreateCustomShader(
                "materials/owner-second",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));
        ShaderRenderData conflicting = fixture.Freeze(
            CreateCustomShader(
                "materials/owner-conflict",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Vector2, 0, Vector4.Zero),
                ]));

        Assert.Equal(ShaderBindingState.Ready, MakeReady(fixture, first).State);
        Assert.Equal(ShaderBindingState.Ready, MakeReady(fixture, second).State);

        // Retire the last owner through a pipeline failure for another output format. The name is
        // shared, so the earlier owner must keep guarding it.
        fixture.Factory.PipelineFailure = new Bork2dException("pipeline failed");
        Assert.Equal(
            ShaderBindingState.Failed,
            fixture.Store.Resolve(second, Format.B8G8R8A8Srgb).State);
        fixture.Factory.PipelineFailure = null;

        _ = fixture.Store.Resolve(conflicting, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/owner-conflict");
        fixture.Store.Pump();

        Assert.Equal(
            ShaderBindingState.Failed,
            fixture.Store.Resolve(conflicting, Format.R8G8B8A8Srgb).State);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("already registered", StringComparison.Ordinal));
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(first, Format.R8G8B8A8Srgb).State);
    }

    [Fact]
    public void GlobalNameIsReleasedWhenTheLastOwnerRetires()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData first = fixture.Freeze(
            CreateCustomShader(
                "materials/last-first",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));
        ShaderRenderData second = fixture.Freeze(
            CreateCustomShader(
                "materials/last-second",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));
        ShaderRenderData differentType = fixture.Freeze(
            CreateCustomShader(
                "materials/last-different",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Vector2, 0, Vector4.Zero),
                ]));

        Assert.Equal(ShaderBindingState.Ready, MakeReady(fixture, first).State);
        Assert.Equal(ShaderBindingState.Ready, MakeReady(fixture, second).State);

        fixture.Factory.PipelineFailure = new Bork2dException("pipeline failed");
        _ = fixture.Store.Resolve(first, Format.B8G8R8A8Srgb);
        _ = fixture.Store.Resolve(second, Format.B8G8R8A8Srgb);
        fixture.Factory.PipelineFailure = null;

        // Both owners retired, so the name is free again for a different type.
        Assert.Equal(
            ShaderBindingState.Ready,
            MakeReady(fixture, differentType).State);
    }

    [Fact]
    public void DefaultReloadKeepsACustomGlobalNameClaim()
    {
        const string replacementPath = "shader/sprite-spv-v2";
        using var fixture = new StoreFixture(
            CreateCustomShader(
                ShaderStore.DefaultSpirVPath,
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));
        ShaderRenderData custom = fixture.Freeze(
            CreateCustomShader(
                "materials/custom-claim",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Float, 0, Vector4.Zero),
                ]));
        ShaderRenderData conflicting = fixture.Freeze(
            CreateCustomShader(
                "materials/custom-claim-conflict",
                globals:
                [
                    new ShaderUniform("gShared", ShaderUniformType.Vector2, 0, Vector4.Zero),
                ]));

        // Mount a replacement default that no longer declares the shared name, but keep its SPIR-V
        // pending so the custom shader loads and claims the name before the default swap.
        fixture.Assets.DefaultAsset = CreateCustomShader(replacementPath);
        fixture.Assets.CatalogVersion++;
        fixture.Store.Pump();
        Assert.Equal(ShaderBindingState.Ready, MakeReady(fixture, custom).State);

        fixture.Assets.CompleteSpirV(replacementPath, CreateSpirV(seed: 12));
        fixture.Store.Pump();
        Assert.Empty(fixture.Store.DefaultEntry.Metadata.GlobalUniforms);
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(custom, Format.R8G8B8A8Srgb).State);

        _ = fixture.Store.Resolve(conflicting, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/custom-claim-conflict");
        fixture.Store.Pump();

        Assert.Equal(
            ShaderBindingState.Failed,
            fixture.Store.Resolve(conflicting, Format.R8G8B8A8Srgb).State);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("already registered", StringComparison.Ordinal));
    }

    [Fact]
    public void IdenticalContentSharesOneModuleAndOnePipelinePerFormat()
    {
        using var fixture = new StoreFixture();
        byte[] content = CreateSpirV(seed: 9);
        ShaderRenderData first = fixture.Freeze(CreateCustomShader("materials/same-a"));
        ShaderRenderData second = fixture.Freeze(CreateCustomShader("materials/same-b"));

        _ = fixture.Store.Resolve(first, Format.R8G8B8A8Srgb);
        _ = fixture.Store.Resolve(second, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/same-a", content);
        fixture.Assets.CompleteSpirV("materials/same-b", content);
        fixture.Store.Pump();

        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(first, Format.R8G8B8A8Srgb).State);
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(second, Format.R8G8B8A8Srgb).State);
        Assert.Equal(2, fixture.Factory.CreateModuleCount);
        Assert.Single(fixture.Factory.CreatedPipelines);

        _ = fixture.Store.Resolve(first, Format.B8G8R8A8Srgb);
        Assert.Equal(2, fixture.Factory.CreatedPipelines.Count);
        Assert.Contains(fixture.Factory.CreatedPipelines, key => key.Format == Format.B8G8R8A8Srgb);
    }

    [Fact]
    public void CatalogRemountReloadsReadyShadersAndRetriesFailedPaths()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData ready = fixture.Freeze(CreateCustomShader("materials/reload"));
        ShaderRenderData failed = fixture.Freeze(CreateCustomShader("materials/retry"));

        _ = fixture.Store.Resolve(ready, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/reload", CreateSpirV(seed: 1));
        fixture.Store.Pump();
        ShaderBinding beforeReload = fixture.Store.Resolve(ready, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Ready, beforeReload.State);

        _ = fixture.Store.Resolve(failed, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/retry", [9, 9, 9, 9]);
        fixture.Store.Pump();
        Assert.Equal(
            ShaderBindingState.Failed,
            fixture.Store.Resolve(failed, Format.R8G8B8A8Srgb).State);

        // A later mount retires both entries: the ready path reloads from the new winner and the
        // failed path is retried.
        fixture.Assets.CatalogVersion++;
        fixture.Assets.ResetSpirV("materials/reload");
        fixture.Assets.ResetSpirV("materials/retry");
        fixture.Store.Pump();

        ShaderBinding reloading = fixture.Store.Resolve(ready, Format.R8G8B8A8Srgb);
        ShaderBinding retrying = fixture.Store.Resolve(failed, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Pending, reloading.State);
        Assert.Equal(ShaderBindingState.Pending, retrying.State);
        Assert.Equal(2, fixture.Assets.LoadedDataPaths.Count(path => path == "materials/reload"));
        Assert.Equal(2, fixture.Assets.LoadedDataPaths.Count(path => path == "materials/retry"));

        fixture.Assets.CompleteSpirV("materials/reload", CreateSpirV(seed: 2));
        fixture.Assets.CompleteSpirV("materials/retry", CreateSpirV(seed: 3));
        fixture.Store.Pump();

        ShaderBinding reloaded = fixture.Store.Resolve(ready, Format.R8G8B8A8Srgb);
        ShaderBinding retried = fixture.Store.Resolve(failed, Format.R8G8B8A8Srgb);
        Assert.Equal(ShaderBindingState.Ready, reloaded.State);
        Assert.Equal(ShaderBindingState.Ready, retried.State);
        Assert.NotEqual(beforeReload.EntryId, reloaded.EntryId);
    }

    [Fact]
    public void RetiredCustomModulesAndPipelinesDestroyAfterTheSafetyAge()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(CreateCustomShader("materials/retired"));

        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV("materials/retired", CreateSpirV(seed: 4));
        fixture.Store.Pump();
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb).State);
        Assert.Equal(2, fixture.Factory.CreateModuleCount);
        Assert.Single(fixture.Factory.CreatedPipelines);

        fixture.Assets.CatalogVersion++;
        fixture.Store.Pump();

        // Two frames in flight: the retired module and its pipeline survive the first two pumps.
        fixture.Store.Pump();
        Assert.Equal(0, fixture.Factory.DestroyModuleCount);
        fixture.Store.Pump();
        Assert.Equal(1, fixture.Factory.DestroyModuleCount);
        Assert.Single(fixture.Factory.DestroyedPipelines);
    }

    [Fact]
    public void DefaultShaderReloadKeepsThePreviousEntryUntilTheReplacementIsReady()
    {
        using var fixture = new StoreFixture();
        const string replacementPath = "shader/sprite-spv-v2";
        ShaderRenderData initial = fixture.Store.DefaultEntry.Metadata;
        Assert.Empty(initial.InstanceUniforms);

        fixture.Assets.DefaultAsset = CreateCustomShader(
            replacementPath,
            instance:
            [
                new ShaderUniform(
                    "uPulse",
                    ShaderUniformType.Float,
                    0,
                    new Vector4(1f, 0f, 0f, 0f)),
            ]);
        fixture.Assets.CatalogVersion++;
        fixture.Store.Pump();

        Assert.Same(initial, fixture.Store.DefaultEntry.Metadata);
        Assert.Contains(replacementPath, fixture.Assets.LoadedDataPaths);
        Assert.Contains(ShaderStore.DefaultShaderPath, fixture.Assets.LoadedAssetPaths);
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(null, Format.R8G8B8A8Srgb).State);

        fixture.Assets.CompleteSpirV(replacementPath, CreateSpirV(seed: 5));
        fixture.Store.Pump();

        Assert.Single(fixture.Store.DefaultEntry.Metadata.InstanceUniforms);
        Assert.Equal("uPulse", fixture.Store.DefaultEntry.Metadata.InstanceUniforms[0].Name);
        Assert.Equal(2, fixture.Factory.CreateModuleCount);

        // The previous default module retires only after every frame that could use it completes.
        fixture.Store.Pump();
        Assert.Equal(0, fixture.Factory.DestroyModuleCount);
        fixture.Store.Pump();
        Assert.Equal(1, fixture.Factory.DestroyModuleCount);
    }

    [Fact]
    public void DefaultShaderReloadFailureKeepsThePreviousEntry()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData initial = fixture.Store.DefaultEntry.Metadata;
        const string replacementPath = "shader/sprite-spv-v2";
        fixture.Assets.DefaultAsset = CreateCustomShader(replacementPath);
        fixture.Assets.CatalogVersion++;
        fixture.Store.Pump();
        fixture.Assets.CompleteSpirV(replacementPath, [7, 7, 7, 7]);
        fixture.Store.Pump();

        Assert.Same(initial, fixture.Store.DefaultEntry.Metadata);
        Assert.Equal(
            ShaderBindingState.Ready,
            fixture.Store.Resolve(null, Format.R8G8B8A8Srgb).State);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("previous shader remains active", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidDefaultReplacementMetadataKeepsThePreviousEntry()
    {
        using var fixture = new StoreFixture();
        ShaderRenderData initial = fixture.Store.DefaultEntry.Metadata;
        const string replacementPath = "shader/sprite-spv-v2";
        fixture.Assets.DefaultAsset = CreateCustomShader(
            replacementPath,
            layoutVersion: ShaderLayout.CurrentVersion - 1);
        fixture.Assets.CatalogVersion++;
        fixture.Store.Pump();
        fixture.Assets.CompleteSpirV(replacementPath, CreateSpirV(seed: 6));
        fixture.Store.Pump();

        Assert.Same(initial, fixture.Store.DefaultEntry.Metadata);
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Message.Contains("layout version", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidBuiltInDeclarationSetIsAFatalStartupFailure()
    {
        var assets = new ControllableShaderAssets
        {
            DefaultAsset = CreateCustomShader(
                "shader/sprite-spv",
                instance:
                [
                    new ShaderUniform("u", ShaderUniformType.Vector4, 2, Vector4.Zero),
                ]),
        };
        assets.CompleteSpirV("shader/sprite-spv");
        var factory = new FakeShaderModuleFactory();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => ShaderStore.Create(
                assets,
                factory,
                assets,
                loggerFactory.CreateLogger("Bork2d.Render")));

        Assert.Contains(ShaderStore.DefaultShaderPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("aligned", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertInvalid(ShaderAsset asset, string fragment)
    {
        using var fixture = new StoreFixture();
        ShaderRenderData metadata = fixture.Freeze(asset);

        ShaderBinding first = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        ShaderBinding second = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);

        Assert.Equal(ShaderBindingState.Failed, first.State);
        Assert.Equal(ShaderBindingState.Failed, second.State);
        Assert.DoesNotContain(asset.SpirV.Path ?? string.Empty, fixture.Assets.LoadedDataPaths);
        Assert.Equal(1, fixture.Factory.CreateModuleCount);
        Assert.Equal(
            1,
            fixture.Logs.Entries.Count(
                entry => entry.Message.Contains(fragment, StringComparison.Ordinal)));
    }

    private static ShaderBinding MakeReady(StoreFixture fixture, ShaderRenderData metadata)
    {
        _ = fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
        fixture.Assets.CompleteSpirV(metadata.SpirV.Path!);
        fixture.Store.Pump();
        return fixture.Store.Resolve(metadata, Format.R8G8B8A8Srgb);
    }

    private static ILoggerFactory CreateLoggerFactory(out CapturingLoggerProvider provider)
    {
        var created = new CapturingLoggerProvider();
        provider = created;
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(created);
        });
    }

    private static ShaderAsset CreateCustomShader(
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

    private static byte[] CreateSpirV(int seed = 0)
    {
        var spirV = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(spirV, SpirVMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(spirV.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(spirV.AsSpan(8), (uint)(seed + 1));
        return spirV;
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;

        internal StoreFixture(ShaderAsset? defaultAsset = null)
        {
            if (defaultAsset is not null)
            {
                Assets.DefaultAsset = defaultAsset;
            }

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(Logs);
            });
            Assets.CompleteSpirV(
                Assets.DefaultAsset!.SpirV.Path!,
                CreateSpirV(seed: 100));
            Store = ShaderStore.Create(
                Assets,
                Factory,
                Assets,
                _loggerFactory.CreateLogger("Bork2d.Render"));
        }

        internal ControllableShaderAssets Assets { get; } = new();

        internal FakeShaderModuleFactory Factory { get; } = new();

        internal CapturingLoggerProvider Logs { get; } = new();

        internal ShaderStore Store { get; }

        internal ShaderRenderData Freeze(ShaderAsset asset) => ShaderRenderData.Freeze(asset);

        public void Dispose()
        {
            Store.Dispose();
            _loggerFactory.Dispose();
        }
    }

    private static ShaderAsset CreateShader(
        DataRef? spirV = null,
        uint layoutVersion = ShaderLayout.CurrentVersion,
        string entryPoint = "main")
        => new()
        {
            SpirV = spirV ?? ShaderStore.DefaultSpirVPath,
            LayoutVersion = layoutVersion,
            EntryPoint = entryPoint,
        };

    private static byte[] CreateSpirV()
    {
        var spirV = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(spirV, SpirVMagic);
        return spirV;
    }

    private sealed class FakeShaderAssets(ShaderAsset? shader, byte[]? spirV) : IAssets
    {
        public Task<T> LoadAsset<T>(string assetPath)
            where T : IShapeable<T>
        {
            if (typeof(T) != typeof(ShaderAsset))
            {
                throw new NotSupportedException($"Unsupported asset type {typeof(T)}.");
            }

            if (shader is null)
            {
                return Task.FromException<T>(
                    new AssetException(
                        AssetErrorKind.NotFound,
                        assetPath,
                        $"No asset at '{assetPath}'."));
            }

            return Task.FromResult((T)(object)shader);
        }

        public Task<T> LoadAsset<T>(AssetRef<T> reference)
            where T : IShapeable<T>
            => reference.HasValue
                ? LoadAsset<T>(reference.Path!)
                : throw new ArgumentException("The reference is absent.", nameof(reference));

        public Task<byte[]> LoadData(string assetPath)
            => spirV is not null
                ? Task.FromResult(spirV)
                : Task.FromException<byte[]>(
                    new AssetException(
                        AssetErrorKind.NotFound,
                        assetPath,
                        $"No data at '{assetPath}'."));

        public Task<byte[]> LoadData(DataRef reference)
            => reference.HasValue
                ? LoadData(reference.Path!)
                : throw new ArgumentException("The reference is absent.", nameof(reference));

        public void LoadPack(string packPath)
            => throw new NotSupportedException("Shader-store tests do not mount packs.");

        public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
            => throw new NotSupportedException("Shader-store tests do not browse assets.");

        public bool Exists(string assetPath)
            => throw new NotSupportedException("Shader-store tests do not query assets.");
    }
}
