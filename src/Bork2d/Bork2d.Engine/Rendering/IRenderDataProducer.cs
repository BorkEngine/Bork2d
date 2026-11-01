using System.Numerics;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Captures one renderable kind from the scene into its typed snapshot buffer during a capture pass.
/// </summary>
/// <remarks>
/// <para>
/// Producers are registered in <see cref="SnapshotCapture"/> order and tried for every traversed
/// node. The first producer that recognizes the node's type captures it and supplies the node's
/// world matrix; when none matches, the capture pass falls back to the plain transform path. The
/// engine's camera and sprite renderables are producers too, so capture has no central type switch.
/// </para>
/// <para>
/// A producer owns exactly one render-data slot. Slots are assigned by the renderable registry at
/// construction; the camera and sprite producers occupy the snapshot's reserved slots and a custom
/// producer uses a slot at or above <see cref="FrameSnapshot.ReservedSlotCount"/>.
/// </para>
/// </remarks>
internal interface IRenderDataProducer
{
    /// <summary>
    /// Gets a short diagnostic name for this producer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the render-data slot this producer writes into.
    /// </summary>
    int Slot { get; }

    /// <summary>
    /// Assigns the render-data slot. Called once by the renderable registry.
    /// </summary>
    /// <param name="slot">The non-negative render-data slot.</param>
    void AssignSlot(int slot);

    /// <summary>
    /// Captures <paramref name="node"/> when this producer's renderable type matches.
    /// </summary>
    /// <remarks>
    /// The capture pass holds the structural lock while calling this. A matching producer takes the
    /// node's per-object lock exactly once, writes its typed record, and composes the node's world
    /// matrix; a non-matching producer touches nothing.
    /// </remarks>
    /// <param name="node">The traversed scene node.</param>
    /// <param name="parentWorld">The node's evaluated parent world matrix.</param>
    /// <param name="snapshot">The slot-owned snapshot being filled.</param>
    /// <param name="world">
    /// The node's composed world matrix when this producer matched; otherwise the default value.
    /// </param>
    /// <returns><see langword="true"/> when the producer captured the node.</returns>
    bool TryCapture(
        Transform node,
        in Matrix3x2 parentWorld,
        FrameSnapshot snapshot,
        out Matrix3x2 world);
}
