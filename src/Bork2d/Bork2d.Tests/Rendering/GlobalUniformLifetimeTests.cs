using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Tests.Scene;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Proves that resetting a snapshot releases the managed uniform names it captured, so a warm reset
/// retains no obsolete strings.
/// </summary>
/// <remarks>
/// The collection disables parallelization so a forced collection cannot be perturbed by another
/// test class while the weak reference is observed.
/// </remarks>
[Collection(SceneGcCollection.Name)]
public sealed class GlobalUniformLifetimeTests
{
    [Fact]
    public void ResetReleasesCapturedUniformNames()
    {
        var snapshot = new FrameSnapshot();

        WeakReference reference = AddThenReset(snapshot);

        for (var attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            ForceCollect();
        }

        Assert.False(reference.IsAlive, "Reset must clear the captured uniform name references.");
        Assert.Equal(0, snapshot.GlobalUniformCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddThenReset(FrameSnapshot snapshot)
    {
        string name = new('n', 128);
        var reference = new WeakReference(name);
        snapshot.AddGlobalUniform(
            new GlobalUniformSnapshot(name, ShaderUniformType.Float, Vector4.One));
        snapshot.Reset();
        return reference;
    }

    private static void ForceCollect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
