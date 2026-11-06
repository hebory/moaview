using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MoaView.Services;

namespace MoaView.Controls;

internal sealed class BrowserImagePreviewControl : SpecializedPreviewControl
{
    private const string PageUrl = "https://moaview-image.invalid/index.html";
    private const string ImageUrl = "https://moaview-image.invalid/image";
    private const string Page = """
        <!doctype html><html><head><meta charset="utf-8">
        <style>
        body { margin:0; height:100vh; display:grid; place-items:center; background:#121722; }
        img { max-width:100%; max-height:100vh; }
        </style></head><body><script>
        const image = new Image();
        image.alt = '';
        image.onload = () => chrome.webview.postMessage('ready');
        image.onerror = () => chrome.webview.postMessage('error');
        document.body.append(image);
        image.src = '/image';
        addEventListener('keydown', event => {
          if ((event.code === 'Space' || event.key === 'Escape') && !event.repeat) {
            event.preventDefault();
            chrome.webview.postMessage('close');
          }
        });
        </script></body></html>
        """;
    private const string PageHeaders = "Content-Type: text/html; charset=utf-8\r\n" +
        "Content-Security-Policy: default-src 'none'; img-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; base-uri 'none'";

    private readonly WebView2 _web = new() { Visibility = Visibility.Hidden };
    private readonly CancellationTokenSource _lifetime = new();
    public event EventHandler? CloseRequested;

    public BrowserImagePreviewControl(byte[] bytes, string contentType)
    {
        Children.Add(_web);
        Loaded += Start;

        async void Start(object sender, RoutedEventArgs e)
        {
            Loaded -= Start;
            var cancellation = _lifetime.Token;
            try
            {
                _ = WatchTimeoutAsync(cancellation);
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoaView", "ImageWebView"));
                if (IsDisposed || Ready.IsCompleted) return;
                await _web.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Ready.IsCompleted) return;
                ConfigureBrowser(_web.CoreWebView2, bytes, contentType);
                _web.CoreWebView2.Navigate(PageUrl);
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException || FilePreviewService.IsPreviewError(ex))
            {
                Fail(ex is WebView2RuntimeNotFoundException
                    ? "이 이미지를 보려면 Microsoft Edge WebView2 Runtime이 필요합니다."
                    : "이미지 미리보기 화면을 시작하지 못했습니다.");
            }
        }
    }

    private void ConfigureBrowser(CoreWebView2 core, byte[] bytes, string contentType)
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
        core.WebResourceRequested += (_, e) =>
        {
            var environment = core.Environment;
            if (e.Request.Uri == PageUrl && e.ResourceContext == CoreWebView2WebResourceContext.Document)
                e.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(Page)), 200, "OK", PageHeaders);
            else if (e.Request.Uri == ImageUrl && e.ResourceContext == CoreWebView2WebResourceContext.Image)
                e.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes, writable: false), 200, "OK",
                    $"Content-Type: {contentType}\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store");
            else
                e.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
        };
        core.ProcessFailed += (_, _) => Fail("이미지 미리보기 프로세스가 종료되었습니다.");
        core.WebMessageReceived += (_, e) =>
        {
            if (IsDisposed || e.Source != PageUrl) return;
            switch (e.TryGetWebMessageAsString())
            {
                case "ready" when !Ready.IsCompleted:
                    _web.Visibility = Visibility.Visible;
                    ShowReady();
                    break;
                case "error":
                    Fail("이미지를 표시할 수 없습니다. 파일이 손상되었거나 지원하는 인코딩이 아닐 수 있습니다.");
                    break;
                case "close":
                    Dispatcher.BeginInvoke(() => { if (!IsDisposed) CloseRequested?.Invoke(this, EventArgs.Empty); });
                    break;
            }
        };
    }

    private async Task WatchTimeoutAsync(CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
            if (!Ready.IsCompleted) Fail("제한 시간 안에 이미지를 표시하지 못했습니다.");
        }
        catch (OperationCanceledException) { }
    }

    private void Fail(string message)
    {
        if (IsDisposed) return;
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
