using Bork2d.Engine;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Creates the production Vulkan renderer (windowed or offscreen); the host or tests inject the
/// factory.
/// </summary>
/// <remarks>
/// The factory validates the start request synchronously so a missing windowed instance/surface or
/// an invalid offscreen extent fails before a thread exists. It never starts the renderer and never
/// touches the instance. A supplied instance factory creates the offscreen instance; when omitted,
/// the production <see cref="VulkanInstanceFactory"/> is created only for an offscreen start.
/// </remarks>
internal sealed class VulkanRendererFactory : IRendererFactory
{
    private readonly IVulkanInstanceFactory? _instanceFactory;

    /// <summary>Initializes a new instance of the <see cref="VulkanRendererFactory"/> class.</summary>
    internal VulkanRendererFactory()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VulkanRendererFactory"/> class.</summary>
    /// <param name="instanceFactory">Creates an offscreen renderer's owned instance.</param>
    internal VulkanRendererFactory(IVulkanInstanceFactory instanceFactory)
    {
        ArgumentNullException.ThrowIfNull(instanceFactory);
        _instanceFactory = instanceFactory;
    }

    /// <inheritdoc/>
    public IRenderer Create(RendererStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        return startInfo.Mode switch
        {
            RendererMode.Windowed => CreateWindowed(startInfo),
            RendererMode.Offscreen => CreateOffscreen(startInfo),
            _ => throw new Bork2dException(
                $"The Vulkan renderer does not support mode {startInfo.Mode}."),
        };
    }

    private static VulkanRenderer CreateWindowed(RendererStartInfo startInfo)
    {
        if (startInfo.Instance is null)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-null Vulkan instance.");
        }

        if (startInfo.Instance.Handle == 0)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-zero Vulkan instance handle.");
        }

        if (startInfo.SurfaceHandle == 0)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-zero surface handle.");
        }

        return new VulkanRenderer(startInfo);
    }

    private VulkanOffscreenRenderer CreateOffscreen(RendererStartInfo startInfo)
    {
        if (startInfo.Width <= 0 || startInfo.Height <= 0)
        {
            throw new Bork2dException(
                "Offscreen Vulkan rendering requires a positive target extent; got "
                    + $"{startInfo.Width}x{startInfo.Height}.");
        }

        return new VulkanOffscreenRenderer(
            startInfo,
            _instanceFactory ?? new VulkanInstanceFactory());
    }
}
