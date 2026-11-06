namespace Bork2d.Bake.Fonts;

/// <summary>The severity of one font bake diagnostic.</summary>
internal enum FontDiagnosticSeverity
{
    /// <summary>The bake continues and still emits its outputs.</summary>
    Info = 0,

    /// <summary>The bake fails and emits no outputs.</summary>
    Error = 1,
}
