using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Shaders;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The internal sprite pipeline: descriptor-set layout, pipeline layout, the vertex shader module,
/// one graphics pipeline per (shader content hash, output format), and the cached page samplers
/// (M8 WP-2, shader ABI v2).
/// </summary>
/// <remarks>
/// <para>
/// Descriptor set 0 has binding 0 (the per-frame instance storage buffer, read by the vertex stage)
/// and binding 1 (the bindless combined image sampler array, read by the fragment stage). Binding 1
/// is partially bound, updated after bind, and uses a variable descriptor count so page capacity can
/// grow without recreating the pipeline layout; it is the highest binding in its set, as Vulkan
/// requires for a variable descriptor count. Descriptor set 1 has binding 2 (the per-frame dynamic
/// uniform buffer holding the global block, read by the fragment stage); it is a separate set
/// because a set containing an update-after-bind binding may not also contain a dynamic uniform
/// buffer. The pipeline layout carries descriptor sets 0 and 1 and a 64-byte view-projection push
/// constant.
/// </para>
/// <para>
/// The vertex module is created from the built-in pack's <c>shader/vertex-spv</c>; the pipeline owns
/// it. A fragment module comes from the shader catalog, which owns it, and the pipeline cache is
/// keyed by the shader content hash and the output format, so identical content shares one pipeline
/// and a swapchain format change lazily creates that format's pipeline. The graphics pipeline uses
/// dynamic rendering with one linear UNORM scene color attachment, no depth/stencil, no vertex input,
/// triangle-strip topology, four vertices per instance, dynamic viewport/scissor, disabled culling and
/// depth test/write, and straight alpha blending.
/// </para>
/// <para>
/// All objects are render-thread-owned and created once at renderer startup (or when a new output
/// format or fragment module first appears). Destruction is best-effort, idempotent, and never
/// throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class SpritePipeline : IDisposable, IShaderModuleFactory
{
    /// <summary>The push-constant size: one 4x4 view-projection matrix in bytes.</summary>
    internal const int PushConstantBytes = 64;

    /// <summary>The descriptor-set binding of the instance storage buffer.</summary>
    internal const uint InstanceBinding = 0;

    /// <summary>The descriptor-set binding of the bindless combined image sampler array.</summary>
    internal const uint TextureBinding = 1;

    /// <summary>The descriptor-set binding of the dynamic global uniform buffer.</summary>
    internal const uint GlobalBinding = 2;

    private static readonly byte[] _entryPoint = "main\0"u8.ToArray();

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly IVulkanInstance? _instance;
    private readonly ShaderModule _vertexModule;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly DescriptorSetLayout _globalSetLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly uint _maxTexturePages;
    private readonly Dictionary<ulong, Dictionary<Format, Pipeline>> _pipelines = [];
    private readonly Dictionary<(TextureFilter Filter, TextureWrap Wrap), Sampler> _samplers = [];
    private int _destroyed;

    private SpritePipeline(
        Vk vk,
        Device device,
        ILogger logger,
        IVulkanInstance? instance,
        ShaderModule vertexModule,
        DescriptorSetLayout descriptorSetLayout,
        DescriptorSetLayout globalSetLayout,
        PipelineLayout pipelineLayout,
        uint maxTexturePages)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _instance = instance;
        _vertexModule = vertexModule;
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
    /// Creates the vertex module, descriptor-set layout, and pipeline layout. A Vulkan failure
    /// throws a <see cref="Bork2dException"/> so renderer startup fails cleanly (D-M6-14).
    /// </summary>
    /// <param name="device">The selected device; its API object owns the created handles.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <param name="vertexSpirV">The built-in sprite vertex module's SPIR-V bytes.</param>
    /// <returns>The created pipeline object; the caller owns it.</returns>
    /// <exception cref="ArgumentException"><paramref name="vertexSpirV"/> is empty.</exception>
    internal static SpritePipeline Create(
        VulkanDevice device,
        IVulkanInstance? instance,
        ILogger logger,
        byte[] vertexSpirV)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(vertexSpirV);
        if (vertexSpirV.Length == 0)
        {
            throw new ArgumentException("The vertex SPIR-V module is empty.", nameof(vertexSpirV));
        }

        uint maxTexturePages = (uint)TexturePageCapacity.EffectiveLimit(
            device.Limits.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            device.Limits.MaxDescriptorSetUpdateAfterBindSampledImages);
        TexturePageCapacity.EnsureSupportsInitialCapacity((int)maxTexturePages);

        Vk vk = device.Api;
        Device logicalDevice = device.Device;
        var vertexModule = default(ShaderModule);
        var descriptorSetLayout = default(DescriptorSetLayout);
        var globalSetLayout = default(DescriptorSetLayout);
        var pipelineLayout = default(PipelineLayout);
        try
        {
            vertexModule = CreateShaderModule(
                vk,
                logicalDevice,
                vertexSpirV,
                "shader/vertex-spv");
            descriptorSetLayout = CreateDescriptorSetLayout(vk, logicalDevice, maxTexturePages);
            globalSetLayout = CreateGlobalSetLayout(vk, logicalDevice);
            pipelineLayout = CreatePipelineLayout(
                vk,
                logicalDevice,
                descriptorSetLayout,
                globalSetLayout);

            var pipeline = new SpritePipeline(
                vk,
                logicalDevice,
                logger,
                instance,
                vertexModule,
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
                vertexModule);
            throw;
        }
    }
    /// <summary>
    /// Creates a fragment shader module the caller owns.
    /// </summary>
    /// <param name="spirv">The fragment SPIR-V bytes.</param>
    /// <param name="name">The asset path or diagnostic name used in errors and labels.</param>
    /// <returns>The created module; the shader store owns and destroys it.</returns>
    /// <exception cref="ArgumentException"><paramref name="spirv"/> is empty.</exception>
    /// <exception cref="Bork2dException">The module could not be created.</exception>
    internal ShaderModule CreateFragmentModule(byte[] spirv, string name)
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
            $"Bork2d sprite fragment shader ({name})");
        return module;
    }

    /// <summary>Destroys a fragment shader module created by <see cref="CreateFragmentModule"/>.</summary>
    /// <param name="module">The module to destroy; a zero handle is ignored.</param>
    internal void DestroyFragmentModule(ShaderModule module)
        => DestroyShaderModule(module, "fragment");

    /// <summary>
    /// Gets the graphics pipeline for a shader content hash and output color format, creating it on
    /// first use.
    /// </summary>
    /// <param name="colorFormat">The sRGB output color format.</param>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="fragmentModule">The fragment shader module the pipeline uses.</param>
    /// <returns>The cached pipeline for that content and format.</returns>
    /// <exception cref="Bork2dException">The pipeline could not be created.</exception>
    internal Pipeline GetOrCreatePipeline(
        Format colorFormat,
        ulong contentHash,
        ShaderModule fragmentModule)
    {
        if (_pipelines.TryGetValue(contentHash, out Dictionary<Format, Pipeline>? byFormat)
            && byFormat.TryGetValue(colorFormat, out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreateGraphicsPipeline(colorFormat, fragmentModule);
        if (byFormat is null)
        {
            byFormat = [];
            _pipelines.Add(contentHash, byFormat);
        }

        byFormat.Add(colorFormat, pipeline);
        _instance?.SetObjectName(
            _device.Handle,
            ObjectType.Pipeline,
            (nint)pipeline.Handle,
            $"Bork2d sprite pipeline ({colorFormat}, content 0x{contentHash:X16})");
        return pipeline;
    }

    /// <summary>Looks up a cached pipeline without creating one.</summary>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="colorFormat">The sRGB output color format.</param>
    /// <param name="pipeline">The cached pipeline when one exists.</param>
    /// <returns><see langword="true"/> when the pipeline exists.</returns>
    internal bool TryGetPipeline(ulong contentHash, Format colorFormat, out Pipeline pipeline)
    {
        if (_pipelines.TryGetValue(contentHash, out Dictionary<Format, Pipeline>? byFormat))
        {
            return byFormat.TryGetValue(colorFormat, out pipeline);
        }

        pipeline = default;
        return false;
    }

    /// <summary>Destroys every cached pipeline for one shader content hash.</summary>
    /// <param name="contentHash">The shader content hash whose pipelines are destroyed.</param>
    internal void DestroyPipelines(ulong contentHash)
    {
        if (!_pipelines.Remove(contentHash, out Dictionary<Format, Pipeline>? byFormat))
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

    ShaderModule IShaderModuleFactory.CreateModule(byte[] spirV, string name)
        => CreateFragmentModule(spirV, name);

    void IShaderModuleFactory.DestroyModule(ShaderModule module) => DestroyFragmentModule(module);

    Pipeline IShaderModuleFactory.GetOrCreatePipeline(
        Format colorFormat,
        ulong contentHash,
        ShaderModule module)
        => GetOrCreatePipeline(colorFormat, contentHash, module);

    bool IShaderModuleFactory.TryGetPipeline(
        ulong contentHash,
        Format colorFormat,
        out Pipeline pipeline)
        => TryGetPipeline(contentHash, colorFormat, out pipeline);

    void IShaderModuleFactory.DestroyPipelines(ulong contentHash) => DestroyPipelines(contentHash);

    /// <summary>Destroys the pipelines, samplers, vertex module, and layouts; never throws.</summary>
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

        DestroyShaderModule(_vertexModule, "vertex");
        DestroyPipelineLayout(_pipelineLayout);
        DestroyDescriptorSetLayout(_descriptorSetLayout);
        DestroyDescriptorSetLayout(_globalSetLayout);
    }

    private Pipeline CreateGraphicsPipeline(Format colorFormat, ShaderModule fragmentModule)
    {
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        fixed (byte* entryPoint = _entryPoint)
        {
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexModule,
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
                    $"vkCreateGraphicsPipelines failed for the sprite pipeline with color format "
                        + $"{colorFormat} and VkResult {result}.");
            }

            return pipeline;
        }
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
            (nint)_vertexModule.Handle,
            "Bork2d sprite vertex shader");
        _instance.SetObjectName(
            device,
            ObjectType.DescriptorSetLayout,
            (nint)_descriptorSetLayout.Handle,
            "Bork2d sprite descriptor set layout");
        _instance.SetObjectName(
            device,
            ObjectType.DescriptorSetLayout,
            (nint)_globalSetLayout.Handle,
            "Bork2d sprite global descriptor set layout");
        _instance.SetObjectName(
            device,
            ObjectType.PipelineLayout,
            (nint)_pipelineLayout.Handle,
            "Bork2d sprite pipeline layout");
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
        Result result = vk.CreateDescriptorSetLayout(device, in layoutInfo, null, out DescriptorSetLayout layout);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateDescriptorSetLayout failed for the sprite descriptor set with VkResult "
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
                $"vkCreateDescriptorSetLayout failed for the sprite global descriptor set with "
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
        Result result = vk.CreatePipelineLayout(device, in layoutInfo, null, out PipelineLayout layout);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreatePipelineLayout failed for the sprite pipeline with VkResult {result}.");
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
            _logger.LogError(exception, "Vulkan sprite pipeline destruction failed.");
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
            _logger.LogError(exception, "Vulkan sprite sampler destruction failed.");
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
                "Vulkan sprite {Stage} shader module destruction failed.",
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
            _logger.LogError(exception, "Vulkan sprite pipeline-layout destruction failed.");
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
            _logger.LogError(exception, "Vulkan sprite descriptor-set-layout destruction failed.");
        }
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        PipelineLayout pipelineLayout,
        DescriptorSetLayout descriptorSetLayout,
        DescriptorSetLayout globalSetLayout,
        ShaderModule vertexModule)
    {
        if (pipelineLayout.Handle != 0)
        {
            try
            {
                vk.DestroyPipelineLayout(device, pipelineLayout, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan sprite pipeline-layout destruction failed.");
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
                logger.LogError(exception, "Vulkan sprite descriptor-set-layout destruction failed.");
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
                    "Vulkan sprite global descriptor-set-layout destruction failed.");
            }
        }

        if (vertexModule.Handle != 0)
        {
            try
            {
                vk.DestroyShaderModule(device, vertexModule, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan sprite vertex shader module destruction failed.");
            }
        }
    }
}
