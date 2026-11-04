using System.Collections.ObjectModel;
using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned line renderable: a transform plus an open or closed polyline and line-specific
/// draw state.
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
/// The shader, color, layer, z-index, instance-uniform state, and their capture packing come from
/// <see cref="Renderable2D{TLayer}"/>. Capture reads them through
/// <see cref="Renderable2D{TLayer}.CaptureShared"/> in the same critical section as the geometry.
/// </para>
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class LineRender<TLayer> : Renderable2D<TLayer>, ILineRender<TLayer>
    where TLayer : struct, Enum
{
    private Vector2[] _points;
    private ReadOnlyCollection<Vector2> _pointsView;
    private LineGeometryData? _geometry;
    private bool _geometryDirty;
    private bool _closed;
    private float _width;
    private bool _antiAlias;

    internal LineRender(Lock structuralLock)
        : base(structuralLock, "line")
    {
        _points = [];
        _pointsView = Array.AsReadOnly(_points);
        _geometry = null;
        _geometryDirty = true;
        _closed = false;
        _width = 1f;
        _antiAlias = true;
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
    public override Bounds Bounds
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
    /// width, or closed state changed since the last rebuild. The shared shader, color, layer,
    /// z-index and uniform state are read in the same critical section. The snapshot's
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
            CaptureShared(
                out ShaderRenderData? shaderData,
                out Vector4 uniform0,
                out Vector4 uniform1,
                out Vector4 uniform2,
                out Vector4 uniform3,
                out Color color,
                out uint layer,
                out int zIndex);
            snapshot = new LineSnapshot(
                geometry,
                local,
                color,
                layer,
                zIndex,
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

    private LineGeometryData? GetGeometryUnderLock()
    {
        if (_geometryDirty)
        {
            _geometry = LineGeometryBuilder.Build(_points, _closed, _width);
            _geometryDirty = false;
        }

        return _geometry;
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
}
