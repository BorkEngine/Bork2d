using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The engine's single renderable-type registry: definition lookup for creation and the ordered
/// producer list for snapshot capture.
/// </summary>
/// <remarks>
/// <para>
/// Definitions are registered in declaration order. The first <c>reservedSlots</c> definitions fill
/// the snapshot's reserved render-data slots (<c>0</c> camera, <c>1</c> sprite) in that order; any
/// later definition gets the next contiguous slot starting at the reserved count. The engine
/// registers the camera and sprite definitions first and passes the resulting
/// <see cref="IRenderDataProducer"/> list to <see cref="SnapshotCapture"/>.
/// </para>
/// <para>
/// A custom renderable kind is added by registering one definition; creation and capture then run
/// through this registry instead of a central type switch or new engine creation method. The
/// renderer still needs a matching render pass, which is outside this registry's scope.
/// </para>
/// </remarks>
internal sealed class RenderableRegistry
{
    private readonly Dictionary<Type, IRenderableDefinition> _definitions = new();
    private readonly List<IRenderDataProducer> _producers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderableRegistry"/> class.
    /// </summary>
    /// <param name="reservedSlots">
    /// The number of snapshot-reserved slots the leading definitions fill. The next definition
    /// starts at this count.
    /// </param>
    /// <param name="definitions">The definitions in slot order; the core ones come first.</param>
    /// <exception cref="ArgumentException">
    /// Fewer definitions are supplied than the reserved-slot count.
    /// </exception>
    /// <exception cref="Bork2dException">Two definitions claim the same node type.</exception>
    internal RenderableRegistry(int reservedSlots, params IRenderableDefinition[] definitions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reservedSlots);
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Length < reservedSlots)
        {
            throw new ArgumentException(
                $"The first {reservedSlots} definitions must fill the reserved render-data slots; "
                    + $"only {definitions.Length} were supplied.",
                nameof(definitions));
        }

        for (var index = 0; index < definitions.Length; index++)
        {
            IRenderableDefinition definition = definitions[index];
            ArgumentNullException.ThrowIfNull(definition);
            if (!_definitions.TryAdd(definition.NodeType, definition))
            {
                throw new Bork2dException(
                    $"A renderable definition for '{definition.NodeType.FullName}' is already "
                        + "registered.");
            }

            IRenderDataProducer producer = definition.Producer;
            producer.AssignSlot(index);
            _producers.Add(producer);
        }

        ReservedSlots = reservedSlots;
    }

    /// <summary>
    /// Gets the number of snapshot-reserved render-data slots the leading definitions fill.
    /// </summary>
    internal int ReservedSlots { get; }

    /// <summary>
    /// Gets the registered producers in capture order, with their assigned slots.
    /// </summary>
    internal IReadOnlyList<IRenderDataProducer> Producers => _producers;

    /// <summary>
    /// Creates the renderable whose exact type is <paramref name="nodeType"/> and attaches it to
    /// <paramref name="parent"/>.
    /// </summary>
    /// <param name="nodeType">The exact runtime node type of a registered definition.</param>
    /// <param name="parent">The engine-owned parent to attach to.</param>
    /// <returns>The created and attached node.</returns>
    /// <exception cref="Bork2dException">No definition is registered for the type.</exception>
    internal Transform Create(Type nodeType, ISceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(nodeType);
        ArgumentNullException.ThrowIfNull(parent);

        if (!_definitions.TryGetValue(nodeType, out IRenderableDefinition? definition))
        {
            throw new Bork2dException(
                $"No renderable definition is registered for '{nodeType.FullName}'.");
        }

        return definition.Create(parent);
    }
}
