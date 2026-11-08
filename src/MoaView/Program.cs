using System.Windows;
using MoaView.Services;

namespace MoaView;

public static class Program
{
    [System.STAThread]
    public static int Main(string[] args)
    {
        var options = App.ParseStartupOptions(args, null);
        // Shell previews and verification processes retain their independent, one-window lifetime.
        using var resident = options.PreviewOnly || options.VerifyStartupAndClose ? null : new ResidentInstance();
        if (resident is { IsPrimary: false })
        {
            var path = options.InitialFilePath is { } file ? System.IO.Path.GetFullPath(file) : null;
            if (resident.TryForward(new ResidentRequest(options.Background, path), out var error)) return 0;
            MessageBox.Show(error, "MoaView", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }

        var app = new App(args, resident);
        app.InitializeComponent();
        return app.Run();
    }
}
