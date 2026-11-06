using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Shaders;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The internal 2D pipeline: descriptor-set layouts, the pipeline layout, the three engine vertex
/// modules (sprite, line, and font glyph), one graphics pipeline per
/// <c>(kind, shader content hash, output format)</c>, and the cached page samplers.
/// </summary>
/// <remarks>
/// <para>
/// Descriptor set 0 has binding 0 (the per-frame sprite instance storage buffer, read by the sprite
/// and font vertex stages) and binding 1 (the bindless combined image sampler array, read by the
/// fragment stage). Binding 1 is partially bound, updated after bind, and uses a variable descriptor
/// count so page capacity can grow without recreating the pipeline layout; it is the highest binding
/// in its set, as Vulkan requires for a variable descriptor count. Descriptor set 1 has binding 2
/// (the per-frame dynamic uniform buffer holding the global block, read by the fragment stage); it
/// is a separate set because a set containing an update-after-bind binding may not also contain a
/// dynamic uniform buffer. The pipeline layout carries descriptor sets 0 and 1 and a 64-byte
/// view-projection push constant shared by every kind.
/// </para>
/// <para>
/// The sprite vertex module is created from the built-in pack's <c>shader/vertex-spv</c>, the line
/// vertex module from <c>shader/line-vertex-spv</c>, and the font vertex module from the same
/// <c>shader/vertex-spv</c> bytes as the sprite stage (a separate Vulkan handle, identical code and
/// instance ABI); the pipeline owns all three. A fragment module comes from the shader catalog, which
/// owns it, and the pipeline cache is keyed by the vertex-stage kind, the shader content hash, and
/// the output format, so identical content shares one fragment module while the sprite, line, and
/// font pipelines stay distinct and a swapchain format change lazily creates that format's pipeline.
/// </para>
/// <para>
/// All graphics pipelines use dynamic rendering with one linear UNORM scene color attachment, no
/// depth/stencil, dynamic viewport/scissor, disabled culling and depth test/write, and straight alpha
/// blending. The sprite and font pipelines have no vertex input and use triangle-strip topology with
/// four generated quad corners per instance. The line pipeline uses triangle-list topology and a
/// 112-byte vertex stride with attribute locations 0-7 (position, line UV, color, misc, and the four
/// instance uniform lanes).
/// </para>
/// <para>
/// All objects are render-thread-owned and created once at renderer startup (or when a new output
/// format or fragment module first appears). Destruction is best-effort, idempotent, and never throws
/// (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class TwoDimensionalPipeline : IDisposable, IShaderModuleFactory
{
    /// <summary>The push-constant size: one 4x4 view-projection matrix in bytes.</summary>
    internal const int PushConstantBytes = 64;

    /// <summary>The descriptor-set binding of the sprite instance storage buffer.</summary>
    internal const uint InstanceBinding = 0;

    /// <summary>The descriptor-set binding of the bindless combined image sampler array.</summary>
    internal const uint TextureBinding = 1;

    /// <summary>The descriptor-set binding of the dynamic global uniform buffer.</summary>
    internal const uint GlobalBinding = 2;

    /// <summary>The number of vertex-attribute locations the line pipeline binds.</summary>
    internal const int LineVertexAttributeCount = 8;

    private static readonly byte[] _entryPoint = "main\0"u8.ToArray();

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly IVulkanInstance? _instance;
    private readonly ShaderModule[] _vertexModules = new ShaderModule[3];
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly DescriptorSetLayout _globalSetLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly uint _maxTexturePages;
    private readonly Dictionary<(ShaderPipelineKind Kind, ulong ContentHash), Dictionary<Format, Pipeline>>
        _pipelines = [];
    private readonly Dictionary<(TextureFilter Filter, TextureWrap Wrap), Sampler> _samplers = [];
    private int _destroyed;

    private TwoDimensionalPipeline(
        Vk vk,
        Device device,
        ILogger logger,
        IVulkanInstance? instance,
        ShaderModule spriteVertexModule,
        ShaderModule lineVertexModule,
        ShaderModule fontVertexModule,
        DescriptorSetLayout descriptorSetLayout,
        DescriptorSetLayout globalSetLayout,
        PipelineLayout pipelineLayout,
        uint maxTexturePages)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _instance = instance;
        _vertexModules[(int)ShaderPipelineKind.Sprite] = spriteVertexModule;
        _vertexModules[(int)ShaderPipelineKind.Line] = lineVertexModule;
        _vertexModules[(int)ShaderPipelineKind.Font] = fontVertexModule;
        _descriptorSetLayout = descriptorSetLayout;
        _globalSetLayout = globalSetLayout;
        _pipelineLayout = pipelineLayout;
        _maxTexturePages = maxTexturePages;
    }

    /// <summary>Gets the descriptor-set layout shared by every frame's bindless set.</summary>
    internal DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;

    /// <summary>Gets the descriptor-set layout of the frame's global uniform set.</summary>
    internal DescriptorSetLayout GlobalSetLayout => _globalSetLayout;

    /// <summary>Gets the pipeline layout (64-byte push constant + descriptor sets 0 and 1).</summary>
    internal PipelineLayout PipelineLayout => _pipelineLayout;

    /// <summary>
    /// Gets the fixed layout maximum for the bindless sampler array: the smaller of the device's
    /// <c>maxPerStageDescriptorUpdateAfterBindSampledImages</c> and
    /// <c>maxDescriptorSetUpdateAfterBindSampledImages</c>.
    /// </summary>
    internal uint MaxTexturePages => _maxTexturePages;

    /// <summary>
    /// Creates the three vertex modules, descriptor-set layouts, and the pipeline layout. A Vulkan
    /// failure throws a <see cref="Bork2dException"/> so renderer startup fails cleanly (D-M6-14).
    /// </summary>
    /// <param name="device">The selected device; its API object owns the created handles.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <param name="spriteVertexSpirV">The built-in sprite vertex module's SPIR-V bytes.</param>
    /// <param name="lineVertexSpirV">The built-in line vertex module's SPIR-V bytes.</param>
    /// <returns>The created pipeline object; the caller owns it.</returns>
    /// <exception cref="ArgumentException">A vertex SPIR-V module is empty.</exception>
    internal static TwoDimensionalPipeline Create(
        VulkanDevice device,
        IVulkanInstance? instance,
        ILogger logger,
        byte[] spriteVertexSpirV,
        byte[] lineVertexSpirV)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(spriteVertexSpirV);
        ArgumentNullException.ThrowIfNull(lineVertexSpirV);
        if (spriteVertexSpirV.Length == 0)
        {
            throw new ArgumentException(
                "The sprite vertex SPIR-V module is empty.",
                nameof(spriteVertexSpirV));
        }

        if (lineVertexSpirV.Length == 0)
        {
            throw new ArgumentException(
                "The line vertex SPIR-V module is empty.",
                nameof(lineVertexSpirV));
        }

        uint maxTexturePages = (uint)TexturePageCapacity.EffectiveLimit(
            device.Limits.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            device.Limits.MaxDescriptorSetUpdateAfterBindSampledImages);
        TexturePageCapacity.EnsureSupportsInitialCapacity((int)maxTexturePages);

        Vk vk = device.Api;
        Device logicalDevice = device.Device;
        var spriteVertexModule = default(ShaderModule);
        var lineVertexModule = default(ShaderModule);
        var fontVertexModule = default(ShaderModule);
        var descriptorSetLayout = default(DescriptorSetLayout);
        var globalSetLayout = default(DescriptorSetLayout);
        var pipelineLayout = default(PipelineLayout);
        try
        {
            spriteVertexModule = CreateShaderModule(
                vk,
                logicalDevice,
                spriteVertexSpirV,
                ShaderStore.VertexSpirVPath);
            lineVertexModule = CreateShaderModule(
                vk,
                logicalDevice,
                lineVertexSpirV,
                ShaderStore.LineVertexSpirVPath);

            // The font pipeline uses the sprite vertex stage's bytes and instance ABI. A second
            // module handle keeps vertex-module ownership symmetric and makes the font pipeline a
            // distinct cache key without a dedicated glyph buffer or vertex asset.
            fontVertexModule = CreateShaderModule(
                vk,
                logicalDevice,
                spriteVertexSpirV,
                ShaderStore.VertexSpirVPath);
            descriptorSetLayout = CreateDescriptorSetLayout(vk, logicalDevice, maxTexturePages);
            globalSetLayout = CreateGlobalSetLayout(vk, logicalDevice);
            pipelineLayout = CreatePipelineLayout(
                vk,
                logicalDevice,
                descriptorSetLayout,
                globalSetLayout);

            var pipeline = new TwoDimensionalPipeline(
                vk,
                logicalDevice,
                logger,
                instance,
                spriteVertexModule,
                lineVertexModule,
                fontVertexModule,
                descriptorSetLayout,
                globalSetLayout,
                pipelineLayout,
                maxTexturePages);
            pipeline.SetObjectNames();
            return pipeline;
        }
        catch
        {
            DestroyBestEffort(
                vk,
                logicalDevice,
                logger,
                pipelineLayout,
                descriptorSetLayout,
                globalSetLayout,
                spriteVertexModule,
                lineVertexModule,
                fontVertexModule);
            throw;
        }
    }

    /// <summary>
    /// Creates a fragment shader module the caller owns.
    /// </summary>
    /// <param name="kind">The vertex-stage kind the module will be paired with, for labels.</param>
    /// <param name="spirv">The fragment SPIR-V bytes.</param>
    /// <param name="name">The asset path or diagnostic name used in errors and labels.</param>
    /// <returns>The created module; the shader store owns and destroys it.</returns>
    /// <exception cref="ArgumentException"><paramref name="spirv"/> is empty.</exception>
    /// <exception cref="Bork2dException">The module could not be created.</exception>
    internal ShaderModule CreateFragmentModule(
        ShaderPipelineKind kind,
        byte[] spirv,
        string name)
    {
        ArgumentNullException.ThrowIfNull(spirv);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (spirv.Length == 0)
        {
            throw new ArgumentException("The fragment SPIR-V module is empty.", nameof(spirv));
        }

        ShaderModule module = CreateShaderModule(_vk, _device, spirv, name);
        _instance?.SetObjectName(
            _device.Handle,
            ObjectType.ShaderModule,
            (nint)module.Handle,
            $"Bork2d 2D {Describe(kind)} fragment shader ({name})");
        return module;
    }

    /// <summary>Destroys a fragment shader module created by <see cref="CreateFragmentModule"/>.</summary>
    /// <param name="kind">The vertex-stage kind the module was created for, for labels.</param>
    /// <param name="module">The module to destroy; a zero handle is ignored.</param>
    internal void DestroyFragmentModule(ShaderPipelineKind kind, ShaderModule module)
        => DestroyShaderModule(module, $"{Describe(kind)} fragment");

    /// <summary>
    /// Gets the graphics pipeline for a kind, shader content hash, and output color format, creating
    /// it on first use.
    /// </summary>
    /// <param name="kind">The vertex-stage kind the pipeline combines with the fragment module.</param>
    /// <param name="colorFormat">The sRGB output color format.</param>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="fragmentModule">The fragment shader module the pipeline uses.</param>
    /// <returns>The cached pipeline for that kind, content, and format.</returns>
    /// <exception cref="Bork2dException">The pipeline could not be created.</exception>
    internal Pipeline GetOrCreatePipeline(
        ShaderPipelineKind kind,
        Format colorFormat,
        ulong contentHash,
        ShaderModule fragmentModule)
    {
        var key = (kind, contentHash);
        if (_pipelines.TryGetValue(key, out Dictionary<Format, Pipeline>? byFormat)
            && byFormat.TryGetValue(colorFormat, out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreateGraphicsPipeline(kind, colorFormat, fragmentModule);
        if (byFormat is null)
        {
            byFormat = [];
            _pipelines.Add(key, byFormat);
        }

        byFormat.Add(colorFormat, pipeline);
        _instance?.SetObjectName(
            _device.Handle,
            ObjectType.Pipeline,
            (nint)pipeline.Handle,
            $"Bork2d 2D {Describe(kind)} pipeline ({colorFormat}, content 0x{contentHash:X16})");
        return pipeline;
    }

    /// <summary>Looks up a cached pipeline without creating one.</summary>
    /// <param name="kind">The vertex-stage kind the pipeline combines with the fragment module.</param>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="colorFormat">The sRGB output color format.</param>
    /// <param name="pipeline">The cached pipeline when one exists.</param>
    /// <returns><see langword="true"/> when the pipeline exists.</returns>
    internal bool TryGetPipeline(
        ShaderPipelineKind kind,
        ulong contentHash,
        Format colorFormat,
        out Pipeline pipeline)
    {
        if (_pipelines.TryGetValue((kind, contentHash), out Dictionary<Format, Pipeline>? byFormat))
        {
            return byFormat.TryGetValue(colorFormat, out pipeline);
        }

        pipeline = default;
        return false;
    }

    /// <summary>Destroys every cached pipeline for one kind and shader content hash.</summary>
    /// <param name="kind">The vertex-stage kind whose pipeline is destroyed.</param>
    /// <param name="contentHash">The shader content hash whose pipeline is destroyed.</param>
    internal void DestroyPipelines(ShaderPipelineKind kind, ulong contentHash)
    {
        if (!_pipelines.Remove((kind, contentHash), out Dictionary<Format, Pipeline>? byFormat))
        {
            return;
        }

        foreach (Pipeline pipeline in byFormat.Values)
        {
            DestroyPipeline(pipeline);
        }
    }

    /// <summary>
    /// Gets the sampler for a descriptor's filter/wrap pair, creating and caching it on first use.
    /// </summary>
    /// <param name="filter">The descriptor filter; only <c>Nearest</c>/<c>Linear</c> are used.</param>
    /// <param name="wrap">The descriptor wrap; only <c>Clamp</c>/<c>Repeat</c> are used.</param>
    /// <returns>The cached sampler; the pipeline owns and destroys it.</returns>
    /// <exception cref="Bork2dException">The sampler could not be created.</exception>
    internal Sampler GetOrCreateSampler(TextureFilter filter, TextureWrap wrap)
    {
        if (_samplers.TryGetValue((filter, wrap), out Sampler existing))
        {
            return existing;
        }

        SamplerCreateInfo info = CreateSamplerInfo(filter, wrap);
        Result result = _vk.CreateSampler(_device, in info, null, out Sampler sampler);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateSampler failed for the sprite page sampler ({filter}, {wrap}) with "
                    + $"VkResult {result}.");
        }

        _samplers.Add((filter, wrap), sampler);
        _instance?.SetObjectName(
            _device.Handle,
            ObjectType.Sampler,
            (nint)sampler.Handle,
            $"Bork2d sprite sampler ({filter}, {wrap})");
        return sampler;
    }

    /// <summary>
    /// Creates the sampler state for a filter/wrap pair: the filter selects nearest or linear
    /// magnification/minification, mip filtering is always linear with the full chain available
    /// (<c>MinLod = 0</c>, <c>MaxLod = 1000</c>), and anisotropy is disabled.
    /// </summary>
    /// <remarks>
    /// The cache key stays <c>(Filter, Wrap)</c> because every page carries a complete mip chain down
    /// to 1x1 and the sampler's maximum LOD clamps to that chain. Exposing the state as a pure helper
    /// keeps the sampler contract testable without a GPU.
    /// </remarks>
    /// <param name="filter">The descriptor filter.</param>
    /// <param name="wrap">The descriptor wrap.</param>
    /// <returns>The sampler creation state.</returns>
    internal static SamplerCreateInfo CreateSamplerInfo(TextureFilter filter, TextureWrap wrap)
    {
        bool linear = filter == TextureFilter.Linear;
        bool repeat = wrap == TextureWrap.Repeat;
        return new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = linear ? Filter.Linear : Filter.Nearest,
            MinFilter = linear ? Filter.Linear : Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = repeat ? SamplerAddressMode.Repeat : SamplerAddressMode.ClampToEdge,
            AddressModeV = repeat ? SamplerAddressMode.Repeat : SamplerAddressMode.ClampToEdge,
            AddressModeW = repeat ? SamplerAddressMode.Repeat : SamplerAddressMode.ClampToEdge,
            MipLodBias = 0f,
            AnisotropyEnable = false,
            MaxAnisotropy = 1f,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MinLod = 0f,
            MaxLod = 1000f,
            BorderColor = BorderColor.FloatTransparentBlack,
            UnnormalizedCoordinates = false,
        };
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    ShaderModule IShaderModuleFactory.CreateModule(
        ShaderPipelineKind kind,
        byte[] spirV,
        string name)
        => CreateFragmentModule(kind, spirV, name);

    void IShaderModuleFactory.DestroyModule(ShaderPipelineKind kind, ShaderModule module)
        => DestroyFragmentModule(kind, module);

    Pipeline IShaderModuleFactory.GetOrCreatePipeline(
        ShaderPipelineKind kind,
        Format colorFormat,
        ulong contentHash,
        ShaderModule module)
        => GetOrCreatePipeline(kind, colorFormat, contentHash, module);

    bool IShaderModuleFactory.TryGetPipeline(
        ShaderPipelineKind kind,
        ulong contentHash,
        Format colorFormat,
        out Pipeline pipeline)
        => TryGetPipeline(kind, contentHash, colorFormat, out pipeline);

    void IShaderModuleFactory.DestroyPipelines(ShaderPipelineKind kind, ulong contentHash)
        => DestroyPipelines(kind, contentHash);

    /// <summary>Destroys the pipelines, samplers, vertex modules, and layouts; never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        foreach (Dictionary<Format, Pipeline> byFormat in _pipelines.Values)
        {
            foreach (Pipeline pipeline in byFormat.Values)
            {
                DestroyPipeline(pipeline);
            }
        }

        _pipelines.Clear();

        foreach (Sampler sampler in _samplers.Values)
        {
            DestroySampler(sampler);
        }

        _samplers.Clear();

        DestroyShaderModule(_vertexModules[(int)ShaderPipelineKind.Sprite], "sprite vertex");
        DestroyShaderModule(_vertexModules[(int)ShaderPipelineKind.Line], "line vertex");
        DestroyShaderModule(_vertexModules[(int)ShaderPipelineKind.Font], "font vertex");
        _vertexModules[(int)ShaderPipelineKind.Sprite] = default;
        _vertexModules[(int)ShaderPipelineKind.Line] = default;
        _vertexModules[(int)ShaderPipelineKind.Font] = default;
        DestroyPipelineLayout(_pipelineLayout);
        DestroyDescriptorSetLayout(_descriptorSetLayout);
        DestroyDescriptorSetLayout(_globalSetLayout);
    }

    private static string Describe(ShaderPipelineKind kind)
        => kind switch
        {
            ShaderPipelineKind.Sprite => "sprite",
            ShaderPipelineKind.Line => "line",
            _ => "font",
        };

    private Pipeline CreateGraphicsPipeline(
        ShaderPipelineKind kind,
        Format colorFormat,
        ShaderModule fragmentModule)
    {
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];

        // The line vertex input state must outlive the CreateGraphicsPipelines call, so it is
        // allocated in this method's frame rather than in a helper.
        VertexInputBindingDescription* lineBinding =
            stackalloc VertexInputBindingDescription[1];
        VertexInputAttributeDescription* lineAttributes =
            stackalloc VertexInputAttributeDescription[LineVertexAttributeCount];
        fixed (byte* entryPoint = _entryPoint)
        {
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexModules[(int)kind],
                PName = entryPoint,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentModule,
                PName = entryPoint,
            };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleStrip,
                PrimitiveRestartEnable = false,
            };
            if (kind == ShaderPipelineKind.Line)
            {
                ConfigureLineVertexInput(
                    ref vertexInput,
                    &inputAssembly,
                    lineBinding,
                    lineAttributes);
            }

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };
            var rasterization = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false,
                DepthBiasConstantFactor = 0f,
                DepthBiasClamp = 0f,
                DepthBiasSlopeFactor = 0f,
                LineWidth = 1f,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
                SampleShadingEnable = false,
                MinSampleShading = 1f,
                PSampleMask = null,
                AlphaToCoverageEnable = false,
                AlphaToOneEnable = false,
            };
            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.SrcAlpha,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit
                    | ColorComponentFlags.GBit
                    | ColorComponentFlags.BBit
                    | ColorComponentFlags.ABit,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                LogicOp = LogicOp.Copy,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment,
            };
            DynamicState* dynamicStates = stackalloc DynamicState[2];
            dynamicStates[0] = DynamicState.Viewport;
            dynamicStates[1] = DynamicState.Scissor;
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };
            Format* colorFormats = stackalloc Format[1];
            colorFormats[0] = colorFormat;
            var rendering = new PipelineRenderingCreateInfo
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ViewMask = 0,
                ColorAttachmentCount = 1,
                PColorAttachmentFormats = colorFormats,
                DepthAttachmentFormat = Format.Undefined,
                StencilAttachmentFormat = Format.Undefined,
            };
            var createInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                PNext = &rendering,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PTessellationState = null,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PDepthStencilState = null,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicState,
                Layout = _pipelineLayout,
                RenderPass = default,
                Subpass = 0,
                BasePipelineHandle = default,
                BasePipelineIndex = -1,
            };

            Result result = _vk.CreateGraphicsPipelines(
                _device,
                default,
                1,
                &createInfo,
                null,
                out Pipeline pipeline);
            if (result != Result.Success || pipeline.Handle == 0)
            {
                throw new Bork2dException(
                    $"vkCreateGraphicsPipelines failed for the 2D {Describe(kind)} pipeline with "
                        + $"color format {colorFormat} and VkResult {result}.");
            }

            return pipeline;
        }
    }

    /// <summary>
    /// Fills the line pipeline's vertex input state: one 112-byte per-vertex binding and attribute
    /// locations 0-7 matching the built-in line vertex shader and <see cref="LineVertex"/>.
    /// </summary>
    /// <param name="vertexInput">The vertex input state to fill.</param>
    /// <param name="inputAssembly">The input-assembly state whose topology becomes a triangle list.</param>
    /// <param name="binding">
    /// The caller-owned binding storage; it must outlive the pipeline creation call.
    /// </param>
    /// <param name="attributes">
    /// The caller-owned attribute storage; it must outlive the pipeline creation call.
    /// </param>
    private static void ConfigureLineVertexInput(
        ref PipelineVertexInputStateCreateInfo vertexInput,
        PipelineInputAssemblyStateCreateInfo* inputAssembly,
        VertexInputBindingDescription* binding,
        VertexInputAttributeDescription* attributes)
    {
        binding[0] = new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = LineVertex.SizeInBytes,
            InputRate = VertexInputRate.Vertex,
        };
        attributes[0] = new VertexInputAttributeDescription
        {
            Location = 0,
            Binding = 0,
            Format = Format.R32G32Sfloat,
            Offset = LineVertex.PositionOffset,
        };
        attributes[1] = new VertexInputAttributeDescription
        {
            Location = 1,
            Binding = 0,
            Format = Format.R32G32Sfloat,
            Offset = LineVertex.LineUvOffset,
        };
        attributes[2] = new VertexInputAttributeDescription
        {
            Location = 2,
            Binding = 0,
            Format = Format.R32G32B32A32Sfloat,
            Offset = LineVertex.ColorOffset,
        };
        attributes[3] = new VertexInputAttributeDescription
        {
            Location = 3,
            Binding = 0,
            Format = Format.R32G32B32A32Sfloat,
            Offset = LineVertex.MiscOffset,
        };
        attributes[4] = new VertexInputAttributeDescription
        {
            Location = 4,
            Binding = 0,
            Format = Format.R32G32B32A32Sfloat,
            Offset = LineVertex.Params0Offset,
        };
        attributes[5] = new VertexInputAttributeDescription
        {
            Location = 5,
            Binding = 0,
            Format = Format.R32G32B32A32Sfloat,
            Offset = LineVertex.Params1Offset,
        };
        attributes[6] = new VertexInputAttributeDescription
        {
            Location = 6,
            Binding = 0,
            Format = Format.R32G32B32A32Sfloat,
            Offset = LineVertex.Params2Offset,
        };
        attributes[7] = new VertexInputAttributeDescription
        {
            Location = 7,
            Binding = 0,
            Format = Format.R32G32B32A32Sfloat,
            Offset = LineVertex.Params3Offset,
        };

        vertexInput.VertexBindingDescriptionCount = 1;
        vertexInput.PVertexBindingDescriptions = binding;
        vertexInput.VertexAttributeDescriptionCount = LineVertexAttributeCount;
        vertexInput.PVertexAttributeDescriptions = attributes;
        inputAssembly->Topology = PrimitiveTopology.TriangleList;
    }

    private void SetObjectNames()
    {
        if (_instance is null)
        {
            return;
        }

        nint device = _device.Handle;
        _instance.SetObjectName(
            device,
            ObjectType.ShaderModule,
            (nint)_vertexModules[(int)ShaderPipelineKind.Sprite].Handle,
            "Bork2d sprite vertex shader");
        _instance.SetObjectName(
            device,
            ObjectType.ShaderModule,
            (nint)_vertexModules[(int)ShaderPipelineKind.Line].Handle,
            "Bork2d line vertex shader");
        _instance.SetObjectName(
            device,
            ObjectType.ShaderModule,
            (nint)_vertexModules[(int)ShaderPipelineKind.Font].Handle,
            "Bork2d font vertex shader");
        _instance.SetObjectName(
            device,
            ObjectType.DescriptorSetLayout,
            (nint)_descriptorSetLayout.Handle,
            "Bork2d 2D descriptor set layout");
        _instance.SetObjectName(
            device,
            ObjectType.DescriptorSetLayout,
            (nint)_globalSetLayout.Handle,
            "Bork2d 2D global descriptor set layout");
        _instance.SetObjectName(
            device,
            ObjectType.PipelineLayout,
            (nint)_pipelineLayout.Handle,
            "Bork2d 2D pipeline layout");
    }

    private static ShaderModule CreateShaderModule(
        Vk vk,
        Device device,
        byte[] spirv,
        string name)
    {
        fixed (byte* code = spirv)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code,
            };
            Result result = vk.CreateShaderModule(device, in info, null, out ShaderModule module);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateShaderModule failed for '{name}' with VkResult {result}.");
            }

            return module;
        }
    }

    private static DescriptorSetLayout CreateDescriptorSetLayout(
        Vk vk,
        Device device,
        uint maxTexturePages)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = InstanceBinding,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit,
            PImmutableSamplers = null,
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = TextureBinding,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = maxTexturePages,
            StageFlags = ShaderStageFlags.FragmentBit,
            PImmutableSamplers = null,
        };

        // The variable/UAB binding must be the highest binding in the set; the global uniform set
        // is separate because a UAB set may not contain a dynamic uniform buffer.
        DescriptorBindingFlags* bindingFlags = stackalloc DescriptorBindingFlags[2];
        bindingFlags[0] = DescriptorBindingFlags.None;
        bindingFlags[1] = DescriptorBindingFlags.PartiallyBoundBit
            | DescriptorBindingFlags.UpdateAfterBindBit
            | DescriptorBindingFlags.VariableDescriptorCountBit;
        var flagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 2,
            PBindingFlags = bindingFlags,
        };
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            PNext = &flagsInfo,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            BindingCount = 2,
            PBindings = bindings,
        };
        Result result = vk.CreateDescriptorSetLayout(
            device,
            in layoutInfo,
            null,
            out DescriptorSetLayout layout);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateDescriptorSetLayout failed for the 2D descriptor set with VkResult "
                    + $"{result}.");
        }

        return layout;
    }

    private static DescriptorSetLayout CreateGlobalSetLayout(Vk vk, Device device)
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = GlobalBinding,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
            PImmutableSamplers = null,
        };
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding,
        };
        Result result = vk.CreateDescriptorSetLayout(
            device,
            in layoutInfo,
            null,
            out DescriptorSetLayout layout);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateDescriptorSetLayout failed for the 2D global descriptor set with "
                    + $"VkResult {result}.");
        }

        return layout;
    }

    private static PipelineLayout CreatePipelineLayout(
        Vk vk,
        Device device,
        DescriptorSetLayout descriptorSetLayout,
        DescriptorSetLayout globalSetLayout)
    {
        var pushConstant = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = PushConstantBytes,
        };
        DescriptorSetLayout* setLayouts = stackalloc DescriptorSetLayout[2];
        setLayouts[0] = descriptorSetLayout;
        setLayouts[1] = globalSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstant,
        };
        Result result = vk.CreatePipelineLayout(
            device,
            in layoutInfo,
            null,
            out PipelineLayout layout);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreatePipelineLayout failed for the 2D pipeline with VkResult {result}.");
        }

        return layout;
    }

    private void DestroyPipeline(Pipeline pipeline)
    {
        if (pipeline.Handle == 0)
        {
            return;
        }

        try
        {
            _vk.DestroyPipeline(_device, pipeline, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan 2D pipeline destruction failed.");
        }
    }

    private void DestroySampler(Sampler sampler)
    {
        if (sampler.Handle == 0)
        {
            return;
        }

        try
        {
            _vk.DestroySampler(_device, sampler, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan 2D sampler destruction failed.");
        }
    }

    private void DestroyShaderModule(ShaderModule module, string stage)
    {
        if (module.Handle == 0)
        {
            return;
        }

        try
        {
            _vk.DestroyShaderModule(_device, module, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Vulkan 2D {Stage} shader module destruction failed.",
                stage);
        }
    }

    private void DestroyPipelineLayout(PipelineLayout layout)
    {
        if (layout.Handle == 0)
        {
            return;
        }

        try
        {
            _vk.DestroyPipelineLayout(_device, layout, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan 2D pipeline-layout destruction failed.");
        }
    }

    private void DestroyDescriptorSetLayout(DescriptorSetLayout layout)
    {
        if (layout.Handle == 0)
        {
            return;
        }

        try
        {
            _vk.DestroyDescriptorSetLayout(_device, layout, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan 2D descriptor-set-layout destruction failed.");
        }
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        PipelineLayout pipelineLayout,
        DescriptorSetLayout descriptorSetLayout,
        DescriptorSetLayout globalSetLayout,
        ShaderModule spriteVertexModule,
        ShaderModule lineVertexModule,
        ShaderModule fontVertexModule)
    {
        if (pipelineLayout.Handle != 0)
        {
            try
            {
                vk.DestroyPipelineLayout(device, pipelineLayout, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan 2D pipeline-layout destruction failed.");
            }
        }

        if (descriptorSetLayout.Handle != 0)
        {
            try
            {
                vk.DestroyDescriptorSetLayout(device, descriptorSetLayout, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan 2D descriptor-set-layout destruction failed.");
            }
        }

        if (globalSetLayout.Handle != 0)
        {
            try
            {
                vk.DestroyDescriptorSetLayout(device, globalSetLayout, null);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Vulkan 2D global descriptor-set-layout destruction failed.");
            }
        }

        DestroyShaderModuleBestEffort(vk, device, logger, spriteVertexModule, "sprite vertex");
        DestroyShaderModuleBestEffort(vk, device, logger, lineVertexModule, "line vertex");
        DestroyShaderModuleBestEffort(vk, device, logger, fontVertexModule, "font vertex");
    }

    private static void DestroyShaderModuleBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        ShaderModule module,
        string stage)
    {
        if (module.Handle == 0)
        {
            return;
        }

        try
        {
            vk.DestroyShaderModule(device, module, null);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Vulkan 2D {Stage} shader module destruction failed.",
                stage);
        }
    }
}
