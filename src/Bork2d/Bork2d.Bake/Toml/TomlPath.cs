using System.Globalization;

namespace Bork2d.Bake.Toml;

/// <summary>
/// A segment-aware key path into a TOML document, used to look up syntax spans.
/// </summary>
/// <remarks>
/// Segments are length-prefixed so a quoted key containing dots, a separator character, or an
/// array marker can never collide with another path. Array elements are addressed with a
/// <c>#index</c> suffix on the last segment.
/// </remarks>
internal readonly record struct TomlPath
{
    private TomlPath(string key) => Key = key;

    /// <summary>Gets the encoded dictionary key.</summary>
    internal string Key { get; }

    /// <summary>Gets the root path.</summary>
    internal static TomlPath Root => new(string.Empty);

    /// <summary>Returns a child path for a key segment.</summary>
    /// <param name="name">The decoded key segment.</param>
    /// <returns>The child path.</returns>
    internal TomlPath Child(string name)
        => new(Key + name.Length.ToString(CultureInfo.InvariantCulture) + ":" + name);

    /// <summary>Returns an element path for an array item.</summary>
    /// <param name="index">The zero-based element index.</param>
    /// <returns>The element path.</returns>
    internal TomlPath Element(int index)
        => new(Key + "#" + index.ToString(CultureInfo.InvariantCulture));
}
