using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned line renderable: a transform plus an open or closed polyline and draw state.
/// </summary>
/// <remarks>
/// <para>
/// Line renderables are scene entities with no destruction state and no global registration. They
/// are retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </para>
/// <para>
/// Assigning <see cref="Points"/>, <see cref="Width"/>, or <see cref="Closed"/> marks the cached
/// local-space geometry for a rebuild on the next capture or bounds read. The immutable
/// <see cref="Rendering.LineGeometryData"/> is reused until one of those inputs changes, so a warm
/// capture allocates nothing and color, anti-aliasing, shader, layer, z-index, or transform changes
/// never rebuild it.
/// </para>
/// <para>
/// <see cref="Shader"/> follows the sprite freeze rule for its declarations.
/// <see cref="SetUniform(string, float)"/> validates immediately against the currently assigned
/// shader's frozen metadata and stores accepted values in a per-renderable set;
/// <see cref="ResetUniforms"/> clears them so the shader defaults apply again. Reassigning the
/// shader keeps stored values, and capture applies only those whose name and type still match the
/// new frozen declarations.
/// </para>
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class LineRender<TLayer> : Transform, ILineRender<TLayer>
    where TLayer : struct, Enum
{
    private readonly InstanceUniformSet _uniforms = new();

    private Vector2[] _points;
    private ReadOnlyCollection<Vector2> _pointsView;
    private LineGeometryData? _geometry;
    private bool _geometryDirty;
    private ShaderAsset? _shader;
    private ShaderRenderData? _shaderData;
    private bool _closed;
    private float _width;
    private Color _color;
    private bool _antiAlias;
    private TLayer _layer;
    private int _zIndex;

    internal LineRender(Lock structuralLock)
        : base(structuralLock)
    {
        _points = [];
        _pointsView = Array.AsReadOnly(_points);
        _geometry = null;
        _geometryDirty = true;
        _shader = null;
        _shaderData = null;
        _closed = false;
        _width = 1f;
        _color = Color.White;
        _antiAlias = true;
        _layer = default;
        _zIndex = 0;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Vector2> Points
    {
        get
        {
            lock (Sync)
            {
                return _pointsView;
            }
        }

        set
        {
            // Copy outside the lock: a caller-owned list must never stay aliased, and the cached
            // read-only view is published with the copy it wraps.
            Vector2[] copy = CopyPoints(value);
            ReadOnlyCollection<Vector2> view = Array.AsReadOnly(copy);
            lock (Sync)
            {
                _points = copy;
                _pointsView = view;
                _geometryDirty = true;
            }
        }
    }

    /// <inheritdoc/>
    public bool Closed
    {
        get
        {
            lock (Sync)
            {
                return _closed;
            }
        }

        set
        {
            lock (Sync)
            {
                if (_closed != value)
                {
                    _closed = value;
                    _geometryDirty = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public float Width
    {
        get
        {
            lock (Sync)
            {
                return _width;
            }
        }

        set
        {
            lock (Sync)
            {
                if (_width != value)
                {
                    _width = value;
                    _geometryDirty = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public Color Color
    {
        get
        {
            lock (Sync)
            {
                return _color;
            }
        }

        set
        {
            lock (Sync)
            {
                _color = value;
            }
        }
    }

    /// <inheritdoc/>
    public bool AntiAlias
    {
        get
        {
            lock (Sync)
            {
                return _antiAlias;
            }
        }

        set
        {
            lock (Sync)
            {
                _antiAlias = value;
            }
        }
    }

    /// <inheritdoc/>
    public ShaderAsset? Shader
    {
        get
        {
            lock (Sync)
            {
                return _shader;
            }
        }

        set
        {
            // Freeze outside the lock: a malformed asset must not throw from this setter, and the
            // conditional weak table publishes exactly one copy for concurrent assignments.
            ShaderRenderData? shaderData = ShaderRenderData.FreezeOrNull(value);
            lock (Sync)
            {
                _shader = value;
                _shaderData = shaderData;
            }
        }
    }

    /// <inheritdoc/>
    public TLayer Layer
    {
        get
        {
            lock (Sync)
            {
                return _layer;
            }
        }

        set
        {
            lock (Sync)
            {
                _layer = value;
            }
        }
    }

    /// <inheritdoc/>
    public int ZIndex
    {
        get
        {
            lock (Sync)
            {
                return _zIndex;
            }
        }

        set
        {
            lock (Sync)
            {
                _zIndex = value;
            }
        }
    }

    /// <inheritdoc/>
    public Bounds Bounds
    {
        get
        {
            LineGeometryData? geometry;
            lock (Sync)
            {
                geometry = GetGeometryUnderLock();
            }

            // The world matrix is composed under the structural lock, so read the renderable state
            // first and never hold the per-object lock while acquiring the structural lock.
            return LineBounds.CreateWorldBounds(geometry, WorldMatrix());
        }
    }

    /// <summary>
    /// Copies this line's capture state under one per-object lock acquisition.
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock. Capture uses this instead of the public property
    /// getters, which would acquire <see cref="Transform.Sync"/> once per field and could observe a
    /// concurrent writer between fields. The cached geometry is rebuilt here only when points,
    /// width, or closed state changed since the last rebuild. The snapshot's
    /// <see cref="LineSnapshot.WorldMatrix"/> is the local matrix as a placeholder; the capture pass
    /// replaces it with the composed world matrix after this method returns and the lock is
    /// released.
    /// </remarks>
    /// <param name="snapshot">
    /// The line values with the local matrix in <see cref="LineSnapshot.WorldMatrix"/>.
    /// </param>
    /// <returns>The line's local-to-parent matrix.</returns>
    internal Matrix3x2 CaptureForSnapshot(out LineSnapshot snapshot)
    {
        lock (Sync)
        {
            var local = LocalMatrixUnderLock;
            LineGeometryData? geometry = GetGeometryUnderLock();
            ShaderRenderData? shaderData = _shaderData;
            var uniform0 = Vector4.Zero;
            var uniform1 = Vector4.Zero;
            var uniform2 = Vector4.Zero;
            var uniform3 = Vector4.Zero;
            if (shaderData is not null)
            {
                Span<Vector4> block = stackalloc Vector4[ShaderLayout.InstanceBlockVectorCount];
                _uniforms.WriteBlock(shaderData, block);
                uniform0 = block[0];
                uniform1 = block[1];
                uniform2 = block[2];
                uniform3 = block[3];
            }

            snapshot = new LineSnapshot(
                geometry,
                local,
                _color,
                ToMask(_layer),
                _zIndex,
                _antiAlias)
            {
                ShaderData = shaderData,
                Uniform0 = uniform0,
                Uniform1 = uniform1,
                Uniform2 = uniform2,
                Uniform3 = uniform3,
                Width = _width,
            };
            return local;
        }
    }

    /// <inheritdoc/>
    public void SetUniform(string name, float value)
        => SetUniformCore(name, ShaderUniformType.Float, new Vector4(value, 0f, 0f, 0f));

    /// <inheritdoc/>
    public void SetUniform(string name, Vector2 value)
        => SetUniformCore(name, ShaderUniformType.Vector2, new Vector4(value.X, value.Y, 0f, 0f));

    /// <inheritdoc/>
    public void SetUniform(string name, Vector3 value)
        => SetUniformCore(
            name,
            ShaderUniformType.Vector3,
            new Vector4(value.X, value.Y, value.Z, 0f));

    /// <inheritdoc/>
    public void SetUniform(string name, Vector4 value)
        => SetUniformCore(name, ShaderUniformType.Vector4, value);

    /// <inheritdoc/>
    public void SetUniform(string name, Color value)
        => SetUniformCore(name, ShaderUniformType.Color, value);

    /// <inheritdoc/>
    public void ResetUniforms()
    {
        lock (Sync)
        {
            _uniforms.Reset();
        }
    }

    private LineGeometryData? GetGeometryUnderLock()
    {
        if (_geometryDirty)
        {
            _geometry = LineGeometryBuilder.Build(_points, _closed, _width);
            _geometryDirty = false;
        }

        return _geometry;
    }

    private void SetUniformCore(string name, ShaderUniformType type, Vector4 value)
    {
        lock (Sync)
        {
            _uniforms.Set(name, type, value, _shaderData, "line");
        }
    }

    private static Vector2[] CopyPoints(IReadOnlyList<Vector2>? points)
    {
        if (points is null || points.Count == 0)
        {
            return [];
        }

        var copy = new Vector2[points.Count];
        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = points[index];
        }

        return copy;
    }

    private static uint ToMask(TLayer layer) => (uint)Unsafe.As<TLayer, int>(ref layer);
}
