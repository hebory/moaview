using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class VideoPlaybackTests
{
    private const string FixtureName = "playback.mp4";

    [Fact]
    public void Video_WaitsForReadySurfaceThenAutoplaysAndSupportsPauseResume()
    {
        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            using var copiedFixture = new TempTestDirectory();
            var path = copiedFixture.GetPath(FixtureName);
            File.Copy(FixturePath(), path);
            var window = CreateWindow();
            try
            {
                window.Show();
                window.ViewModel.OpenFile(path);
                Assert.True(Complete(window.WaitForSpecializedPreviewAsync(), TimeSpan.FromSeconds(18)), "Video did not finish opening.");

                var media = FindMedia(window);
                var play = FindControl<Button>(window, "일시 정지");
                var position = FindControl<Slider>(window, "재생 위치");
                Assert.True(media.ScrubbingEnabled, "Video preroll must be enabled before opening.");
                Assert.Equal(160, media.NaturalVideoWidth);
                Assert.Equal(90, media.NaturalVideoHeight);
                Assert.True(media.IsLoaded && media.IsVisible);
                Assert.True(media.ActualWidth > 0 && media.ActualHeight > 0);
                Assert.False(media.IsBuffering);
                Assert.Equal("일시 정지", AutomationProperties.GetName(play));

                var initialPosition = media.Position;
                PumpUntil(() => media.Position > initialPosition + TimeSpan.FromMilliseconds(350),
                    TimeSpan.FromSeconds(3), "Video did not autoplay after the surface became ready.");
                PumpUntil(() => position.Value > 0.25, TimeSpan.FromSeconds(1),
                    "Timeline did not advance with autoplay.");

                Assert.True(window.HandleKeyInput(Key.K));
                var pausedPosition = media.Position;
                PumpFor(TimeSpan.FromMilliseconds(450));
                Assert.InRange(Math.Abs((media.Position - pausedPosition).TotalMilliseconds), 0, 180);
                Assert.Equal("재생", AutomationProperties.GetName(play));

                Assert.True(window.HandleKeyInput(Key.K));
                PumpUntil(() => media.Position > pausedPosition + TimeSpan.FromMilliseconds(350),
                    TimeSpan.FromSeconds(3), "Video did not resume after pause.");
                Assert.Equal("일시 정지", AutomationProperties.GetName(play));

                window.Close();
                Assert.Null(media.Source);
            }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        });
    }

    [Fact]
    public void VideoContainerWithAudioOnly_IsRejectedAndSourceReleased()
    {
        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            using var temp = new TempTestDirectory();
            var path = temp.GetPath("audio-only.mp4");
            CreateSilentWave(path);
            var window = CreateWindow();
            try
            {
                window.Show();
                window.ViewModel.OpenFile(path);
                Assert.False(Complete(window.WaitForSpecializedPreviewAsync(), TimeSpan.FromSeconds(8)),
                    "An audio-only source with an MP4 extension must not be reported as a ready video.");

                var media = FindMedia(window);
                Assert.Null(media.Source);
                Assert.Contains("영상 트랙을 읽을 수 없습니다", FindStatus(window));
            }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        });
    }

    [Fact]
    public void ClosingWhileVideoOpens_ReleasesSourceAndPreventsLateAutoplay()
    {
        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            using var temp = new TempTestDirectory();
            var path = temp.GetPath(FixtureName);
            File.Copy(FixturePath(), path);
            var window = CreateWindow();
            window.Show();
            window.ViewModel.OpenFile(path);
            PumpUntil(() => CurrentMedia(window)?.Source is not null, TimeSpan.FromSeconds(3), "Video source was not attached.");
            var media = CurrentMedia(window)!;
            var play = FindControl<Button>(window, "재생");
            // Close synchronously as soon as the native source has started opening, before
            // MediaOpened or the two composition passes can complete the autoplay transition.
            window.Close();
            Assert.Null(media.Source);
            PumpFor(TimeSpan.FromSeconds(1));
            Assert.Null(media.Source);
            Assert.Equal("재생", AutomationProperties.GetName(play));
            Assert.False(play.IsEnabled);
        });
    }

    private static MainWindow CreateWindow() =>
        new(new MainWindowViewModel()) { ShowActivated = false, ShowInTaskbar = false };

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);

    private static MediaElement FindMedia(MainWindow window) =>
        Descendants((DependencyObject)window.FindName("SpecializedViewport"))
            .OfType<MediaElement>().Single();

    private static MediaElement? CurrentMedia(MainWindow window) =>
        Descendants((DependencyObject)window.FindName("SpecializedViewport"))
            .OfType<MediaElement>().FirstOrDefault(media => media.Source is not null);

    private static T FindControl<T>(MainWindow window, string automationName) where T : DependencyObject =>
        Descendants((DependencyObject)window.FindName("SpecializedViewport"))
            .OfType<T>().Single(control => AutomationProperties.GetName(control) == automationName);

    private static string FindStatus(MainWindow window) =>
        Descendants((DependencyObject)window.FindName("SpecializedViewport"))
            .OfType<TextBlock>().Select(text => text.Text)
            .First(text => text.Contains("영상 트랙을 읽을 수 없습니다", StringComparison.Ordinal));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static T Complete<T>(Task<T> task, TimeSpan timeout)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timer = new DispatcherTimer { Interval = timeout };
            timer.Tick += (_, _) => frame.Continue = false;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        Assert.True(task.IsCompleted, $"Video preview did not complete within {timeout}.");
        return task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        var deadline = DateTime.UtcNow + timeout;
        timer.Tick += (_, _) =>
        {
            if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(condition(), message);
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void CreateSilentWave(string path)
    {
        const int sampleRate = 8000;
        const int sampleCount = sampleRate * 2;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + sampleCount);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate);
        writer.Write((ushort)1);
        writer.Write((ushort)8);
        writer.Write("data"u8);
        writer.Write(sampleCount);
        writer.Write(Enumerable.Repeat((byte)128, sampleCount).ToArray());
    }

}
