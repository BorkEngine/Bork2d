using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The pure present-mode policy (D-M6-17).
/// </summary>
internal static class PresentModePolicy
{
    /// <summary>
    /// Selects the present mode: <c>FIFO</c> when vsync is requested; otherwise <c>MAILBOX</c>,
    /// then <c>IMMEDIATE</c>, then <c>FIFO</c>.
    /// </summary>
    /// <param name="vsync">The requested vertical-sync setting.</param>
    /// <param name="available">The present modes the surface supports.</param>
    /// <returns>The selected present mode.</returns>
    internal static PresentModeKHR Select(bool vsync, IReadOnlyList<PresentModeKHR> available)
    {
        if (vsync)
        {
            return PresentModeKHR.FifoKhr;
        }

        if (Contains(available, PresentModeKHR.MailboxKhr))
        {
            return PresentModeKHR.MailboxKhr;
        }

        return Contains(available, PresentModeKHR.ImmediateKhr)
            ? PresentModeKHR.ImmediateKhr
            : PresentModeKHR.FifoKhr;
    }

    private static bool Contains(IReadOnlyList<PresentModeKHR> available, PresentModeKHR mode)
    {
        for (var index = 0; index < available.Count; index++)
        {
            if (available[index] == mode)
            {
                return true;
            }
        }

        return false;
    }
}
