namespace Bork2d.Bake.Baking;

/// <summary>
/// How far a bake pipeline run progressed before it stopped.
/// </summary>
internal enum BakeFailureStage
{
    /// <summary>The run succeeded.</summary>
    None,

    /// <summary>The project descriptor could not be parsed or is invalid.</summary>
    Descriptor,

    /// <summary>The generated host could not be loaded or does not match the descriptor.</summary>
    Host,

    /// <summary>A custom-data source could not be read, parsed, or bound.</summary>
    Sources,

    /// <summary>A texture source, sidecar, or payload failed.</summary>
    Textures,

    /// <summary>The asset index or a reference failed validation.</summary>
    References,

    /// <summary>The pack could not be composed or written.</summary>
    Pack,
}
