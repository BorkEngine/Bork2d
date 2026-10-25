using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Common;

// Not completed
public class Transform: ITransform
{
    public ITransform? Parent { get; set; } = null;
    public Vector2 LocalPosition { get; set; } = Vector2.Zero;
    public float LocalRotation { get; set; } = 0.0f;
    public Vector2 LocalScale { get; set; } = Vector2.One;
    
    // Should be calculated automatically when local stuff changes, or parent changes
    // When parent changes Transform should retain its global stuff and recalculate local 
    public Vector2 GlobalPosition { get; set; } = Vector2.Zero;
    public float GlobalRotation { get; set; } = 0.0f;
    public Vector2 GlobalScale { get; set; } = Vector2.One;
}