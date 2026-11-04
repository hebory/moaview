using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MoaView.ViewModels;

namespace MoaView;

public readonly record struct StartupOptions(string? InitialFilePath, bool VerifyStartupAndClose);

public partial class App : Application
{
    public const string VerifyStartupSwitch = "--verify-startup";

    private readonly IReadOnlyList<string> _startupArgs;

    public App()
        : this(Array.Empty<string>())
    {
    }

    public App(IReadOnlyList<string> startupArgs)
    {
        _startupArgs = startupArgs ?? Array.Empty<string>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = ParseStartupOptions(_startupArgs, e.Args);
        if (options.VerifyStartupAndClose)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                WriteStandardErrorLine(args.Exception.ToString());
                args.Handled = true;
                Shutdown(3);
            };
        }

        var viewModel = new MainWindowViewModel();
        if (!string.IsNullOrWhiteSpace(options.InitialFilePath))
        {
            viewModel.OpenFile(options.InitialFilePath);
        }

        var mainWindow = new MainWindow(viewModel);
        if (options.VerifyStartupAndClose)
        {
            mainWindow.ShowActivated = false;
            mainWindow.ShowInTaskbar = false;
        }

        MainWindow = mainWindow;
        mainWindow.Show();

        if (options.VerifyStartupAndClose)
        {
            ScheduleStartupVerification(mainWindow);
        }
    }

    public static StartupOptions ParseStartupOptions(
        IReadOnlyList<string>? constructorArgs,
        IReadOnlyList<string>? eventArgs)
    {
        var verifyStartup = false;
        string? initialFilePath = null;

        InspectArgs(constructorArgs, ref verifyStartup, ref initialFilePath);
        InspectArgs(eventArgs, ref verifyStartup, ref initialFilePath);

        return new StartupOptions(initialFilePath, verifyStartup);
    }

    public static string? ResolveInitialFilePath(
        IReadOnlyList<string>? constructorArgs,
        IReadOnlyList<string>? eventArgs) =>
        ParseStartupOptions(constructorArgs, eventArgs).InitialFilePath;

    private static void InspectArgs(
        IReadOnlyList<string>? args,
        ref bool verifyStartup,
        ref string? initialFilePath)
    {
        if (args is null)
        {
            return;
        }

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (string.Equals(arg.Trim(), VerifyStartupSwitch, StringComparison.OrdinalIgnoreCase))
            {
                verifyStartup = true;
                continue;
            }

            initialFilePath ??= arg;
        }
    }

    private void ScheduleStartupVerification(MainWindow mainWindow)
    {
        mainWindow.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                try
                {
                    var verified = mainWindow.VerifyRenderedState();
                    WriteVerificationReport(mainWindow, verified);

                    if (!verified)
                    {
                        Shutdown(2);
                        return;
                    }

                    mainWindow.Close();
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    WriteStandardErrorLine(ex.ToString());
                    Shutdown(3);
                }
            }));
    }

    private static void WriteVerificationReport(MainWindow window, bool verified)
    {
        var vm = window.ViewModel;
        var result = vm.CurrentResult;
        var reportLine =
            $"MOAVIEW_STARTUP_VERIFIED={verified}" +
            $"|STATE={vm.State}" +
            $"|VISIBLE={window.IsVisible}" +
            $"|TITLE={window.Title}" +
            $"|BADGE={vm.NoticeBadgeText}" +
            $"|PIXELS={result.PixelWidth}x{result.PixelHeight}";

        using var stdout = Console.OpenStandardOutput();
        if (stdout == Stream.Null || !stdout.CanWrite)
        {
            return;
        }

        using var writer = new StreamWriter(
            stdout,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true);
        writer.WriteLine(reportLine);
        writer.Flush();
    }

    private static void WriteStandardErrorLine(string text)
    {
        using var stderr = Console.OpenStandardError();
        if (stderr == Stream.Null || !stderr.CanWrite)
        {
            return;
        }

        using var writer = new StreamWriter(
            stderr,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true);
        writer.WriteLine(text);
        writer.Flush();
    }
}
