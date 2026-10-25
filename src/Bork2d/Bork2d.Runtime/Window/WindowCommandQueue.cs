namespace Bork2d.Runtime;

/// <summary>
/// The kind of a queued <see cref="IWindow"/> command.
/// </summary>
internal enum WindowCommandKind
{
    /// <summary>A title change.</summary>
    Title = 0,

    /// <summary>A framebuffer-size request.</summary>
    Size = 1,

    /// <summary>A fullscreen change.</summary>
    Fullscreen = 2,

    /// <summary>A cursor-visibility change.</summary>
    CursorVisible = 3,
}

/// <summary>
/// One window command drained from <see cref="WindowCommandQueue"/>.
/// </summary>
/// <remarks>
/// The queue stores commands by property and coalesces pending values; a drained command always
/// carries the latest value requested for its property.
/// </remarks>
internal readonly struct WindowCommand
{
    private WindowCommand(
        WindowCommandKind kind,
        string? title,
        float width,
        float height,
        bool flag)
    {
        Kind = kind;
        Title = title;
        Width = width;
        Height = height;
        Flag = flag;
    }

    /// <summary>Gets the command kind.</summary>
    internal WindowCommandKind Kind { get; }

    /// <summary>Gets the requested title for a <see cref="WindowCommandKind.Title"/> command.</summary>
    internal string? Title { get; }

    /// <summary>Gets the requested framebuffer width for a <see cref="WindowCommandKind.Size"/> command.</summary>
    internal float Width { get; }

    /// <summary>Gets the requested framebuffer height for a <see cref="WindowCommandKind.Size"/> command.</summary>
    internal float Height { get; }

    /// <summary>Gets the requested flag for a fullscreen or cursor command.</summary>
    internal bool Flag { get; }

    /// <summary>Creates a title command.</summary>
    /// <param name="title">The requested title.</param>
    /// <returns>The command.</returns>
    internal static WindowCommand TitleCommand(string title)
        => new(WindowCommandKind.Title, title, 0f, 0f, false);

    /// <summary>Creates a size command.</summary>
    /// <param name="width">The requested framebuffer width.</param>
    /// <param name="height">The requested framebuffer height.</param>
    /// <returns>The command.</returns>
    internal static WindowCommand SizeCommand(float width, float height)
        => new(WindowCommandKind.Size, null, width, height, false);

    /// <summary>Creates a fullscreen command.</summary>
    /// <param name="fullscreen">The requested fullscreen state.</param>
    /// <returns>The command.</returns>
    internal static WindowCommand FullscreenCommand(bool fullscreen)
        => new(WindowCommandKind.Fullscreen, null, 0f, 0f, fullscreen);

    /// <summary>Creates a cursor-visibility command.</summary>
    /// <param name="visible">The requested cursor visibility.</param>
    /// <returns>The command.</returns>
    internal static WindowCommand CursorVisibleCommand(bool visible)
        => new(WindowCommandKind.CursorVisible, null, 0f, 0f, visible);

    /// <summary>Returns a short description for logs.</summary>
    /// <returns>The command description.</returns>
    public override string ToString()
        => Kind switch
        {
            WindowCommandKind.Title => $"Title(\"{Title}\")",
            WindowCommandKind.Size => $"Size({Width}x{Height})",
            WindowCommandKind.Fullscreen => $"Fullscreen({Flag})",
            _ => $"CursorVisible({Flag})",
        };
}

/// <summary>
/// The single multi-producer window command queue owned by the OS/window thread.
/// </summary>
/// <remarks>
/// <para>
/// Any thread may enqueue. A command is dropped when its value repeats the last requested value
/// for that property, which keeps a game that writes a window property every tick from growing
/// the queue. Pending commands coalesce per property with the latest value winning, and the
/// original position of a property is kept so the order between different properties is stable.
/// </para>
/// <para>
/// One <see cref="Lock"/> guards the queue. It is never held while calling SDL or while waiting,
/// and the platform releases it before applying the drained commands. The queue allocates only
/// when there is something to drain.
/// </para>
/// </remarks>
internal sealed class WindowCommandQueue
{
    private const int KindCount = 4;

    private readonly Lock _lock = new();
    private readonly PendingCommand[] _pending = new PendingCommand[KindCount];
    private readonly int[] _order = new int[KindCount];

    private string _lastTitle;
    private float _lastWidth;
    private float _lastHeight;
    private bool _lastFullscreen;
    private bool _lastCursorVisible;
    private int _orderCount;

