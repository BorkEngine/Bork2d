using Bork2d.Render;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// Creates a <see cref="FakeRenderer"/> and records the start request, so host tests can assert
/// that headless never constructs a renderer and that windowed/offscreen starts carry the expected
/// opaque handles.
/// </summary>
internal sealed class FakeRendererFactory : IRendererFactory
{
    /// <summary>Initializes a new instance of the <see cref="FakeRendererFactory"/> class.</summary>
    /// <param name="renderer">The renderer to return; a fresh one when omitted.</param>
    internal FakeRendererFactory(FakeRenderer? renderer = null)
        => Renderer = renderer ?? new FakeRenderer();

    /// <summary>Gets the renderer the factory returns.</summary>
    internal FakeRenderer Renderer { get; }

    /// <summary>Gets the last start request.</summary>
    internal RendererStartInfo? StartInfo { get; private set; }

    /// <summary>Gets the number of create calls.</summary>
    internal int CreateCount { get; private set; }

    /// <summary>Gets or sets the exception <see cref="Create"/> throws instead of returning.</summary>
    internal Exception? CreateException { get; set; }

    /// <inheritdoc/>
    public IRenderer Create(RendererStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        CreateCount++;
        StartInfo = startInfo;
        if (CreateException is not null)
        {
            throw CreateException;
        }

        return Renderer;
    }
}
