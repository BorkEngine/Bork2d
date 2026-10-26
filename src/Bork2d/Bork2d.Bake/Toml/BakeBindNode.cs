namespace Bork2d.Bake.Toml;

/// <summary>
/// One value being bound: the TOML DOM value, its segment-aware path (for spans), and the member
/// path relative to the asset root (for diagnostics and reference sites).
/// </summary>
/// <remarks>
/// The path is a value type so recursive binding cannot leak a parent path into a sibling. Array
/// elements and dictionary values keep their index or key in both representations, which is what
/// makes a nested reference diagnostic point at <c>items[2].icon</c> rather than at the table.
/// </remarks>
internal readonly record struct BakeBindNode
{
    /// <summary>Initializes a new instance of the <see cref="BakeBindNode"/> struct.</summary>
    /// <param name="value">The TOML DOM value.</param>
    /// <param name="path">The segment-aware TOML path.</param>
    /// <param name="memberPath">The member path relative to the asset root.</param>
    internal BakeBindNode(object? value, TomlPath path, string memberPath)
    {
        Value = value;
        Path = path;
        MemberPath = memberPath;
    }

    /// <summary>Gets the TOML DOM value.</summary>
    internal object? Value { get; }

    /// <summary>Gets the segment-aware TOML path used for span lookups.</summary>
    internal TomlPath Path { get; }

    /// <summary>Gets the member path relative to the asset root.</summary>
    internal string MemberPath { get; }

    /// <summary>Creates the root node for a parsed document.</summary>
    /// <param name="root">The parsed TOML table.</param>
    /// <returns>The root node.</returns>
    internal static BakeBindNode Root(object root) => new(root, TomlPath.Root, string.Empty);

    /// <summary>Creates a child node for a table key.</summary>
    /// <param name="key">The decoded key segment.</param>
    /// <param name="value">The TOML DOM value.</param>
    /// <returns>The child node.</returns>
    internal BakeBindNode Child(string key, object? value)
        => new(
            value,
            Path.Child(key),
            MemberPath.Length == 0 ? key : MemberPath + "." + key);

    /// <summary>Creates a child node for an array element.</summary>
    /// <param name="index">The zero-based element index.</param>
    /// <param name="value">The TOML DOM value.</param>
    /// <returns>The element node.</returns>
    internal BakeBindNode Element(int index, object? value)
        => new(
            value,
            Path.Element(index),
            MemberPath + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");

    /// <summary>Creates a child node for a dictionary value.</summary>
    /// <param name="key">The dictionary key.</param>
    /// <param name="value">The TOML DOM value.</param>
    /// <returns>The value node.</returns>
    internal BakeBindNode DictionaryValue(string key, object? value)
        => new(value, Path.Child(key), MemberPath + "[" + key + "]");
}
