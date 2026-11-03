using PolyType;

namespace Bork2d.Bake.Toml;

/// <summary>
/// A TOML leaf that accepts either one number or an array of numbers.
/// </summary>
/// <remarks>
/// <para>
/// The shape binder has no union leaf for "scalar or array", so a shader uniform default uses this
/// explicit leaf instead. A scalar number becomes a one-component value and an array keeps its
/// components in source order; anything else is the normal <c>BK2004</c> type-mismatch diagnostic.
/// The leaf is build-tooling-only: no engine type, sidecar wire format, or runtime path depends on
/// it.
/// </para>
/// <para>
/// The leaf knows nothing about the declared uniform type. The shader processor validates the
/// component count and finiteness against that type, so the leaf stays a plain number list.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class TomlNumberOrArray
{
    /// <summary>Initializes a new instance of the <see cref="TomlNumberOrArray"/> class.</summary>
    /// <param name="values">The component values in source order; a scalar has exactly one.</param>
    internal TomlNumberOrArray(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    /// <summary>Gets the component values in source order.</summary>
    internal IReadOnlyList<double> Values { get; }
}
