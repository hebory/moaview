using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoaView.Controls;

internal abstract class SpecializedPreviewControl : Grid, IDisposable
{
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBlock _status = new()
    {
        Text = "미리보기를 준비하고 있습니다.",
        Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(24),
    };

    protected SpecializedPreviewControl()
    {
        Children.Add(_status);
        SetZIndex(_status, 1);
    }

    internal Task<bool> Ready => _ready.Task;
    protected bool IsDisposed { get; private set; }

    protected void ShowReady()
    {
        if (IsDisposed) return;
        _status.Visibility = Visibility.Collapsed;
        _ready.TrySetResult(true);
    }

    protected void ShowError(string message)
    {
        if (IsDisposed) return;
        _status.Text = message;
        _status.Visibility = Visibility.Visible;
        _ready.TrySetResult(false);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _ready.TrySetResult(false);
        ReleaseResources();
        Children.Clear();
    }

    protected virtual void ReleaseResources() { }
}
