using Nerdbank.MessagePack;
using PolyType;

namespace Bork2d.Assets;

/// <summary>
/// The v1 pack header: a MessagePack object with integer keys.
/// </summary>
/// <remarks>
/// <para>
/// Only this top-level type carries <c>[GenerateShape]</c>. <see cref="PackEntry"/> receives a
/// generated shape transitively, which keeps the pack schema free of a reflection resolver.
/// </para>
/// <para>
/// The <c>required</c> members satisfy the Nerdbank <c>NBMsgPack110</c> rule: a missing header or
/// entry path is a deserialization error instead of a silent initializer default.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class PackHeader
{
    /// <summary>Gets the pack entries; may be empty but must not be null.</summary>
    [Key(0)]
    public required PackEntry[] Entries { get; init; }
}
