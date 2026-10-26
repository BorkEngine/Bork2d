namespace Bork2d.Bake.Hosting;

/// <summary>
/// One non-framework assembly reference of the generated bake host.
/// </summary>
/// <param name="AssemblyName">The simple assembly name.</param>
/// <param name="Path">The absolute assembly path used as the reference's <c>HintPath</c>.</param>
internal readonly record struct BakeHostReference(string AssemblyName, string Path);
