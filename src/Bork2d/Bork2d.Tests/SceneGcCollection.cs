using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// Serializes the scene lifetime tests. They force full collections and then observe weak
/// references, so they must not overlap with other test classes.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SceneGcCollection
{
    public const string Name = "Scene GC";
}
