using System.Runtime.InteropServices;
using Bork2d.Api;
using Silk.NET.Core.Contexts;
using Silk.NET.SDL;

namespace Bork2d.Runtime;

/// <summary>
/// Loads the SDL2 native library for <see cref="Sdl"/> without Silk.NET's path resolver.
/// </summary>
/// <remarks>
/// <para>
/// <c>Sdl.GetApi()</c> resolves the native library through
/// <c>Silk.NET.Core.Loader.DefaultPathResolver</c>, which reports IL3000/IL3002 single-file trim
/// warnings under NativeAOT. This context loads the same package-provided SDL2 binary through the
/// runtime's normal native-asset resolution for the Silk.NET.SDL assembly instead, which keeps the
/// AOT publish warning-free without a suppression or an extra binary.
/// </para>
/// <para>
/// The context owns one native library handle and frees it on <see cref="Dispose"/>. It performs no
/// reflection or dynamic code and has no thread affinity.
/// </para>
/// </remarks>
internal sealed class SdlNativeContext : INativeContext
{
    private const string LibraryName = "SDL2";

    private readonly nint _handle;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SdlNativeContext"/> class.</summary>
    /// <exception cref="Bork2dException">The SDL2 native library could not be loaded.</exception>
    internal SdlNativeContext()
    {
        if (!NativeLibrary.TryLoad(LibraryName, typeof(Sdl).Assembly, null, out _handle))
        {
            throw new Bork2dException(
                $"The SDL2 native library ('{LibraryName}') could not be loaded. Silk.NET.SDL 2.23.0 "
                + "supplies it as a runtime-specific asset; verify the package restore and the "
                + "target runtime identifier.");
        }
    }

    /// <summary>Resolves a native export.</summary>
    /// <param name="proc">The exported symbol name.</param>
    /// <param name="slot">The Silk.NET override slot; unused by this context.</param>
    /// <returns>The function address.</returns>
    /// <exception cref="Bork2dException">The symbol is not exported.</exception>
    public nint GetProcAddress(string proc, int? slot = default)
        => NativeLibrary.TryGetExport(_handle, proc, out var address)
            ? address
            : throw new Bork2dException($"The SDL2 native library does not export '{proc}'.");

    /// <summary>Tries to resolve a native export.</summary>
    /// <param name="proc">The exported symbol name.</param>
    /// <param name="address">The function address when found.</param>
    /// <param name="slot">The Silk.NET override slot; unused by this context.</param>
    /// <returns><see langword="true"/> when the symbol was found.</returns>
    public bool TryGetProcAddress(string proc, out nint address, int? slot = default)
        => NativeLibrary.TryGetExport(_handle, proc, out address);

    /// <summary>Frees the native library handle. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeLibrary.Free(_handle);
    }
}
