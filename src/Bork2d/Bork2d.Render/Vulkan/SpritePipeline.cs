using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The internal sprite pipeline: descriptor-set layout, pipeline layout, one graphics pipeline per
/// sRGB output format, and the cached page samplers (M8 WP-2).
/// </summary>
/// <remarks>
/// <para>
/// The descriptor set has binding 0 (the per-frame instance storage buffer, read by the vertex
/// stage) and binding 1 (the bindless combined image sampler array, read by the fragment stage).
/// Binding 1 is partially bound, updated after bind, and uses a variable descriptor count so page
/// capacity can grow without recreating the pipeline layout. The pipeline layout carries a 64-byte
/// view-projection push constant and descriptor set 0.
/// </para>
/// <para>
/// The graphics pipeline uses dynamic rendering with one sRGB color attachment, no depth/stencil,
/// no vertex input, triangle-strip topology, four vertices per instance, dynamic viewport/scissor,
/// disabled culling and depth test/write, and straight alpha blending. WP-2 only creates and owns
/// the objects; recording draws is WP-3/WP-4.
/// </para>
/// <para>
/// All objects are render-thread-owned and created once at renderer startup (or when a new output
/// format first appears). Destruction is best-effort, idempotent, and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class SpritePipeline : IDisposable
{
    /// <summary>The push-constant size: one 4x4 view-projection matrix in bytes.</summary>
    internal const int PushConstantBytes = 64;

    /// <summary>The descriptor-set binding of the instance storage buffer.</summary>
    internal const uint InstanceBinding = 0;

    /// <summary>The descriptor-set binding of the bindless combined image sampler array.</summary>
    internal const uint TextureBinding = 1;

    private static readonly byte[] _entryPoint = "main\0"u8.ToArray();

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly IVulkanInstance? _instance;
    private readonly ShaderModule _vertexModule;
    private readonly ShaderModule _fragmentModule;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly uint _maxTexturePages;
    private readonly Dictionary<Format, Pipeline> _pipelines = [];
    private readonly Dictionary<(TextureFilter Filter, TextureWrap Wrap), Sampler> _samplers = [];
    private int _destroyed;

    private SpritePipeline(
        Vk vk,
        Device device,
        ILogger logger,
        IVulkanInstance? instance,
        ShaderModule vertexModule,
        ShaderModule fragmentModule,
        DescriptorSetLayout descriptorSetLayout,
        PipelineLayout pipelineLayout,
        uint maxTexturePages)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _instance = instance;
        _vertexModule = vertexModule;
        _fragmentModule = fragmentModule;
        _descriptorSetLayout = descriptorSetLayout;
        _pipelineLayout = pipelineLayout;
        _maxTexturePages = maxTexturePages;
    }

    /// <summary>Gets the descriptor-set layout shared by every frame's bindless set.</summary>
    internal DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;

    /// <summary>Gets the pipeline layout (64-byte push constant + descriptor set 0).</summary>
    internal PipelineLayout PipelineLayout => _pipelineLayout;

    /// <summary>
    /// Gets the fixed layout maximum for the bindless sampler array: the smaller of the device's
    /// <c>maxPerStageDescriptorUpdateAfterBindSampledImages</c> and
    /// <c>maxDescriptorSetUpdateAfterBindSampledImages</c>.
    /// </summary>
    internal uint MaxTexturePages => _maxTexturePages;

    /// <summary>
    /// Creates the shader modules, descriptor-set layout, and pipeline layout. A Vulkan failure
    /// throws a <see cref="Bork2dException"/> so renderer startup fails cleanly (D-M6-14).
    /// </summary>
    /// <param name="device">The selected device; its API object owns the created handles.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <returns>The created pipeline object; the caller owns it.</returns>
    internal static SpritePipeline Create(
        VulkanDevice device,
        IVulkanInstance? instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);

        uint maxTexturePages = (uint)TexturePageCapacity.EffectiveLimit(
            device.Limits.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            device.Limits.MaxDescriptorSetUpdateAfterBindSampledImages);
        TexturePageCapacity.EnsureSupportsInitialCapacity((int)maxTexturePages);

        Vk vk = device.Api;
        Device logicalDevice = device.Device;
        var vertexModule = default(ShaderModule);
        var fragmentModule = default(ShaderModule);
        var descriptorSetLayout = default(DescriptorSetLayout);
        var pipelineLayout = default(PipelineLayout);
        try
        {
            vertexModule = CreateShaderModule(
                vk,
                logicalDevice,
                SpriteShaderSpirV.Vertex,
                "sprite.vert");
            fragmentModule = CreateShaderModule(
                vk,
                logicalDevice,
                SpriteShaderSpirV.Fragment,
                "sprite.frag");
            descriptorSetLayout = CreateDescriptorSetLayout(vk, logicalDevice, maxTexturePages);
            pipelineLayout = CreatePipelineLayout(vk, logicalDevice, descriptorSetLayout);

            var pipeline = new SpritePipeline(
                vk,
                logicalDevice,
                logger,
                instance,
                vertexModule,
                fragmentModule,
                descriptorSetLayout,
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
                vertexModule,
                fragmentModule);
            throw;
        }
    }

    /// <summary>
    /// Gets the graphics pipeline for <paramref name="colorFormat"/>, creating it on first use.
    /// </summary>
    /// <param name="colorFormat">The sRGB output color format.</param>
    /// <returns>The pipeline for that format.</returns>
    /// <exception cref="Bork2dException">The pipeline could not be created.</exception>
    internal Pipeline GetOrCreatePipeline(Format colorFormat)
    {
        if (_pipelines.TryGetValue(colorFormat, out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreateGraphicsPipeline(colorFormat);
        _pipelines.Add(colorFormat, pipeline);
        _instance?.SetObjectName(
            _device.Handle,
            ObjectType.Pipeline,
            (nint)pipeline.Handle,
            $"Bork2d sprite pipeline ({colorFormat})");
        return pipeline;
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

        bool linear = filter == TextureFilter.Linear;
        bool repeat = wrap == TextureWrap.Repeat;
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = linear ? Filter.Linear : Filter.Nearest,
            MinFilter = linear ? Filter.Linear : Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = repeat ? SamplerAddressMode.Repeat : SamplerAddressMode.ClampToEdge,
            AddressModeV = repeat ? SamplerAddressMode.Repeat : SamplerAddressMode.ClampToEdge,
            AddressModeW = repeat ? SamplerAddressMode.Repeat : SamplerAddressMode.ClampToEdge,
            MipLodBias = 0f,
            AnisotropyEnable = false,
            MaxAnisotropy = 1f,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MinLod = 0f,
            MaxLod = 0f,
            BorderColor = BorderColor.FloatTransparentBlack,
            UnnormalizedCoordinates = false,
        };
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

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Destroys the pipelines, samplers, modules, and layouts; never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        foreach (Pipeline pipeline in _pipelines.Values)
        {
            DestroyPipeline(pipeline);
        }

        _pipelines.Clear();

        foreach (Sampler sampler in _samplers.Values)
        {
            DestroySampler(sampler);
        }

        _samplers.Clear();

        DestroyShaderModule(_vertexModule, "vertex");
        DestroyShaderModule(_fragmentModule, "fragment");
        DestroyPipelineLayout(_pipelineLayout);
        DestroyDescriptorSetLayout(_descriptorSetLayout);
    }

    private Pipeline CreateGraphicsPipeline(Format colorFormat)
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
                Module = _fragmentModule,
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
            ObjectType.ShaderModule,
            (nint)_fragmentModule.Handle,
            "Bork2d sprite fragment shader");
        _instance.SetObjectName(
            device,
            ObjectType.DescriptorSetLayout,
            (nint)_descriptorSetLayout.Handle,
            "Bork2d sprite descriptor set layout");
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

    private static PipelineLayout CreatePipelineLayout(
        Vk vk,
        Device device,
        DescriptorSetLayout descriptorSetLayout)
    {
        var pushConstant = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = PushConstantBytes,
        };
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorSetLayout,
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
        ShaderModule vertexModule,
        ShaderModule fragmentModule)
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

        if (fragmentModule.Handle != 0)
        {
            try
            {
                vk.DestroyShaderModule(device, fragmentModule, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan sprite fragment shader module destruction failed.");
            }
        }
    }
}
