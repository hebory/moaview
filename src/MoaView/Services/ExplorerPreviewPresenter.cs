using System.Windows.Interop;
using MoaView.ViewModels;

namespace MoaView.Services;

public sealed class ExplorerPreviewPresenter : IExplorerPreviewPresenter
{
    private readonly Func<MainWindowViewModel> _viewModelFactory;
    private readonly AppAppearance? _appearance;

    public ExplorerPreviewPresenter(Func<MainWindowViewModel>? viewModelFactory = null, AppAppearance? appearance = null)
    {
        _viewModelFactory = viewModelFactory ?? (() => new MainWindowViewModel());
        _appearance = appearance;
    }

    public MainWindow? Window { get; private set; }

    public bool IsOpen => Window is { IsVisible: true };

    public void Show(string path, ExplorerContext context)
    {
        if (Window is null)
        {
            var window = new MainWindow(_viewModelFactory(), isExplorerPreview: true, appearance: _appearance)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            // An owned window stays above its Explorer without becoming globally topmost.
            if (context.WindowHandle != 0)
            {
                new WindowInteropHelper(window).Owner = context.WindowHandle;
            }

            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(Window, window))
                {
                    Window = null;
                }
            };
            Window = window;
        }
        else if (context.WindowHandle != 0)
        {
            var helper = new WindowInteropHelper(Window);
            if (helper.Owner != context.WindowHandle)
            {
                helper.Owner = context.WindowHandle;
            }
        }

        Window.ViewModel.OpenFile(path);
        Window.Show();
    }

    public void Close()
    {
        Window?.Close();
        Window = null;
    }
}
