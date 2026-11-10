using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MoaView.Controls;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class VideoPlaybackTests
{
    private const string FixtureName = "playback.mp4";

    [Fact]
    public void Video_AutoplaysPresentedFramesAndSupportsPausedSeekAndControls()
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

                var local = FindLocalPreview(window);
                var playback = Assert.IsType<VideoFramePlayback>(local.Playback);
                var image = Assert.IsType<Image>(playback.View);
                var play = FindControl<Button>(window, "일시 정지");
                var position = FindControl<Slider>(window, "재생 위치");
                Assert.True(playback.IsFrameReady, "The video must not be ready before its first decoded frame is presented.");
                Assert.Equal(160, playback.NaturalVideoWidth);
                Assert.Equal(90, playback.NaturalVideoHeight);
                Assert.True(playback.PresentedFrameCount > 0);
                Assert.False(playback.IsBuffering);
                Assert.Equal("일시 정지", AutomationProperties.GetName(play));

                var initialPosition = playback.Position;
                var firstFrame = PixelHash(image);
                Assert.True(firstFrame.HasContent, "The first decoded video frame is empty.");
                var presentedFrames = playback.PresentedFrameCount;
                PumpUntil(() => playback.Position > initialPosition + TimeSpan.FromMilliseconds(350) &&
                    playback.PresentedFrameCount > presentedFrames && PixelHash(image).Hash != firstFrame.Hash,
                    TimeSpan.FromSeconds(3), "Video did not autoplay and present changing frames.");
                PumpUntil(() => position.Value > 0.25, TimeSpan.FromSeconds(1),
                    "Timeline did not advance with autoplay.");

                Assert.True(window.HandleKeyInput(Key.K));
                var pausedPosition = playback.Position;
                PumpFor(TimeSpan.FromMilliseconds(450));
                Assert.InRange(Math.Abs((playback.Position - pausedPosition).TotalMilliseconds), 0, 80);
                Assert.Equal("재생", AutomationProperties.GetName(play));

                var beforeSeek = PixelHash(image);
                var frameCountBeforeSeek = playback.PresentedFrameCount;
                position.Value = 2;
                position.Value = 3;
                position.Value = 2;
                PumpUntil(() => !playback.IsBuffering && playback.PresentedFrameCount > frameCountBeforeSeek &&
                    PixelHash(image).Hash != beforeSeek.Hash,
                    TimeSpan.FromSeconds(3), "A paused seek did not present the selected video frame.");
                Assert.InRange(playback.Position.TotalSeconds, 1.8, 2.2);
                Assert.Equal("재생", AutomationProperties.GetName(play));

                Assert.True(window.HandleKeyInput(Key.K));
                var positionAfterSeek = playback.Position;
                var speed = FindControl<ComboBox>(window, "재생 속도");
                speed.SelectedItem = 1.5;
                Assert.Equal(1.5, playback.SpeedRatio);
                PumpUntil(() => playback.Position > positionAfterSeek + TimeSpan.FromMilliseconds(350),
                    TimeSpan.FromSeconds(3), "Video did not resume from the paused seek.");
                Assert.True(window.HandleKeyInput(Key.M));
                Assert.True(playback.IsMuted);
                Assert.True(window.HandleKeyInput(Key.M));
                Assert.False(playback.IsMuted);
                Assert.Equal("일시 정지", AutomationProperties.GetName(play));

                Assert.True(window.HandleKeyInput(Key.K));
                var resumedPosition = playback.Position;
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.InRange(Math.Abs((playback.Position - resumedPosition).TotalMilliseconds), 0, 80);

                window.Close();
                Assert.Null(playback.Source);
                Assert.Null(image.Source);
            }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        });
    }

    [Fact]
    public void Video_ReachesEndAndReplaysWithNewFrames()
    {
        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = CreateWindow();
            try
            {
                window.Show();
                window.ViewModel.OpenFile(FixturePath());
                Assert.True(Complete(window.WaitForSpecializedPreviewAsync(), TimeSpan.FromSeconds(18)));
                var playback = Assert.IsType<VideoFramePlayback>(FindLocalPreview(window).Playback);
                var position = FindControl<Slider>(window, "재생 위치");
                var play = FindControl<Button>(window, "일시 정지");
                position.Value = 5.4;
                PumpUntil(() => AutomationProperties.GetName(play) == "처음부터 다시 재생",
                    TimeSpan.FromSeconds(4), "Video did not reach its end after seeking.");
                var finalFrame = PixelHash((Image)playback.View);
                Assert.True(window.HandleKeyInput(Key.K));
                PumpUntil(() => playback.Position.TotalSeconds is > 0.1 and < 2 &&
                    PixelHash((Image)playback.View).Hash != finalFrame.Hash,
                    TimeSpan.FromSeconds(3), "Replay did not restart with a new frame.");
            }
            finally { window.Close(); }
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

                var playback = Assert.IsType<VideoFramePlayback>(FindLocalPreview(window).Playback);
                Assert.Null(playback.Source);
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
            try
            {
                window.Show();
                window.ViewModel.OpenFile(path);
                var local = FindLocalPreview(window);
                PumpUntil(() => local.Playback is VideoFramePlayback && local.Playback.Source is not null,
                    TimeSpan.FromSeconds(3), "Video source was not attached.");
                var playback = Assert.IsType<VideoFramePlayback>(local.Playback);
                var play = FindControl<Button>(window, "재생");
                // Close before the native decoder presents its first frame.
                window.Close();
                Assert.Null(playback.Source);
                Assert.Null(((Image)playback.View).Source);
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.Null(playback.Source);
                Assert.Equal("재생", AutomationProperties.GetName(play));
                Assert.False(play.IsEnabled);
            }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        });
    }

    private static MainWindow CreateWindow() =>
        new(new MainWindowViewModel()) { ShowActivated = false, ShowInTaskbar = false };

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);

    private static LocalMediaPreviewControl FindLocalPreview(MainWindow window) =>
        Descendants((DependencyObject)window.FindName("SpecializedViewport"))
            .OfType<LocalMediaPreviewControl>().Single();

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

    [Fact]
    public void VideoFramePlayback_PresentsInitialFrameWhilePausedThenTracksPlaybackAndPause()
    {
        StaTestRunner.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            using var temp = new TempTestDirectory();
            var path = temp.GetPath(FixtureName);
            File.Copy(FixturePath(), path);

            using var playback = new VideoFramePlayback();
            var view = Assert.IsType<Image>(playback.View);
            var window = new Window { ShowActivated = false, ShowInTaskbar = false, Width = 480, Height = 320 };
            var root = new Grid();
            root.Children.Add(view);
            window.Content = root;
            try
            {
                window.Show();
                var opened = false;
                playback.Opened += (_, _) => opened = true;
                playback.Source = new Uri(path, UriKind.Absolute);

                PumpUntil(() => opened && playback.IsFrameReady && view.Source is BitmapSource,
                    TimeSpan.FromSeconds(16), "The paused video did not present an initial decoded frame.");
                Assert.Equal(160, playback.NaturalVideoWidth);
                Assert.Equal(90, playback.NaturalVideoHeight);
                var firstFrame = PixelHash(view);
                Assert.True(firstFrame.HasContent, "The paused initial video frame contains no image pixels.");
                var firstFrameCount = playback.PresentedFrameCount;
                var initialPosition = playback.Position;
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.InRange(Math.Abs((playback.Position - initialPosition).TotalMilliseconds), 0, 80);
                Assert.Equal(firstFrameCount, playback.PresentedFrameCount);

                playback.Play();
                PumpUntil(() => playback.PresentedFrameCount > firstFrameCount &&
                    PixelHash(view).Hash != firstFrame.Hash,
                    TimeSpan.FromSeconds(3), "Play did not produce a new decoded frame.");
                Assert.True(playback.Position > initialPosition);

                playback.Pause();
                var pausedPosition = playback.Position;
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.InRange(Math.Abs((playback.Position - pausedPosition).TotalMilliseconds), 0, 80);
            }
            finally
            {
                playback.Dispose();
                if (window.IsVisible) window.Close();
            }
        });
    }

    private static FramePixels PixelHash(Image image)
    {
        if (image.Source is not BitmapSource bitmap || bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1)
            return new("", false);
        var pixels = new byte[bitmap.PixelHeight * bitmap.PixelWidth * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var distinct = new HashSet<(byte B, byte G, byte R, byte A)>();
        for (var i = 0; i < pixels.Length && distinct.Count < 4; i += 4)
            distinct.Add((pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]));
        return new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)), distinct.Count > 1);
    }

    private sealed record FramePixels(string Hash, bool HasContent);

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