    /// <summary>Initializes a new instance of the <see cref="WindowCommandQueue"/> class.</summary>
    /// <param name="initialTitle">The initial title, used for same-value drops.</param>
    /// <param name="initialWidth">The initial framebuffer width request.</param>
    /// <param name="initialHeight">The initial framebuffer height request.</param>
    /// <param name="initialFullscreen">The initial fullscreen state.</param>
    /// <param name="initialCursorVisible">The initial cursor visibility.</param>
    internal WindowCommandQueue(
        string initialTitle,
        int initialWidth,
        int initialHeight,
        bool initialFullscreen,
        bool initialCursorVisible)
    {
        _lastTitle = initialTitle;
        _lastWidth = initialWidth;
        _lastHeight = initialHeight;
        _lastFullscreen = initialFullscreen;
        _lastCursorVisible = initialCursorVisible;
    }

    /// <summary>Enqueues a title change unless it repeats the last requested title.</summary>
    /// <param name="title">The requested title.</param>
    /// <returns><see langword="true"/> when the command was queued or updated.</returns>
    internal bool TrySetTitle(string title)
    {
        lock (_lock)
        {
            if (string.Equals(_lastTitle, title, StringComparison.Ordinal))
            {
                return false;
            }

            _lastTitle = title;
            SetPending(WindowCommandKind.Title, new PendingCommand { Title = title });
            return true;
        }
    }

    /// <summary>Enqueues a framebuffer-size request unless it repeats the last requested size.</summary>
    /// <param name="width">The requested framebuffer width in physical pixels.</param>
    /// <param name="height">The requested framebuffer height in physical pixels.</param>
    /// <returns><see langword="true"/> when the command was queued or updated.</returns>
    internal bool TrySetSize(float width, float height)
    {
        lock (_lock)
        {
            if (_lastWidth.Equals(width) && _lastHeight.Equals(height))
            {
                return false;
            }

            _lastWidth = width;
            _lastHeight = height;
            SetPending(
                WindowCommandKind.Size,
                new PendingCommand { Width = width, Height = height });
            return true;
        }
    }

    /// <summary>Enqueues a fullscreen change unless it repeats the last requested state.</summary>
    /// <param name="fullscreen">The requested fullscreen state.</param>
    /// <returns><see langword="true"/> when the command was queued or updated.</returns>
    internal bool TrySetFullscreen(bool fullscreen)
    {
        lock (_lock)
        {
            if (_lastFullscreen == fullscreen)
            {
                return false;
            }

            _lastFullscreen = fullscreen;
            SetPending(
                WindowCommandKind.Fullscreen,
                new PendingCommand { Flag = fullscreen });
            return true;
        }
    }

    /// <summary>Enqueues a cursor change unless it repeats the last requested state.</summary>
    /// <param name="visible">The requested cursor visibility.</param>
    /// <returns><see langword="true"/> when the command was queued or updated.</returns>
    internal bool TrySetCursorVisible(bool visible)
    {
        lock (_lock)
        {
            if (_lastCursorVisible == visible)
            {
                return false;
            }

            _lastCursorVisible = visible;
            SetPending(
                WindowCommandKind.CursorVisible,
                new PendingCommand { Flag = visible });
            return true;
        }
    }

    /// <summary>Drains every pending command in property order.</summary>
    /// <returns>
    /// The pending commands, or an empty array when there are none. The caller owns the array.
    /// </returns>
    internal WindowCommand[] Drain()
    {
        lock (_lock)
        {
            if (_orderCount == 0)
            {
                return [];
            }

            var commands = new WindowCommand[_orderCount];
            for (var index = 0; index < _orderCount; index++)
            {
                var kind = (WindowCommandKind)_order[index];
                commands[index] = _pending[(int)kind].ToCommand(kind);
                _pending[(int)kind] = default;
            }

            _orderCount = 0;
            return commands;
        }
    }

    private void SetPending(WindowCommandKind kind, PendingCommand command)
    {
        if (!_pending[(int)kind].HasValue)
        {
            _order[_orderCount] = (int)kind;
            _orderCount++;
        }

        command.HasValue = true;
        _pending[(int)kind] = command;
    }

    private struct PendingCommand
    {
        internal string? Title;
        internal float Width;
        internal float Height;
        internal bool Flag;
        internal bool HasValue;

        internal readonly WindowCommand ToCommand(WindowCommandKind kind)
            => kind switch
            {
                WindowCommandKind.Title => WindowCommand.TitleCommand(Title ?? string.Empty),
                WindowCommandKind.Size => WindowCommand.SizeCommand(Width, Height),
                WindowCommandKind.Fullscreen => WindowCommand.FullscreenCommand(Flag),
                _ => WindowCommand.CursorVisibleCommand(Flag),
            };
    }
}
