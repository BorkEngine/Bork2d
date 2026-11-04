using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Shaders;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The internal final present pipeline: the engine-owned vertex/fragment modules, its own
/// descriptor-set layout and sampler, and one graphics pipeline per final target format.
/// </summary>
/// <remarks>
/// <para>
/// The present pass is a fullscreen triangle with no vertex buffer, no push constants, and no
/// blending: the fragment stage samples the linear scene color image exactly once per target pixel
/// and writes the sRGB-encoded color into the UNORM swapchain or offscreen target. Because it samples
/// the scene image at a one-to-one pixel mapping, the sampler is nearest with clamped edges.
/// </para>
/// <para>
/// Descriptor set <c>0</c> has one binding: <c>0</c>, a combined image sampler read by the fragment
/// stage. It is a dedicated layout, so the sprite pass's bindless set layout, page descriptors, and
/// sampler cache are untouched. The pipeline layout carries that single set and no push constant.
/// </para>
/// <para>
/// The vertex and fragment modules are loaded from the built-in pack at renderer startup and owned by
/// this object. Pipelines are cached by final target format, so a windowed swapchain format change
/// creates only the present pipeline for that format and never a new sprite pipeline variant.
/// <see cref="Destroy"/> is best-effort, idempotent, and never throws.
/// </para>
/// </remarks>
internal sealed unsafe class PresentPipeline : IDisposable
{
    /// <summary>The canonical path of the built-in present vertex SPIR-V payload.</summary>
    internal const string VertexSpirVPath = "present/vertex-spv";

    /// <summary>The canonical path of the built-in present fragment SPIR-V payload.</summary>
    internal const string FragmentSpirVPath = "present/fragment-spv";

    /// <summary>The present descriptor-set binding of the linear scene image.</summary>
    internal const uint SceneBinding = 0;

    private static readonly byte[] _entryPoint = "main\0"u8.ToArray();

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly IVulkanInstance? _instance;
    private readonly ShaderModule _vertexModule;
    private readonly ShaderModule _fragmentModule;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly Sampler _sampler;
    private readonly Dictionary<Format, Pipeline> _pipelines = [];
    private int _destroyed;

