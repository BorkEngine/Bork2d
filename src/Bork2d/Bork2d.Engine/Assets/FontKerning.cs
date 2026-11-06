namespace Bork2d.Engine.Assets;

/// <summary>
/// One nonzero kerning adjustment between two selected codepoints.
/// </summary>
/// <param name="LeftCodepoint">The codepoint of the preceding glyph.</param>
/// <param name="RightCodepoint">The codepoint of the following glyph.</param>
/// <param name="Adjustment">
/// The horizontal adjustment in em units, divided by the font's units per em. It is added to the
/// right glyph's pen position and is nonzero only when the font carries pair data for the pair.
/// </param>
/// <remarks>
/// The array in <see cref="FontAsset.Kerning"/> is sorted by
/// <c>(LeftCodepoint, RightCodepoint)</c> and carries only pairs where both codepoints are part of
/// the baked selection and the font reports a nonzero adjustment.
/// </remarks>
public readonly record struct FontKerning(
    int LeftCodepoint,
    int RightCodepoint,
    float Adjustment);
