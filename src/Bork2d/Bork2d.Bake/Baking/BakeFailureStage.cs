namespace Bork2d.Bake.Baking;

/// <summary>
/// How far a bake pipeline run progressed before it stopped.
/// </summary>
internal enum BakeFailureStage
{
    /// <summary>The run succeeded.</summary>
    None,

    /// <summary>The output root or a pack output path is invalid.</summary>
    Configuration,

    /// <summary>A custom-data source could not be read, parsed, or bound.</summary>
    Sources,

    /// <summary>A processor source, output, required sidecar, or processor call failed.</summary>
    Processors,

    /// <summary>The asset index or a reference failed validation.</summary>
    References,

    /// <summary>The pack could not be composed or written.</summary>
    Pack,
}
