using System.Runtime.CompilerServices;
using Bork2d.Api;

namespace Bork2d.Common;

/// <summary>
/// Validates the game's layer enum and derives its full layer mask.
/// </summary>
/// <remarks>
/// <para>
/// A valid layer type is an <see cref="int"/>-backed <see cref="FlagsAttribute"/> enum with at
/// least one named non-zero single-bit value. <c>None = 0</c> is allowed; composite non-zero
/// values are rejected. The rules are checked once per closed generic type and cached, with no
/// runtime reflection-based serialization or assembly scanning.
/// </para>
/// <para>
/// Invalid layer types fail fast with <see cref="Bork2dException"/> naming the type and the
/// reason.
/// </para>
/// </remarks>
internal static class LayerValidation
{
    private const int MaxLayerCount = 32;

    /// <summary>
    /// Gets a value indicating whether <typeparamref name="TLayer"/> is a valid layer enum.
    /// </summary>
    /// <typeparam name="TLayer">The candidate layer enum.</typeparam>
    /// <returns><see langword="true"/> when the type is valid.</returns>
    internal static bool IsValid<TLayer>()
        where TLayer : struct, Enum
        => LayerInfo<TLayer>.Error is null;

    /// <summary>
    /// Validates <typeparamref name="TLayer"/> and throws when it is not a valid layer enum.
    /// </summary>
    /// <typeparam name="TLayer">The layer enum to validate.</typeparam>
    /// <exception cref="Bork2dException">The type is not a valid layer enum.</exception>
    internal static void Validate<TLayer>()
        where TLayer : struct, Enum
    {
        if (LayerInfo<TLayer>.Error is { } error)
        {
            throw new Bork2dException(error);
        }
    }

    /// <summary>
    /// Gets the mask containing every declared non-zero layer flag.
    /// </summary>
    /// <typeparam name="TLayer">The layer enum.</typeparam>
    /// <returns>The bitwise OR of all declared non-zero single-bit values.</returns>
    /// <exception cref="Bork2dException">The type is not a valid layer enum.</exception>
    internal static TLayer All<TLayer>()
        where TLayer : struct, Enum
    {
        Validate<TLayer>();
        var flags = LayerInfo<TLayer>.AllFlags;
        return Unsafe.As<int, TLayer>(ref flags);
    }

    private static string Invalid(Type type, string reason)
    {
        return $"Layer enum '{type.FullName ?? type.Name}' is invalid: {reason}. A layer type must be "
            + $"an Int32-backed [Flags] enum with 1-{MaxLayerCount} named single-bit values; "
            + "None = 0 is allowed.";
    }

    private static int ToInt32<TLayer>(TLayer value)
        where TLayer : struct, Enum
        => Unsafe.As<TLayer, int>(ref value);

    private static class LayerInfo<TLayer>
        where TLayer : struct, Enum
    {
        internal static readonly int AllFlags;
        internal static readonly string? Error;

        static LayerInfo()
        {
            var type = typeof(TLayer);

            if (!type.IsDefined(typeof(FlagsAttribute), inherit: false))
            {
                Error = Invalid(type, "it is missing [Flags]");
                return;
            }

            var underlyingType = Enum.GetUnderlyingType(type);
            if (underlyingType != typeof(int))
            {
                Error = Invalid(type, $"its underlying type is {underlyingType.Name}, not Int32");
                return;
            }

            var flags = 0;
            var count = 0;
            foreach (var value in Enum.GetValues<TLayer>())
            {
                var bits = ToInt32(value);
                if (bits == 0)
                {
                    continue;
                }

                if ((bits & (bits - 1)) != 0)
                {
                    Error = Invalid(type, $"the value 0x{bits:X8} is composite, not a single bit");
                    return;
                }

                flags |= bits;
                count++;
            }

            if (count == 0)
            {
                Error = Invalid(type, "it declares no non-zero single-bit value");
                return;
            }

            AllFlags = flags;
        }
    }
}
