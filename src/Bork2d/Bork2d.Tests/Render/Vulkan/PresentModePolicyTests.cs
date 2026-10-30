using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for <see cref="PresentModePolicy"/> (D-M6-17).
/// </summary>
public sealed class PresentModePolicyTests
{
    [Fact]
    public void VsyncAlwaysSelectsFifo()
    {
        PresentModeKHR selected = PresentModePolicy.Select(
            vsync: true,
            [PresentModeKHR.MailboxKhr, PresentModeKHR.ImmediateKhr, PresentModeKHR.FifoKhr]);

        Assert.Equal(PresentModeKHR.FifoKhr, selected);
    }

    [Fact]
    public void NoVsyncPrefersMailbox()
    {
        PresentModeKHR selected = PresentModePolicy.Select(
            vsync: false,
            [PresentModeKHR.ImmediateKhr, PresentModeKHR.MailboxKhr, PresentModeKHR.FifoKhr]);

        Assert.Equal(PresentModeKHR.MailboxKhr, selected);
    }

    [Fact]
    public void NoVsyncFallsBackToImmediate()
    {
        PresentModeKHR selected = PresentModePolicy.Select(
            vsync: false,
            [PresentModeKHR.ImmediateKhr, PresentModeKHR.FifoKhr]);

        Assert.Equal(PresentModeKHR.ImmediateKhr, selected);
    }

    [Fact]
    public void NoVsyncFallsBackToFifoWhenOnlyFifoIsAvailable()
    {
        PresentModeKHR selected = PresentModePolicy.Select(
            vsync: false,
            [PresentModeKHR.FifoKhr]);

        Assert.Equal(PresentModeKHR.FifoKhr, selected);
    }

    [Fact]
    public void NoVsyncFallsBackToFifoForAnEmptyList()
    {
        PresentModeKHR selected = PresentModePolicy.Select(vsync: false, []);

        Assert.Equal(PresentModeKHR.FifoKhr, selected);
    }
}
