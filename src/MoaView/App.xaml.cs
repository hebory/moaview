using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MoaView.Services;
using MoaView.ViewModels;

namespace MoaView;

public readonly record struct StartupOptions(string? InitialFilePath, bool VerifyStartupAndClose, bool PreviewOnly);

public partial class App : Application
{
    public const string VerifyStartupSwitch = "--verify-startup";
    public const string PreviewSwitch = "--preview";

    private readonly IReadOnlyList<string> _startupArgs;
    private ExplorerPreviewIntegration? _explorerIntegration;

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

        var mainWindow = new MainWindow(viewModel, isExplorerPreview: options.PreviewOnly);
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
        else if (!options.PreviewOnly)
        {
            mainWindow.ExplorerIntegrationToggleRequested += (_, _) =>
                SetExplorerIntegration(mainWindow, mainWindow.IsExplorerIntegrationRequested);
            SetExplorerIntegration(mainWindow, enabled: true);
        }
    }

    private void SetExplorerIntegration(MainWindow window, bool enabled)
    {
        _explorerIntegration?.Dispose();
        _explorerIntegration = null;
        if (!enabled)
        {
            window.SetExplorerIntegrationState(false, "탐색기 Space 미리보기가 꺼져 있습니다. 클릭하면 다시 켭니다.");
            return;
        }

        try
        {
            _explorerIntegration = new ExplorerPreviewIntegration(Dispatcher);
            _explorerIntegration.Failed += (_, message) =>
            {
                _explorerIntegration?.Dispose();
                _explorerIntegration = null;
                window.SetExplorerIntegrationState(false, message);
            };
            window.SetExplorerIntegrationState(true,
                "모아뷰가 켜져 있을 때 탐색기에서 파일 하나를 선택하고 Space를 누르세요.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            window.SetExplorerIntegrationState(false, $"탐색기 연동을 시작하지 못했습니다. {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _explorerIntegration?.Dispose();
        _explorerIntegration = null;
        base.OnExit(e);
    }

    public static StartupOptions ParseStartupOptions(
        IReadOnlyList<string>? constructorArgs,
        IReadOnlyList<string>? eventArgs)
    {
        var verifyStartup = false;
        var previewOnly = false;
        string? initialFilePath = null;

        InspectArgs(constructorArgs, ref verifyStartup, ref previewOnly, ref initialFilePath);
        InspectArgs(eventArgs, ref verifyStartup, ref previewOnly, ref initialFilePath);

        return new StartupOptions(initialFilePath, verifyStartup, previewOnly);
    }

    public static string? ResolveInitialFilePath(
        IReadOnlyList<string>? constructorArgs,
        IReadOnlyList<string>? eventArgs) =>
        ParseStartupOptions(constructorArgs, eventArgs).InitialFilePath;

    private static void InspectArgs(
        IReadOnlyList<string>? args,
        ref bool verifyStartup,
        ref bool previewOnly,
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

            if (string.Equals(arg.Trim(), PreviewSwitch, StringComparison.OrdinalIgnoreCase))
            {
                previewOnly = true;
                continue;
            }

            initialFilePath ??= arg;
        }
    }

    private void ScheduleStartupVerification(MainWindow mainWindow)
    {
        mainWindow.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(async () =>
            {
                try
                {
                    if (mainWindow.ViewModel.IsRiveVisible)
                        await mainWindow.WaitForRivePreviewAsync().WaitAsync(TimeSpan.FromSeconds(12));
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
            $"|RIVE={window.RivePlaybackStatus}" +
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
