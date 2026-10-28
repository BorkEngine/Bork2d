using PolyType;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// A top-level <c>readonly record struct</c> asset root (D-M4-11): baked as custom data, loaded with
/// <c>LoadAsset&lt;T&gt;</c>, and targeted by <c>AssetRef&lt;StatsRoot&gt;</c>.
/// </summary>
/// <param name="Attack">The attack value.</param>
/// <param name="Defense">The defense value.</param>
[GenerateShape]
public readonly partial record struct StatsRoot(int Attack, int Defense);
