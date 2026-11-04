using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The shared base of engine 2D renderables: the shader, color, layer, z-index, and instance-uniform
/// state that every drawable 2D kind exposes.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// Every member is serialized by the per-object <see cref="Transform.Sync"/> lock. The shader setter
/// freezes the asset's declarations outside that lock, so a malformed asset cannot throw from the
/// setter and concurrent assignments publish exactly one frozen copy. Derived kinds pack their
/// capture record with <see cref="CaptureShared"/> while holding the lock and compose the world
/// matrix after releasing it.
/// </remarks>
internal abstract class Renderable2D<TLayer> : Transform, IRenderable<TLayer>
    where TLayer : struct, Enum
{
    private readonly InstanceUniformSet _uniforms = new();
    private readonly string _renderableKind;

    private ShaderAsset? _shader;
    private ShaderRenderData? _shaderData;
    private Color _color;
    private TLayer _layer;
    private int _zIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="Renderable2D{TLayer}"/> class.
    /// </summary>
    /// <param name="structuralLock">The engine-owned global structural lock.</param>
    /// <param name="renderableKind">
    /// The lowercase renderable kind used in the instance-uniform diagnostic messages.
    /// </param>
    internal Renderable2D(Lock structuralLock, string renderableKind)
        : base(structuralLock)
    {
        ArgumentNullException.ThrowIfNull(renderableKind);
        _renderableKind = renderableKind;
        _shader = null;
        _shaderData = null;
        _color = Color.White;
        _layer = default;
        _zIndex = 0;
    }

    /// <inheritdoc/>
    public abstract Bounds Bounds { get; }

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

    /// <summary>
    /// Packs the shared capture fields and the frozen instance-uniform block into one value set.
    /// </summary>
    /// <remarks>
    /// The caller must hold <see cref="Transform.Sync"/>. The method reads the frozen shader
    /// declarations, the stored uniform overrides, the color, the layer, and the z-index in one
    /// critical section, so a concurrent writer cannot produce a mixed record. The instance block is
    /// packed into a stack buffer, so the warm capture path allocates nothing.
    /// </remarks>
    /// <param name="shaderData">
    /// The assigned shader's frozen declarations, or <see langword="null"/> when none is assigned.
    /// </param>
    /// <param name="uniform0">The first vector of the instance uniform block.</param>
    /// <param name="uniform1">The second vector of the instance uniform block.</param>
    /// <param name="uniform2">The third vector of the instance uniform block.</param>
    /// <param name="uniform3">The fourth vector of the instance uniform block.</param>
    /// <param name="color">The stored linear RGBA render color.</param>
    /// <param name="layer">The stored layer as an unsigned culling mask.</param>
    /// <param name="zIndex">The stored shared 2D draw order.</param>
    protected void CaptureShared(
        out ShaderRenderData? shaderData,
        out Vector4 uniform0,
        out Vector4 uniform1,
        out Vector4 uniform2,
        out Vector4 uniform3,
        out Color color,
        out uint layer,
        out int zIndex)
    {
        shaderData = _shaderData;
        uniform0 = Vector4.Zero;
        uniform1 = Vector4.Zero;
        uniform2 = Vector4.Zero;
        uniform3 = Vector4.Zero;
        if (shaderData is not null)
        {
            Span<Vector4> block = stackalloc Vector4[ShaderLayout.InstanceBlockVectorCount];
            _uniforms.WriteBlock(shaderData, block);
            uniform0 = block[0];
            uniform1 = block[1];
            uniform2 = block[2];
            uniform3 = block[3];
        }

        color = _color;
        layer = ToMask(_layer);
        zIndex = _zIndex;
    }

    private void SetUniformCore(string name, ShaderUniformType type, Vector4 value)
    {
        lock (Sync)
        {
            _uniforms.Set(name, type, value, _shaderData, _renderableKind);
        }
    }

    private static uint ToMask(TLayer layer) => (uint)Unsafe.As<TLayer, int>(ref layer);
}
