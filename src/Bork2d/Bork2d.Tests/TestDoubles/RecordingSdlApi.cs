using Bork2d.Runtime.Sdl;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// Records the seam calls made by the real windowed platform while forwarding to a real
/// <see cref="SilkSdlApi"/>.
/// </summary>
/// <remarks>
/// Only the setup calls are recorded; the per-event polling calls would make the log grow without
/// bound. The wrapper proves that the platform sets both Windows DPI hints before
/// <c>SDL_Init</c> and that it creates the window afterwards.
/// </remarks>
internal sealed class RecordingSdlApi : ISdlApi
{
    private readonly ISdlApi _inner;
    private readonly Lock _gate = new();
    private readonly List<string> _calls = [];
    private bool _disposed;

    internal RecordingSdlApi(ISdlApi inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Gets a snapshot of the recorded setup calls in call order.</summary>
    internal IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    public SdlVersion Version => _inner.Version;

    public string VideoDriver => _inner.VideoDriver;

    public string GetError() => _inner.GetError();

    public bool SetHint(string name, string value)
    {
        Record($"SetHint({name}={value})");
        return _inner.SetHint(name, value);
    }

    public void InitializeVideo()
    {
        Record("InitializeVideo");
        _inner.InitializeVideo();
    }

    public void Quit()
    {
        Record("Quit");
        _inner.Quit();
    }

    public ISdlWindow CreateWindow(string title, int width, int height, SdlWindowFlags flags)
    {
        Record($"CreateWindow({title},{width},{height},{(uint)flags:X8})");
        return _inner.CreateWindow(title, width, height, flags);
    }

    public bool TryPollEvent(out SdlEvent sdlEvent) => _inner.TryPollEvent(out sdlEvent);

    public bool TryWaitEvent(out SdlEvent sdlEvent) => _inner.TryWaitEvent(out sdlEvent);

    public bool TryPushEvent(in SdlEvent sdlEvent) => _inner.TryPushEvent(sdlEvent);

    public int RegisterEvents(int count)
    {
        Record($"RegisterEvents({count})");
        return _inner.RegisterEvents(count);
    }

    public void ShowCursor(bool visible) => _inner.ShowCursor(visible);

    public void StartTextInput() => _inner.StartTextInput();

    public void StopTextInput() => _inner.StopTextInput();

    public ReadOnlySpan<byte> GetKeyboardState() => _inner.GetKeyboardState();

    public SdlMouseState GetMouseState() => _inner.GetMouseState();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Record("Dispose");
        _inner.Dispose();
    }

    private void Record(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}
