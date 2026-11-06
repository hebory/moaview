using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class RivePreviewTests
{
    [Fact]
    public void Rive_RealRuntimeRendersBundledFixtureAndSupportsPauseAndReload()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "data_bind_runtime_test.riv");
        Assert.True(File.Exists(fixture));
        using var temp = new TempTestDirectory();
        var appearance = new AppAppearance(new FileAppThemeSettings(temp.GetPath("appearance.json")));
        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var model = new MainWindowViewModel();
            var window = new MainWindow(model, appearance: appearance) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                model.OpenFile(fixture);
                Assert.Equal(PreviewStateKind.RiveLoaded, model.State);
                var host = (Grid)window.FindName("RiveViewport");
                var load = window.WaitForRivePreviewAsync();
                var ready = Complete(load);
                var errorText = string.Join(" ", ((Grid)host.Children[0]).Children.OfType<TextBlock>().Select(t => t.Text));
                Assert.True(ready, window.RivePlaybackStatus + ": " + errorText);
                Assert.True(window.VerifyRenderedState());
                var web = ((Grid)host.Children[0]).Children.OfType<WebView2>().Single();
                Complete(Task.Delay(100));
                var hasPixels = Complete(web.ExecuteScriptAsync("Array.from(document.getElementById('canvas').getContext('2d').getImageData(0,0,100,100).data).some(value => value !== 0)"));
                // The fixture can be centered away from the top-left; test the full drawing surface as well.
                if (hasPixels != "true")
                    hasPixels = Complete(web.ExecuteScriptAsync("(() => { const c=document.getElementById('canvas'); return c.getContext('2d').getImageData(0,0,c.width,c.height).data.some(value=>value!==0); })()"));
                Assert.Equal("true", hasPixels);
                Assert.Equal("true", Complete(web.ExecuteScriptAsync("document.getElementById('artboard').options.length > 0")));
                Assert.Equal("\"재생\"", Complete(web.ExecuteScriptAsync("document.getElementById('pause').click(); document.getElementById('pause').textContent")));
                appearance.Select(AppTheme.Light);
                Complete(WaitForLightTheme());
                Assert.Same(load, window.WaitForRivePreviewAsync());
                Assert.Equal("\"재생\"", Complete(web.ExecuteScriptAsync("document.getElementById('pause').textContent")));
                Assert.False(model.CanExecuteConversion);
                model.OpenFile(fixture);
                Assert.True(Complete(window.WaitForRivePreviewAsync()));
                Assert.Equal("ready", window.RivePlaybackStatus);

                async Task WaitForLightTheme()
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    while (await web.ExecuteScriptAsync("document.documentElement.style.colorScheme") != "\"light\"")
                        await Task.Delay(20, timeout.Token);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Rive_DamagedBinaryShowsFailureAndCanRecoverToText()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("broken.riv");
        File.WriteAllBytes(path, [82, 73, 86, 69, 7, 0, 0, 0]);
        File.WriteAllText(temp.GetPath("recovered.txt"), "복구됨");
        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var model = new MainWindowViewModel();
            var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                model.OpenFile(path);
                Assert.False(Complete(window.WaitForRivePreviewAsync()));
                Assert.Equal("error", window.RivePlaybackStatus);
                Assert.True(window.VerifyRenderedState());
                model.OpenFile(temp.GetPath("recovered.txt"));
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal("none", window.RivePlaybackStatus);
                Assert.True(window.VerifyRenderedState());
            }
            finally { window.Close(); }
        });
    }

    private static T Complete<T>(Task<T> task)
    {
        Complete((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            timeout.Tick += (_, _) => frame.Continue = false;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            timeout.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();
        }
        Assert.True(task.IsCompleted, "Rive 검증이 제한 시간 안에 완료되지 않았습니다.");
        task.GetAwaiter().GetResult();
    }
}
