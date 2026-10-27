using Bork2d.Bake.Baking;

namespace Bork2d.Bake.Builder.Commands;

/// <summary>
/// The parsed invocation options shared by the assets commands.
/// </summary>
internal sealed class CommandOptions
{
    /// <summary>Gets or sets the <c>--project-root</c> override.</summary>
    internal string? ProjectRoot { get; set; }

    /// <summary>Gets or sets the <c>--output-root</c> value.</summary>
    internal string? OutputRoot { get; set; }

    /// <summary>Gets or sets the <c>--mode</c> value.</summary>
    internal BakeMode Mode { get; set; } = BakeMode.Debug;

    /// <summary>Gets or sets a value indicating whether <c>--check</c> was passed.</summary>
    internal bool Check { get; set; }

    /// <summary>Gets or sets a value indicating whether <c>--force</c> was passed.</summary>
    internal bool Force { get; set; }

    /// <summary>Gets or sets a value indicating whether <c>--verbose</c> was passed.</summary>
    internal bool Verbose { get; set; }

    /// <summary>Gets or sets a value indicating whether <c>--quiet</c> was passed.</summary>
    internal bool Quiet { get; set; }
}
