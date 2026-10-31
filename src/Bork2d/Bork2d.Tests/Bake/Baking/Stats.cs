using PolyType;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// A nested <c>readonly record struct</c> member of <see cref="GameConfig"/>, exercised on both the
/// TOML bind path and the generated-shape MessagePack path.
/// </summary>
/// <param name="Attack">The attack value.</param>
/// <param name="Defense">The defense value.</param>
[GenerateShape]
public readonly partial record struct Stats(int Attack, int Defense);
