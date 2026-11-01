using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// Describes one renderable kind: how to create its scene node and how to capture it.
/// </summary>
/// <remarks>
/// A definition is the single place a renderable type is registered. Creation goes through
/// <see cref="Create"/>, which builds the node and attaches it to the parent, and capture goes
/// through <see cref="Producer"/>, which writes the node's typed record into its snapshot slot.
/// Adding a renderable therefore needs no edit to a central capture switch or to the engine's
/// explicit creation methods.
/// </remarks>
internal interface IRenderableDefinition
{
    /// <summary>
    /// Gets the exact runtime node type this definition creates and its producer captures.
    /// </summary>
    Type NodeType { get; }

    /// <summary>
    /// Gets the producer that captures this renderable kind.
    /// </summary>
    IRenderDataProducer Producer { get; }

    /// <summary>
    /// Creates the renderable node and attaches it as a new child of <paramref name="parent"/>.
    /// </summary>
    /// <param name="parent">The engine-owned parent to attach to.</param>
    /// <returns>The created and attached node.</returns>
    Transform Create(ISceneNode parent);
}
