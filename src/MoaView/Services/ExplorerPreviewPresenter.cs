using System.Windows.Interop;
using MoaView.ViewModels;

namespace MoaView.Services;

public sealed class ExplorerPreviewPresenter : IExplorerPreviewPresenter
{
    public MainWindow? Window { get; private set; }

    public bool IsOpen => Window is { IsVisible: true };

    public void Show(string path, ExplorerContext context)
    {
        if (Window is null)
        {
            var window = new MainWindow(new MainWindowViewModel(), isExplorerPreview: true)
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

        Window.ViewModel.OpenFile(path);
        Window.Show();
    }

    public void Close()
    {
        Window?.Close();
        Window = null;
    }
}
