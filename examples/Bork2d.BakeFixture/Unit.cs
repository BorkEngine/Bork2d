using PolyType;

namespace Bork2d.BakeFixture;

/// <summary>
/// The fixture's processor-emitted data type: a <c>readonly record struct</c> produced by the Unit
/// acceptance processor from a JSON source and loaded with <c>LoadAsset&lt;Unit&gt;</c>.
/// </summary>
/// <remarks>
/// The type is public and carries <c>[GenerateShape]</c> on a <c>partial</c> declaration, so the
/// engine serializes it with its compiled shape. It lives in the game-side fixture (the stand-in for a
/// game's <c>Project.Common</c>) so the runtime can load it, and it needs no
/// <c>RegisterAsset&lt;Unit&gt;()</c> entry: processor outputs are written through
/// <c>AddAsset&lt;T&gt;</c>, which carries the shape at compile time.
/// </remarks>
/// <param name="Name">The unit name, defaulted from the source file stem or the sidecar.</param>
/// <param name="Health">The unit's health.</param>
/// <param name="Attack">The unit's attack value.</param>
[GenerateShape]
public readonly partial record struct Unit(string Name, int Health, int Attack);
