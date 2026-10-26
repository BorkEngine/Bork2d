using System.ComponentModel;
using PolyType;

namespace Bork2d.Api.Assets;

/// <summary>
/// Serializes <see cref="DataRef"/> as its canonical path string.
/// </summary>
/// <remarks>
/// This type is public because PolyType needs it when generating shapes in other assemblies. It is
/// infrastructure, not a game-facing API.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DataRefMarshaler : IMarshaler<DataRef, string?>
{
    /// <inheritdoc/>
    public string? Marshal(DataRef value) => value.Path;

    /// <inheritdoc/>
    public DataRef Unmarshal(string? surrogate) => new(surrogate);
}
