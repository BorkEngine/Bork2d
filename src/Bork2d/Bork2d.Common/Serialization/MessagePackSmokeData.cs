using Bork2d.Api.Rendering;
using PolyType;

namespace Bork2d.Common.Serialization;

/// <summary>
/// The internal data type used to prove the Nerdbank.MessagePack source-generated shape path
/// without a reflection resolver.
/// </summary>
/// <remarks>
/// <para>
/// This type exists only for the generated-shape smoke: <see cref="MessagePackSmokeSerializer"/>
/// round-trips it in the AOT smoke app and in <c>Bork2d.Tests</c>. It is not a pack, asset, or
/// game data type; the pack/asset serialization models arrive with M3/M4 and will replace this
/// placeholder policy check.
/// </para>
/// <para>
/// The type is <c>internal</c> and the shape is generated into this assembly, so nothing about
/// the serializer becomes public. Only the top-level type carries <c>[GenerateShape]</c>;
/// referenced types receive generated shapes transitively.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class MessagePackSmokeData
{
    /// <summary>Gets or sets the smoke document name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the smoke document value.</summary>
    public int Value { get; set; }

    /// <summary>Gets or sets the smoke document scale.</summary>
    public float Scale { get; set; }

    /// <summary>
    /// Gets or sets a linear color. It is serialized by <c>ColorMarshaler</c> as
    /// <c>[r, g, b, a]</c>, not as an object map.
    /// </summary>
    public Color Tint { get; set; }
}
