namespace Bork2d.Bake.Processors;

/// <summary>
/// The processor that owns a source file and the normalized extension it matched.
/// </summary>
/// <param name="Processor">The owning processor.</param>
/// <param name="Extension">
/// The matched extension, normalized to lowercase without a leading dot; a compound extension keeps
/// its inner dots (for example <c>tar.gz</c>).
/// </param>
internal readonly record struct BakeProcessorMatch(
    BakeProcessorRegistration Processor,
    string Extension);
