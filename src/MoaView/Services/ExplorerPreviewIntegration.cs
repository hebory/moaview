using System.Windows.Threading;

namespace MoaView.Services;

/// <summary>Owns the process-local UI, timers and the one-per-session Explorer hotkey listener.</summary>
public sealed class ExplorerPreviewIntegration : IDisposable
{
    private readonly Mutex _mutex;
    private readonly WindowsExplorerSelectionService _selection;
    private readonly ExplorerPreviewCoordinator _coordinator;
    private readonly ExplorerSpaceKeyHook _hook;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public ExplorerPreviewIntegration(Dispatcher dispatcher)
    {
        _mutex = new Mutex(false, @"Local\MoaView.ExplorerSpacePreview");
        bool acquired;
        try
        {
            acquired = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            _mutex.Dispose();
            throw new InvalidOperationException("다른 모아뷰 창에서 탐색기 Space 미리보기를 사용 중입니다.");
        }

        WindowsExplorerSelectionService? selection = null;
        ExplorerSpaceKeyHook? hook = null;
        try
        {
            _selection = selection = new WindowsExplorerSelectionService();
            _coordinator = new ExplorerPreviewCoordinator(_selection, new ExplorerPreviewPresenter(), ExplorerWindowContext.Capture);
            _hook = hook = new ExplorerSpaceKeyHook(context =>
            {
                if (!dispatcher.HasShutdownStarted)
                {
                    dispatcher.BeginInvoke(new Action(async () =>
                    {
                        if (!_disposed)
                        {
                            await RunSafelyAsync(() => _coordinator.ToggleAsync(context));
                        }
                    }));
                }
            }, ExplorerPreviewDiagnostics.Write);

            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch
        {
            hook?.Dispose();
            selection?.Dispose();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            throw;
        }
    }

    public event EventHandler<string>? Failed;

    private async void OnTick(object? sender, EventArgs e) => await RunSafelyAsync(_coordinator.RefreshAsync);

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // Shutting down the Shell dispatcher can cancel a queued lookup.
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (!_disposed)
            {
                Failed?.Invoke(this, $"탐색기 연동이 중단되었습니다. 다시 켜서 재시도할 수 있습니다. {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _hook.Dispose();
        _coordinator.Dispose();
        _selection.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
