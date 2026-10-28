using System.Runtime.InteropServices;
using Bork2d.Engine;
using Silk.NET.SDL;
using SilkNet = Silk.NET.SDL;

namespace Bork2d.Runtime.Sdl;

/// <summary>
/// Creates the <see cref="Bork2dException"/> the SDL seam reports for a failed native call.
/// </summary>
/// <remarks>
/// Every failure message includes the SDL operation and the current <c>SDL_GetError</c> text so the
/// caller can surface it unchanged; no SDL error state is cached or cleared here.
/// </remarks>
internal static unsafe class SdlError
{
    /// <summary>Creates a failure for an SDL operation.</summary>
    /// <param name="sdl">The SDL instance whose error state is read.</param>
    /// <param name="operation">The SDL operation that failed, for example <c>SDL_CreateWindow</c>.</param>
    /// <returns>The exception to throw.</returns>
    internal static Bork2dException Create(SilkNet.Sdl sdl, string operation)
        => new($"SDL operation '{operation}' failed: {GetMessage(sdl)}");

    /// <summary>Reads the current SDL error message.</summary>
    /// <param name="sdl">The SDL instance whose error state is read.</param>
    /// <returns>The UTF-8 error text, or an empty string when SDL has no error.</returns>
    internal static string GetMessage(SilkNet.Sdl sdl)
    {
        var error = sdl.GetError();
        return error is null ? string.Empty : Marshal.PtrToStringUTF8((nint)error) ?? string.Empty;
    }
}
