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
    Func<ExplorerContext?> getCurrentContext) : IDisposable
{
    private int _generation;
    private bool _opening;
    private bool _queryPending;
    private bool _disposed;
    private ExplorerContext _source;
    private string? _path;

    public async Task ToggleAsync(ExplorerContext context)
    {
        if (_disposed)
        {
            return;
        }

        if (_opening || presenter.IsOpen)
        {
            Close();
            return;
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
        if (_disposed || _queryPending || !presenter.IsOpen)
        {
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

    public void Close()
    {
        ++_generation;
        _opening = false;
        _path = null;
        presenter.Close();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }
}
