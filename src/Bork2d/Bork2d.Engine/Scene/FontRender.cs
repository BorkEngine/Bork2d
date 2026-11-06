using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Microsoft.Extensions.Logging;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned font renderable: a transform, a frozen font asset, the text to lay out, and the
/// font-specific layout state.
/// </summary>
/// <remarks>
/// <para>
/// Font renderables are scene entities with no destruction state and no global registration. They
/// are retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </para>
/// <para>
/// Assigning <see cref="Font"/> freezes its atlas reference, metrics, glyphs, and kerning once and
/// caches the laid-out quads. A cached layout is reused until <see cref="Font"/>,
/// <see cref="Text"/>, <see cref="Size"/>, <see cref="Alignment"/>, or <see cref="LineSpacing"/>
/// changes, so a warm capture and bounds read allocate nothing. Mutating the assigned asset
/// afterwards cannot change the cached layout, a bounds read, or a published snapshot.
/// </para>
/// <para>
/// The shader, color, layer, z-index, instance-uniform state, and their capture packing come from
/// <see cref="Renderable2D{TLayer}"/>. Capture reads them through
/// <see cref="Renderable2D{TLayer}.CaptureShared"/> in the same critical section as the layout.
/// </para>
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class FontRender<TLayer> : Renderable2D<TLayer>, IFontRender<TLayer>
    where TLayer : struct, Enum
{
    private readonly ILogger _logger;
    private readonly HashSet<int> _warnedMissingCodepoints = [];

    private FontAsset? _font;
    private FontRenderData? _fontData;
    private string _text;
    private float _size;
    private TextAlignment _alignment;
    private float _lineSpacing;
    private FontLayoutData _layout;
    private bool _layoutDirty;
    private bool _warnedInvalidMetrics;

    internal FontRender(Lock structuralLock, ILogger logger)
        : base(structuralLock, "font")
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _font = null;
        _fontData = null;
        _text = string.Empty;
        _size = 1f;
        _alignment = TextAlignment.Left;
        _lineSpacing = 1f;
        _layout = FontLayoutData.Empty;
        _layoutDirty = true;
    }

    /// <inheritdoc/>
    public FontAsset? Font
    {
        get
        {
            lock (Sync)
            {
                return _font;
            }
        }

        set
        {
            FontRenderData? renderData = FontRenderData.FreezeOrNull(value);
            lock (Sync)
            {
                // Re-assigning the same asset keeps the first frozen values and does not invalidate
                // the cached layout; only a different asset reference is a change.
                if (!ReferenceEquals(_font, value))
                {
                    _font = value;
                    _fontData = renderData;
                    _layoutDirty = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public string Text
    {
        get
        {
            lock (Sync)
            {
                return _text;
            }
        }

        set
        {
            string text = value ?? string.Empty;
            lock (Sync)
            {
                if (!string.Equals(_text, text, StringComparison.Ordinal))
                {
                    _text = text;
                    _layoutDirty = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public float Size
    {
        get
        {
            lock (Sync)
            {
                return _size;
            }
        }

        set
        {
            lock (Sync)
            {
                if (!_size.Equals(value))
                {
                    _size = value;
                    _layoutDirty = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public TextAlignment Alignment
    {
        get
        {
            lock (Sync)
            {
                return _alignment;
            }
        }

        set
        {
            lock (Sync)
            {
                if (_alignment != value)
                {
                    _alignment = value;
                    _layoutDirty = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public float LineSpacing
    {
        get
        {
            lock (Sync)
            {
                return _lineSpacing;
            }
        }

        set
        {
            lock (Sync)
            {
                if (!_lineSpacing.Equals(value))
                {
                    _lineSpacing = value;
                    _layoutDirty = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override Bounds Bounds
    {
        get
        {
            FontLayoutData layout;
            lock (Sync)
            {
                EnsureLayoutUnderLock();
                layout = _layout;
            }

            // The world matrix is composed under the structural lock, so read the renderable state
            // first and never hold the per-object lock while acquiring the structural lock.
            return FontBounds.CreateWorldBounds(layout, WorldMatrix());
        }
    }

    /// <summary>
    /// Copies this font renderable's capture state under one per-object lock acquisition.
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock. Capture uses this instead of the public property
    /// getters, which would acquire <see cref="Transform.Sync"/> once per field and could observe a
    /// concurrent writer between fields. The cached layout is rebuilt here only when its inputs
    /// changed since the last rebuild. The shared shader, color, layer, z-index and uniform state
    /// are read in the same critical section. The snapshot's <see cref="FontSnapshot.WorldMatrix"/>
    /// is the local matrix as a placeholder; the capture pass replaces it with the composed world
    /// matrix after this method returns and the lock is released.
    /// </remarks>
    /// <param name="snapshot">
    /// The font values with the local matrix in <see cref="FontSnapshot.WorldMatrix"/>.
    /// </param>
    /// <returns>The font's local-to-parent matrix.</returns>
    internal Matrix3x2 CaptureForSnapshot(out FontSnapshot snapshot)
    {
        lock (Sync)
        {
            var local = LocalMatrixUnderLock;
            EnsureLayoutUnderLock();
            CaptureShared(
                out ShaderRenderData? shaderData,
                out Vector4 uniform0,
                out Vector4 uniform1,
                out Vector4 uniform2,
                out Vector4 uniform3,
                out Color color,
                out uint layer,
                out int zIndex);
            snapshot = new FontSnapshot(
                _fontData,
                _layout,
                local,
                color,
                layer,
                zIndex)
            {
                ShaderData = shaderData,
                Uniform0 = uniform0,
                Uniform1 = uniform1,
                Uniform2 = uniform2,
                Uniform3 = uniform3,
            };
            return local;
        }
    }

    private void EnsureLayoutUnderLock()
    {
        if (!_layoutDirty)
        {
            return;
        }

        if (_fontData is null)
        {
            _layout = FontLayoutData.Empty;
            _layoutDirty = false;
            return;
        }

        if (!FontLayoutBuilder.TryGetLineHeight(
            _fontData.Ascent,
            _fontData.Descent,
            _fontData.LineGap,
            _size,
            _lineSpacing,
            out _))
        {
            _layout = FontLayoutData.Empty;
            _layoutDirty = false;
            if (!_warnedInvalidMetrics)
            {
                _warnedInvalidMetrics = true;
                _logger.LogWarning(
                    "FontRender has an invalid Size, LineSpacing, or font vertical metric; the "
                        + "layout is empty. This diagnostic is logged once per renderable.");
            }

            return;
        }

        string atlas = _fontData.Atlas.Path ?? "<none>";
        RebuildLayoutUnderLock(_fontData, atlas);
        _layoutDirty = false;
    }

    /// <summary>
    /// Rebuilds the layout while holding <see cref="Transform.Sync"/> after the dirty and metric
    /// checks have passed.
    /// </summary>
    /// <remarks>
    /// This helper exists so the missing-glyph callback closure is allocated only on the cold
    /// rebuild path. Keeping it out of line prevents the compiler from hoisting that allocation into
    /// <see cref="EnsureLayoutUnderLock"/> and charging it to every warm capture.
    /// </remarks>
    /// <param name="fontData">The frozen font data to lay out.</param>
    /// <param name="atlas">The atlas path used in missing-glyph diagnostics.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RebuildLayoutUnderLock(FontRenderData fontData, string atlas)
    {
        _layout = FontLayoutBuilder.Build(
            fontData.Glyphs,
            fontData.Kerning,
            fontData.Ascent,
            fontData.Descent,
            fontData.LineGap,
            _text,
            _size,
            _alignment,
            _lineSpacing,
            codepoint => WarnMissingGlyphUnderLock(codepoint, atlas));
    }

    private void WarnMissingGlyphUnderLock(int codepoint, string atlas)
    {
        if (_warnedMissingCodepoints.Add(codepoint))
        {
            _logger.LogWarning(
                "Font '{Atlas}' has no glyph for U+{Codepoint:X4} and no '?' glyph; the codepoint "
                    + "is skipped. This diagnostic is logged once per codepoint and renderable.",
                atlas,
                codepoint);
        }
    }
}
