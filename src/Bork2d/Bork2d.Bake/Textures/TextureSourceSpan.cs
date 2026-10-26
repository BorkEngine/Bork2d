namespace Bork2d.Bake.Textures;

/// <summary>
/// A 1-based source location inside a texture sidecar.
/// </summary>
/// <param name="FilePath">
/// The sidecar path in the same display form <c>BakeDiagnostic.FilePath</c> uses (project-relative
/// when under the project directory, otherwise absolute).
/// </param>
/// <param name="Line">The 1-based line of the setting value.</param>
/// <param name="Column">The 1-based column of the setting value.</param>
/// <remarks>
/// The pipeline keeps the span so a later consumer (WP4 pack orchestration or M5 editor) can point
/// at the sidecar setting that produced a descriptor's sampler state without re-parsing the file.
/// </remarks>
internal readonly record struct TextureSourceSpan(string FilePath, int Line, int Column);
