using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MoaView.Controls;

public sealed class RivePreviewControl : Grid, IDisposable
{
    private const string Origin = "https://moaview-rive.invalid/";
    private readonly TextBlock _status = new() { Text = "Rive 재생 화면을 준비하고 있습니다.",
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
    private WebView2? _web;
    private readonly HashSet<WebView2> _retiredBrowsers = new();
    private int _generation;
    private bool _disposed;
    private bool _pageReady;
    private byte[]? _data;
    private bool _isDark = true;
    private Color _background = Color.FromRgb(20, 22, 26);
    private Color _surface = Color.FromRgb(32, 36, 44);
    private Color _text = Color.FromRgb(229, 231, 235);
    private Color _muted = Color.FromRgb(181, 197, 221);
    private Color _control = Color.FromRgb(48, 56, 70);
    private Color _border = Color.FromRgb(74, 85, 104);
    private TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string PlaybackStatus { get; private set; } = "loading";
    public Task<bool> Ready => _ready.Task;
    public event EventHandler? CloseRequested;

    public RivePreviewControl()
    {
        SetResourceReference(BackgroundProperty, "AppPreviewBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedBrush");
        Children.Add(_status);
    }

    public void ApplyAppearance(bool isDark, Color background, Color surface, Color text, Color muted, Color control, Color border)
    {
        _isDark = isDark;
        _background = background;
        _surface = surface;
        _text = text;
        _muted = muted;
        _control = control;
        _border = border;
        SendAppearance();
    }

    public async Task LoadAsync(byte[] data)
    {
        var generation = ++_generation;
        _ready.TrySetResult(false);
        _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _data = data;
        PlaybackStatus = "loading";
        try
        {
            if (_web is null)
            {
                _web = new WebView2();
                Children.Insert(0, _web);
                var environment = await CoreWebView2Environment.CreateAsync(null,
                    MoaView.Services.MoaViewLocalAppData.GetPath("RiveWebView"));
                if (_disposed) return;
                await _web.EnsureCoreWebView2Async(environment);
                if (_disposed) return;
                ConfigureBrowser(_web.CoreWebView2);
                _web.CoreWebView2.Navigate(Origin + "index.html");
            }
            else if (_pageReady) SendData();

            await Task.Delay(TimeSpan.FromSeconds(10));
            if (!_disposed && generation == _generation && PlaybackStatus == "loading")
                Fail("Rive 파일을 제한 시간 안에 표시하지 못했습니다. 다른 파일을 선택한 뒤 다시 열어 주세요.");
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or
            System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!_disposed && generation == _generation)
                Fail(ex is WebView2RuntimeNotFoundException
                    ? "Rive 미리보기에는 Microsoft Edge WebView2 Runtime이 필요합니다. 설치 후 다시 실행해 주세요."
                    : "Rive 재생 화면을 시작하지 못했습니다. 앱을 다시 실행해 주세요.");
        }
    }

    private void ConfigureBrowser(CoreWebView2 core)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "Rive");
        core.SetVirtualHostNameToFolderMapping("moaview-rive.invalid", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri != Origin + "index.html";
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "moaview-rive.invalid")
                e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(), 403, "Blocked", "Content-Type: text/plain");
        };
        core.ProcessFailed += (_, _) =>
        {
            if (ReferenceEquals(_web?.CoreWebView2, core)) Fail("Rive 재생 프로세스가 종료되었습니다. 파일을 다시 열어 주세요.");
        };
        core.WebMessageReceived += (_, e) =>
        {
            if (_disposed || !ReferenceEquals(_web?.CoreWebView2, core) || e.Source != Origin + "index.html") return;
            try
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                var root = message.RootElement;
                var type = root.GetProperty("type").GetString();
                if (type == "pageReady") { _pageReady = true; SendAppearance(); SendData(); return; }
                if (root.GetProperty("id").GetInt32() != _generation) return;
                if (type == "close") { CloseRequested?.Invoke(this, EventArgs.Empty); return; }
                if (type == "error") { Fail(root.GetProperty("message").GetString() ?? "Rive 파일을 표시하지 못했습니다."); return; }
                if (type == "ready")
                {
                    PlaybackStatus = "ready";
                    _status.Visibility = Visibility.Collapsed;
                    _ready.TrySetResult(true);
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
        };
    }

    private void SendAppearance()
    {
        if (_disposed || !_pageReady || _web?.CoreWebView2 is null) return;
        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "theme", isDark = _isDark, background = CssColor(_background), surface = CssColor(_surface),
                text = CssColor(_text), muted = CssColor(_muted), control = CssColor(_control), border = CssColor(_border),
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static string CssColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void SendData()
    {
        if (_disposed || _data is null || _web?.CoreWebView2 is null) return;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "load", id = _generation, base64 = Convert.ToBase64String(_data) }));
    }

    private void Fail(string message)
    {
        if (_disposed) return;
        PlaybackStatus = "error";
        _status.Text = message;
        _status.Visibility = Visibility.Visible;
        _ready.TrySetResult(false);
        // Release a failed or timed-out renderer, including a parser stuck in the browser process.
        var failedWeb = _web;
        _web = null;
        _pageReady = false;
        if (failedWeb is not null)
        {
            _retiredBrowsers.Add(failedWeb);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_retiredBrowsers.Remove(failedWeb)) { Children.Remove(failedWeb); failedWeb.Dispose(); }
            }));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _data = null;
        ++_generation;
        _ready.TrySetResult(false);
        _web?.Dispose();
        _web = null;
        foreach (var browser in _retiredBrowsers) browser.Dispose();
        _retiredBrowsers.Clear();
        Children.Clear();
    }
}
