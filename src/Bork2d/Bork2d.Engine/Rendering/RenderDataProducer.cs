using System.Numerics;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Base class for a typed render-data producer: one runtime type check, then a typed capture.
/// </summary>
/// <remarks>
/// The base performs the single <c>is</c> check per node, so a producer is never asked to capture a
/// node of another renderable type. The derived capture method reads the node under one per-object
/// lock acquisition, writes the typed record through <see cref="FrameSnapshot.GetBuffer{T}"/>, and
/// returns the world matrix it composed from the local and parent matrices.
/// </remarks>
/// <typeparam name="TNode">The renderable node type this producer captures.</typeparam>
internal abstract class RenderDataProducer<TNode> : IRenderDataProducer
    where TNode : Transform
{
    private int _slot = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderDataProducer{TNode}"/> class.
    /// </summary>
    /// <param name="name">The short diagnostic name.</param>
    protected RenderDataProducer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int Slot => _slot;

    /// <inheritdoc/>
    public void AssignSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (_slot >= 0 && _slot != slot)
        {
            throw new InvalidOperationException(
                $"Renderable producer '{Name}' is already assigned render-data slot {_slot}.");
        }

        _slot = slot;
    }

    /// <inheritdoc/>
    public bool TryCapture(
        Transform node,
        in Matrix3x2 parentWorld,
        FrameSnapshot snapshot,
        out Matrix3x2 world)
    {
        if (node is not TNode typed)
        {
            world = default;
            return false;
        }

        return Capture(typed, in parentWorld, snapshot, out world);
    }

    /// <summary>
    /// Captures a node of the producer's renderable type into its typed snapshot buffer.
    /// </summary>
    /// <param name="node">The typed renderable node.</param>
    /// <param name="parentWorld">The node's evaluated parent world matrix.</param>
    /// <param name="snapshot">The slot-owned snapshot being filled.</param>
    /// <param name="world">The node's composed world matrix.</param>
    /// <returns><see langword="true"/> when the node produced render data.</returns>
    protected abstract bool Capture(
        TNode node,
        in Matrix3x2 parentWorld,
        FrameSnapshot snapshot,
        out Matrix3x2 world);
}
