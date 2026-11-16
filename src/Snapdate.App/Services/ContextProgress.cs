namespace Snapdate.App.Services;

/// <summary>
/// An <see cref="IProgress{T}"/> that runs the handler on the synchronization context it was created on (the UI
/// thread), in order, or directly when there is none (tests). Unlike <see cref="Progress{T}"/> it never falls back
/// to the thread pool, so reports cannot overtake each other or arrive after the result.
/// </summary>
public sealed class ContextProgress<T> : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Action<T> _handler;

    public ContextProgress(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public void Report(T value)
    {
        if (_context is null)
        {
            _handler(value);
        }
        else
        {
            _context.Post(_ => _handler(value), null);
        }
    }
}