    private PresentPipeline(
        Vk vk,
        Device device,
        ILogger logger,
        IVulkanInstance? instance,
        ShaderModule vertexModule,
        ShaderModule fragmentModule,
        DescriptorSetLayout descriptorSetLayout,
        PipelineLayout pipelineLayout,
        Sampler sampler)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _instance = instance;
        _vertexModule = vertexModule;
        _fragmentModule = fragmentModule;
        _descriptorSetLayout = descriptorSetLayout;
        _pipelineLayout = pipelineLayout;
        _sampler = sampler;
    }

    /// <summary>Gets the descriptor-set layout shared by every frame's present set.</summary>
    internal DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;

    /// <summary>Gets the pipeline layout (descriptor set 0 only, no push constant).</summary>
    internal PipelineLayout PipelineLayout => _pipelineLayout;

    /// <summary>Gets the sampler the frame present descriptors bind with the scene image view.</summary>
    internal Sampler Sampler => _sampler;

    /// <summary>
    /// Loads both present modules from the built-in pack and creates the layouts, sampler, and
    /// modules. A missing or malformed module is a fatal startup failure.
    /// </summary>
    /// <param name="device">The selected device; its API object owns the created handles.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="assets">The started asset service that serves the built-in pack.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <returns>The created pipeline owner; the caller owns it.</returns>
    /// <exception cref="Bork2dException">A module is missing, not SPIR-V, or creation failed.</exception>
    internal static PresentPipeline Load(
        VulkanDevice device,
        IVulkanInstance? instance,
        IAssets assets,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(logger);

        byte[] vertexSpirV = ShaderStore.LoadSpirV(
            assets,
            VertexSpirVPath,
            "the built-in present vertex module");
        byte[] fragmentSpirV = ShaderStore.LoadSpirV(
            assets,
            FragmentSpirVPath,
            "the built-in present fragment module");
        return Create(device, instance, logger, vertexSpirV, fragmentSpirV);
    }

    /// <summary>
    /// Creates the descriptor-set layout, sampler, modules, and pipeline layout.
    /// </summary>
    /// <param name="device">The selected device; its API object owns the created handles.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <param name="vertexSpirV">The present vertex module's SPIR-V bytes.</param>
    /// <param name="fragmentSpirV">The present fragment module's SPIR-V bytes.</param>
    /// <returns>The created pipeline owner; the caller owns it.</returns>
    /// <exception cref="ArgumentException">A module is empty.</exception>
    /// <exception cref="Bork2dException">A Vulkan object could not be created.</exception>
    internal static PresentPipeline Create(
        VulkanDevice device,
        IVulkanInstance? instance,
        ILogger logger,
        byte[] vertexSpirV,
        byte[] fragmentSpirV)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(vertexSpirV);
        ArgumentNullException.ThrowIfNull(fragmentSpirV);
        if (vertexSpirV.Length == 0)
        {
            throw new ArgumentException("The present vertex SPIR-V module is empty.", nameof(vertexSpirV));
        }

        if (fragmentSpirV.Length == 0)
        {
            throw new ArgumentException(
                "The present fragment SPIR-V module is empty.",
                nameof(fragmentSpirV));
        }

        Vk vk = device.Api;
        Device logicalDevice = device.Device;
        var vertexModule = default(ShaderModule);
        var fragmentModule = default(ShaderModule);
        var descriptorSetLayout = default(DescriptorSetLayout);
        var pipelineLayout = default(PipelineLayout);
        var sampler = default(Sampler);
        try
        {
            vertexModule = CreateShaderModule(
                vk,
                logicalDevice,
                vertexSpirV,
                "present/vertex-spv");
            fragmentModule = CreateShaderModule(
                vk,
                logicalDevice,
                fragmentSpirV,
                "present/fragment-spv");
            descriptorSetLayout = CreateDescriptorSetLayout(vk, logicalDevice);
            pipelineLayout = CreatePipelineLayout(vk, logicalDevice, descriptorSetLayout);
            sampler = CreateSampler(vk, logicalDevice);

            var pipeline = new PresentPipeline(
                vk,
                logicalDevice,
                logger,
                instance,
                vertexModule,
                fragmentModule,
                descriptorSetLayout,
                pipelineLayout,
                sampler);
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
                fragmentModule,
                sampler);
            throw;
        }
    }

    /// <summary>
    /// Gets the graphics pipeline for a final target format, creating it on first use.
    /// </summary>
    /// <param name="targetFormat">The UNORM final target color format.</param>
    /// <returns>The cached pipeline for that format.</returns>
    /// <exception cref="Bork2dException">The pipeline could not be created.</exception>
    internal Pipeline EnsurePipeline(Format targetFormat)
    {
        if (_pipelines.TryGetValue(targetFormat, out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreateGraphicsPipeline(targetFormat);
        _pipelines.Add(targetFormat, pipeline);
        _instance?.SetObjectName(
            _device.Handle,
            ObjectType.Pipeline,
            (nint)pipeline.Handle,
            $"Bork2d present pipeline ({targetFormat})");
        return pipeline;
    }

    /// <summary>Looks up a cached present pipeline without creating one.</summary>
    /// <param name="targetFormat">The UNORM final target color format.</param>
    /// <returns>The cached pipeline for that format.</returns>
    /// <exception cref="Bork2dException">No pipeline exists for that format yet.</exception>
    internal Pipeline GetPipeline(Format targetFormat)
        => _pipelines.TryGetValue(targetFormat, out Pipeline pipeline)
            ? pipeline
            : throw new Bork2dException(
                $"The present pipeline for target format {targetFormat} was not created during "
                    + "target setup.");

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Destroys the pipelines, sampler, modules, and layouts; never throws.</summary>
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
        DestroySampler(_sampler);
        DestroyShaderModule(_vertexModule, "vertex");
        DestroyShaderModule(_fragmentModule, "fragment");
        DestroyPipelineLayout(_pipelineLayout);
        DestroyDescriptorSetLayout(_descriptorSetLayout);
    }

    private Pipeline CreateGraphicsPipeline(Format targetFormat)
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
                Topology = PrimitiveTopology.TriangleList,
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
                BlendEnable = false,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.Zero,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
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
            colorFormats[0] = targetFormat;
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
                    $"vkCreateGraphicsPipelines failed for the present pipeline with target format "
                        + $"{targetFormat} and VkResult {result}.");
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
            "Bork2d present vertex shader");
        _instance.SetObjectName(
            device,
            ObjectType.ShaderModule,
            (nint)_fragmentModule.Handle,
            "Bork2d present fragment shader");
        _instance.SetObjectName(
            device,
            ObjectType.DescriptorSetLayout,
            (nint)_descriptorSetLayout.Handle,
            "Bork2d present descriptor set layout");
        _instance.SetObjectName(
            device,
            ObjectType.PipelineLayout,
            (nint)_pipelineLayout.Handle,
            "Bork2d present pipeline layout");
        _instance.SetObjectName(
            device,
            ObjectType.Sampler,
            (nint)_sampler.Handle,
            "Bork2d present sampler");
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

    private static DescriptorSetLayout CreateDescriptorSetLayout(Vk vk, Device device)
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = SceneBinding,
            DescriptorType = DescriptorType.CombinedImageSampler,
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
                $"vkCreateDescriptorSetLayout failed for the present descriptor set with VkResult "
                    + $"{result}.");
        }

        return layout;
    }

    private static PipelineLayout CreatePipelineLayout(
        Vk vk,
        Device device,
        DescriptorSetLayout descriptorSetLayout)
    {
        DescriptorSetLayout* setLayouts = stackalloc DescriptorSetLayout[1];
        setLayouts[0] = descriptorSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 0,
            PPushConstantRanges = null,
        };
        Result result = vk.CreatePipelineLayout(
            device,
            in layoutInfo,
            null,
            out PipelineLayout layout);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreatePipelineLayout failed for the present pipeline with VkResult {result}.");
        }

        return layout;
    }

    private static Sampler CreateSampler(Vk vk, Device device)
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
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
        Result result = vk.CreateSampler(device, in info, null, out Sampler sampler);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateSampler failed for the present sampler with VkResult {result}.");
        }

        return sampler;
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
            _logger.LogError(exception, "Vulkan present pipeline destruction failed.");
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
            _logger.LogError(exception, "Vulkan present sampler destruction failed.");
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
                "Vulkan present {Stage} shader module destruction failed.",
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
            _logger.LogError(exception, "Vulkan present pipeline-layout destruction failed.");
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
            _logger.LogError(exception, "Vulkan present descriptor-set-layout destruction failed.");
        }
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        PipelineLayout pipelineLayout,
        DescriptorSetLayout descriptorSetLayout,
        ShaderModule vertexModule,
        ShaderModule fragmentModule,
        Sampler sampler)
    {
        if (sampler.Handle != 0)
        {
            try
            {
                vk.DestroySampler(device, sampler, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan present sampler destruction failed.");
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
                logger.LogError(
                    exception,
                    "Vulkan present vertex shader module destruction failed.");
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
                logger.LogError(
                    exception,
                    "Vulkan present fragment shader module destruction failed.");
            }
        }

        if (pipelineLayout.Handle != 0)
        {
            try
            {
                vk.DestroyPipelineLayout(device, pipelineLayout, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan present pipeline-layout destruction failed.");
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
                logger.LogError(
                    exception,
                    "Vulkan present descriptor-set-layout destruction failed.");
            }
        }
    }
}
