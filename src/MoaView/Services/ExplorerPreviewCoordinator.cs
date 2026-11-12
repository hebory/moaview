namespace MoaView.Services;

public readonly record struct ExplorerContext(nint WindowHandle, nint ViewHandle);

public interface IExplorerSelectionService
{
    Task<string?> GetSelectedPathAsync(ExplorerContext context);
}

public interface IExplorerPreviewPresenter
{
    bool IsOpen { get; }
    void Show(string path, ExplorerContext context);
    void Close();
}

/// <summary>UI 스레드에서 호출합니다. 느린 Shell 응답이 닫힌 창을 다시 열지 않도록 요청을 무효화합니다.</summary>
public sealed class ExplorerPreviewCoordinator(
    IExplorerSelectionService selectionService,
    IExplorerPreviewPresenter presenter,
    Func<ExplorerContext?> getCurrentContext,
    Func<nint, bool>? isWindowAlive = null) : IDisposable
{
    private int _generation;
    private bool _opening;
    private bool _queryPending;
    private bool _disposed;
    private ExplorerContext? _pendingOpen;
    private ExplorerContext _source;
    private string? _path;

    public async Task ToggleAsync(ExplorerContext context)
    {
        if (_disposed)
        {
            return;
        }

        if (_pendingOpen is { } pending)
        {
            // Keep only the latest deliberate Space target while an older Shell call drains.
            _pendingOpen = pending == context ? null : context;
            return;
        }

        if (_opening || presenter.IsOpen)
        {
            if (_source == context)
            {
                Close();
                return;
            }

            Close();
            if (_queryPending)
            {
                _pendingOpen = context;
                return;
            }
        }

        // A cancelled COM call may still be completing. Do not queue more work behind it.
        if (_queryPending || getCurrentContext() != context)
        {
            return;
        }

        _opening = true;
        _source = context;
        var generation = ++_generation;
        try
        {
            var path = await ReadSelectionAsync(context);
            await OpenPendingSelectionAsync();
            if (_disposed || generation != _generation || getCurrentContext() != context)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                _path = path;
                presenter.Show(path, context);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _opening = false;
            }
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed || _queryPending)
        {
            return;
        }

        if (!presenter.IsOpen)
        {
            if (_path is not null && !_opening)
            {
                Close();
            }

            return;
        }

        if (_source.WindowHandle != 0 && isWindowAlive is not null && !isWindowAlive(_source.WindowHandle))
        {
            Close();
            return;
        }

        var current = getCurrentContext();
        if (current is not { } context)
        {
            // Clicking the preview, an address bar or another app must not steal focus.
            return;
        }

        if (context.WindowHandle != _source.WindowHandle)
        {
            Close();
            return;
        }

        var generation = _generation;
        var path = await ReadSelectionAsync(context);
        await OpenPendingSelectionAsync();
        if (_disposed || generation != _generation || !presenter.IsOpen || getCurrentContext() != context)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            Close();
        }
        else if (_source != context || !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            _source = context;
            _path = path;
            presenter.Show(path, context);
        }
    }

    private async Task<string?> ReadSelectionAsync(ExplorerContext context)
    {
        _queryPending = true;
        try
        {
            return await selectionService.GetSelectedPathAsync(context);
        }
        finally
        {
            _queryPending = false;
        }
    }

    private async Task OpenPendingSelectionAsync()
    {
        if (_pendingOpen is not { } context)
        {
            return;
        }

        _pendingOpen = null;
        if (!_disposed && getCurrentContext() == context)
        {
            await ToggleAsync(context);
        }
    }

    public void Close()
    {
        ++_generation;
        _opening = false;
        _pendingOpen = null;
        _source = default;
        _path = null;
        presenter.Close();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }
}
