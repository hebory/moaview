using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MoaView.Services;

namespace MoaView.Controls;

internal sealed class Model3DPreviewControl : SpecializedPreviewControl
{
    private const string PageUrl = Model3DResources.Origin + "index.html";
    private const string SecurityHeaders = "\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\n" +
        "Content-Security-Policy: default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "connect-src 'self' data: blob:; img-src 'self' data: blob:; worker-src 'none'; base-uri 'none'; frame-src 'none'";
    private readonly WebView2 _web = new() { Visibility = Visibility.Hidden };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Model3DResources _resources;
    private readonly string _extension;
    private object? _theme;
    private bool _pageReady;
    private bool _failed;
    internal event EventHandler? CloseRequested;

    internal Model3DPreviewControl(string? sourcePath, string name, byte[]? embedded)
    {
        _resources = new(sourcePath, name, embedded);
        _extension = Path.GetExtension(name).ToLowerInvariant();
        Children.Add(_web);
        Loaded += Start;
    }

    internal void ApplyAppearance(bool isDark, Color background, Color surface, Color text, Color muted, Color control, Color border)
    {
        _theme = new { type = "theme", isDark, background = Css(background), surface = Css(surface), text = Css(text),
            muted = Css(muted), control = Css(control), border = Css(border) };
        if (IsDisposed || _failed) return;
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(background.R, background.G, background.B);
        if (_pageReady) Send(_theme);
    }

    private static string Css(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private async void Start(object sender, RoutedEventArgs e)
    {
        Loaded -= Start;
        try
        {
            _ = WatchTimeoutAsync(_lifetime.Token);
            var environment = await CoreWebView2Environment.CreateAsync(null,
                MoaViewLocalAppData.GetPath("ModelWebView"));
            if (IsDisposed || _failed) return;
            await _web.EnsureCoreWebView2Async(environment);
            if (IsDisposed || _failed) return;
            ConfigureBrowser(_web.CoreWebView2);
            _web.CoreWebView2.Navigate(PageUrl);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException || FilePreviewService.IsPreviewError(ex))
        {
            Fail(ex is WebView2RuntimeNotFoundException
                ? "3D 미리보기에는 Microsoft Edge WebView2 Runtime이 필요합니다."
                : "3D 미리보기 화면을 시작하지 못했습니다. 파일을 다시 열어 주세요.");
        }
    }

    private void ConfigureBrowser(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri != PageUrl;
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += async (_, e) =>
        {
            using var deferral = e.GetDeferral();
            try
            {
                var resource = !IsDisposed && !_failed && e.Request.Method == "GET" &&
                    Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)
                    ? await _resources.ReadAsync(uri, _lifetime.Token) : null;
                if (IsDisposed || _failed) return;
                e.Response = resource is null
                    ? core.Environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty)
                    : core.Environment.CreateWebResourceResponse(new MemoryStream(resource.Bytes, writable: false), 200, "OK",
                        "Content-Type: " + resource.ContentType + SecurityHeaders);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (FilePreviewService.IsPreviewError(ex))
            {
                if (IsDisposed || _failed) return;
                e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Unavailable", string.Empty);
                if (ex is PreviewLimitException) Fail(ex.Message);
            }
        };
        core.ProcessFailed += (_, _) => Fail("3D 미리보기 프로세스가 종료되었습니다. 파일을 다시 열어 주세요.");
        core.WebMessageReceived += (_, e) =>
        {
            if (IsDisposed || _failed || e.Source != PageUrl) return;
            try
            {
                using var json = JsonDocument.Parse(e.WebMessageAsJson);
                var root = json.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "pageReady" when !_pageReady:
                        _pageReady = true;
                        if (_theme is not null) Send(_theme);
                        Send(new { type = "load", url = _resources.ModelUrl, extension = _extension });
                        break;
                    case "ready" when !Ready.IsCompleted:
                        _web.Visibility = Visibility.Visible;
                        ShowReady();
                        break;
                    case "error":
                        var message = root.TryGetProperty("message", out var value) ? value.GetString() : null;
                        Fail(string.IsNullOrWhiteSpace(message) ? "3D 파일이 손상되었거나 지원하지 않는 인코딩입니다." : message[..Math.Min(500, message.Length)]);
                        break;
                    case "close":
                        Dispatcher.BeginInvoke(() => { if (!IsDisposed) CloseRequested?.Invoke(this, EventArgs.Empty); });
                        break;
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
        };
    }

    private void Send(object message)
    {
        if (IsDisposed || _failed) return;
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
        catch (InvalidOperationException) { }
    }

    private async Task WatchTimeoutAsync(CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellation);
            if (!Ready.IsCompleted) Fail("제한 시간 안에 3D 모델을 표시하지 못했습니다. 모델 크기나 포함 리소스를 줄여 다시 열어 주세요.");
        }
        catch (OperationCanceledException) { }
    }

    private void Fail(string message)
    {
        if (IsDisposed || _failed) return;
        _failed = true;
        _lifetime.Cancel();
        _web.Visibility = Visibility.Collapsed;
        ShowError(message);
        Dispatcher.BeginInvoke(() => { if (!IsDisposed) _web.Dispose(); });
    }

    protected override void ReleaseResources()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _web.Dispose();
    }
}
