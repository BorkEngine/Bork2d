using Bork2d.Bake.Diagnostics;
using PolyType;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// The host-load shape check for mapped roots. Generated host code calls the generic method once
/// per distinct mapped type, so no member reflection is involved.
/// </summary>
/// <remarks>
/// A custom-data root must be a class or struct, which PolyType exposes as an
/// <see cref="TypeShapeKind.Object"/> shape. A mapped root that resolves to a collection,
/// dictionary, optional, surrogate, enum, union, or function shape is an unsupported host shape and
/// fails with a <c>BK1xxx</c> diagnostic at load time instead of failing deep inside binding.
/// </remarks>
internal static class BakeShapeValidator
{
    /// <summary>Validates that a mapped root has a supported shape kind.</summary>
    /// <typeparam name="T">The mapped root type.</typeparam>
    /// <param name="typeName">The mapped type's full name, used in the diagnostic.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    internal static void ValidateRoot<T>(string typeName, BakeDiagnostics diagnostics)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ITypeShape<T> shape = T.GetTypeShape();
        if (shape.Kind == TypeShapeKind.Object)
        {
            return;
        }

        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.HostShapeUnsupported,
            $"Mapped type '{typeName}' has the unsupported root shape kind "
                + $"'{shape.Kind}'; a custom-data root must be a class or struct.",
            assetPath: typeName);
    }
}
