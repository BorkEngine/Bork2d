namespace Bork2d.Runtime.Sdl;

/// <summary>
/// The SDL library version reported by <c>SDL_GetVersion</c>.
/// </summary>
/// <remarks>
/// The adapter reads the version from the loaded native library, so it reports the binary that is
/// actually deployed rather than the version declared by the NuGet package.
/// </remarks>
internal readonly struct SdlVersion
{
    /// <summary>Initializes a new instance of the <see cref="SdlVersion"/> struct.</summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="patch">The patch version.</param>
    internal SdlVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>Gets the major version.</summary>
    internal int Major { get; }

    /// <summary>Gets the minor version.</summary>
    internal int Minor { get; }

    /// <summary>Gets the patch version.</summary>
    internal int Patch { get; }

    /// <summary>Returns the version as <c>major.minor.patch</c>.</summary>
    /// <returns>The formatted version.</returns>
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
