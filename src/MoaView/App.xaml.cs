using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MoaView.Services;
using MoaView.ViewModels;

namespace MoaView;

public readonly record struct StartupOptions(string? InitialFilePath, bool VerifyStartupAndClose, bool PreviewOnly, bool Background = false);

public partial class App : Application
{
    public const string VerifyStartupSwitch = "--verify-startup";
    public const string PreviewSwitch = "--preview";
    public const string BackgroundSwitch = "--background";

    private readonly IReadOnlyList<string> _startupArgs;
    private ExplorerPreviewIntegration? _explorerIntegration;
    private readonly ResidentInstance? _residentInstance;
    private WindowsStartupRegistration? _startupRegistration;
    private TrayIcon? _trayIcon;
    private MainWindow? _regularWindow;
    private volatile bool _exiting;
    private string _explorerMessage = "";

    public App()
        : this(Array.Empty<string>())
    {
    }

    public App(IReadOnlyList<string> startupArgs, ResidentInstance? residentInstance = null)
    {
        _startupArgs = startupArgs ?? Array.Empty<string>();
        _residentInstance = residentInstance;
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

        if (!options.VerifyStartupAndClose && !options.PreviewOnly)
        {
            StartResident(options);
            return;
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
    }

    private void StartResident(StartupOptions options)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _startupRegistration = new WindowsStartupRegistration();
        _trayIcon = new TrayIcon(
            () => Post(() => OpenMainWindow()),
            () => Post(() => SetExplorerIntegration(_explorerIntegration is null)),
            () => Post(ExitResident));
        SetExplorerIntegration(enabled: true);
        _residentInstance?.StartListening(request =>
        {
            if (_exiting || Dispatcher.HasShutdownStarted)
                throw new InvalidOperationException("모아뷰가 종료 중입니다. 다시 실행해 주세요.");
            Post(() =>
            {
                if (!request.Background || !string.IsNullOrWhiteSpace(request.FilePath))
                    OpenMainWindow(request.FilePath);
            });
        });
        if (!options.Background || !string.IsNullOrWhiteSpace(options.InitialFilePath))
            OpenMainWindow(options.InitialFilePath);
    }

    private void Post(Action action)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(() => { if (!_exiting) action(); }));
    }

    private void OpenMainWindow(string? filePath = null)
    {
        if (_regularWindow is null)
        {
            var window = new MainWindow(new MainWindowViewModel());
            _regularWindow = window;
            MainWindow = window;
            window.ExplorerIntegrationToggleRequested += (_, _) =>
                SetExplorerIntegration(window.IsExplorerIntegrationRequested);
            window.SettingsOpened += (_, _) => RefreshStartupState();
            window.WindowsStartupToggleRequested += (_, _) =>
            {
                _startupRegistration!.TrySetEnabled(window.IsWindowsStartupRequested, out var error);
                RefreshStartupState(error);
            };
            window.Closed += (_, _) =>
            {
                // Closing releases media/WebView resources; the tray owns the app lifetime.
                _regularWindow = null;
                MainWindow = null;
                if (!_exiting && !Dispatcher.HasShutdownStarted) _trayIcon?.ShowCloseHint();
            };
            window.SetExplorerIntegrationState(_explorerIntegration is not null, _explorerMessage);
            RefreshStartupState();
        }

        if (!string.IsNullOrWhiteSpace(filePath)) _regularWindow.ViewModel.OpenFile(filePath);
        _regularWindow.Show();
        if (_regularWindow.WindowState == WindowState.Minimized) _regularWindow.WindowState = WindowState.Normal;
        _regularWindow.Activate();
    }

    private void RefreshStartupState(string? error = null)
    {
        if (_startupRegistration is null || _regularWindow is null) return;
        _startupRegistration.TryGetEnabled(out var enabled, out var readError);
        _regularWindow.SetWindowsStartupState(enabled, error ?? readError);
    }

    private void ExitResident()
    {
        _exiting = true;
        Shutdown();
    }

    private void UpdateExplorerState(string message)
    {
        _explorerMessage = message;
        var enabled = _explorerIntegration is not null;
        _regularWindow?.SetExplorerIntegrationState(enabled, message);
        _trayIcon?.SetExplorerState(enabled);
    }

    private void SetExplorerIntegration(bool enabled)
    {
        _explorerIntegration?.Dispose();
        _explorerIntegration = null;
        if (!enabled)
        {
            UpdateExplorerState("탐색기 Space 미리보기가 꺼져 있습니다. 체크하면 다시 켭니다.");
            return;
        }

        try
        {
            _explorerIntegration = new ExplorerPreviewIntegration(Dispatcher);
            _explorerIntegration.Failed += (_, message) =>
            {
                _explorerIntegration?.Dispose();
                _explorerIntegration = null;
                UpdateExplorerState(message);
                _trayIcon?.ShowError(message);
            };
            UpdateExplorerState("창을 닫아도 트레이에서 실행됩니다. 탐색기에서 파일 하나를 선택하고 Space를 누르세요.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            var message = $"탐색기 연동을 시작하지 못했습니다. {ex.Message}";
            UpdateExplorerState(message);
            _trayIcon?.ShowError(message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _explorerIntegration?.Dispose();
        _explorerIntegration = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _residentInstance?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _exiting = true;
        base.OnSessionEnding(e);
    }

    public static StartupOptions ParseStartupOptions(
        IReadOnlyList<string>? constructorArgs,
        IReadOnlyList<string>? eventArgs)
    {
        var verifyStartup = false;
        var previewOnly = false;
        var background = false;
        string? initialFilePath = null;

        InspectArgs(constructorArgs, ref verifyStartup, ref previewOnly, ref background, ref initialFilePath);
        InspectArgs(eventArgs, ref verifyStartup, ref previewOnly, ref background, ref initialFilePath);

        return new StartupOptions(initialFilePath, verifyStartup, previewOnly, background);
    }

    public static string? ResolveInitialFilePath(
        IReadOnlyList<string>? constructorArgs,
        IReadOnlyList<string>? eventArgs) =>
        ParseStartupOptions(constructorArgs, eventArgs).InitialFilePath;

    private static void InspectArgs(
        IReadOnlyList<string>? args,
        ref bool verifyStartup,
        ref bool previewOnly,
        ref bool background,
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

            if (string.Equals(arg.Trim(), BackgroundSwitch, StringComparison.OrdinalIgnoreCase))
            {
                background = true;
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
                    if (mainWindow.ViewModel.IsSpecializedVisible)
                        await mainWindow.WaitForSpecializedPreviewAsync().WaitAsync(TimeSpan.FromSeconds(12));
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
        var specializedReady = window.WaitForSpecializedPreviewAsync();
        var specializedStatus = vm.IsSpecializedVisible && specializedReady.IsCompletedSuccessfully
            ? specializedReady.Result ? "ready" : "error"
            : "none";
        var reportLine =
            $"MOAVIEW_STARTUP_VERIFIED={verified}" +
            $"|STATE={vm.State}" +
            $"|VISIBLE={window.IsVisible}" +
            $"|TITLE={window.Title}" +
            $"|BADGE={vm.NoticeBadgeText}" +
            $"|RIVE={window.RivePlaybackStatus}" +
            $"|SPECIALIZED={specializedStatus}" +
            $"|PIXELS={result.PixelWidth}x{result.PixelHeight}" +
            $"|THEME={window.CurrentAppTheme}" +
            $"|MDTHEME={vm.SelectedMarkdownTheme.Theme}";

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
