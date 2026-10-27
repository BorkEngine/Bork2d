using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// Groups the bake/load tests in one collection. The fixture no longer builds a generated host:
/// explicit registrations replace it, so the collection carries no shared state.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BakeProjectFixtureCollection
{
    /// <summary>The collection name the bake/load tests join.</summary>
    public const string Name = "Bake project fixture";
}
