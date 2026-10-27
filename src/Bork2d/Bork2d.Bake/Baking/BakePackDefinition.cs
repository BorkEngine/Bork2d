namespace Bork2d.Bake.Baking;

/// <summary>
/// One configured pack in a <see cref="BakeProjectDefinition"/>: a pack path and its asset root,
/// both relative to the project root.
/// </summary>
/// <param name="PackPath">The pack output path relative to the output root, with <c>/</c> separators.</param>
/// <param name="Root">The asset root relative to the project root, with <c>/</c> separators.</param>
internal sealed record BakePackDefinition(string PackPath, string Root);
