using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MoaView.Services;

namespace MoaView.Controls;

internal sealed class LocalMediaPreviewControl : SpecializedPreviewControl
{
    private readonly MediaElement _media = new()
    {
        LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, Volume = 0.35,
    };
    private readonly Button _play = new() { Content = "재생", MinWidth = 84 };
    private readonly Slider _position = new() { Width = 170, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _audioLabel = new()
    {
        Text = "오디오 미리보기", FontSize = 22, Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed,
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly StackPanel _toolbar = new()
    {
        Visibility = Visibility.Collapsed, Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 10),
    };
    private bool _playing;
    private bool _updatingPosition;

    public LocalMediaPreviewControl(string path)
    {
        _toolbar.Children.Add(_play);
        _toolbar.Children.Add(_position);
        var mediaArea = new Grid();
        mediaArea.Children.Add(_media);
        mediaArea.Children.Add(_audioLabel);
        var layout = new DockPanel();
        DockPanel.SetDock(_toolbar, Dock.Bottom);
        layout.Children.Add(_toolbar);
        layout.Children.Add(mediaArea);
        Children.Add(layout);
        Loaded += Start;
        _media.MediaOpened += (_, _) =>
        {
            if (IsDisposed) return;
            _position.IsEnabled = _media.NaturalDuration.HasTimeSpan;
            _position.Maximum = _position.IsEnabled ? _media.NaturalDuration.TimeSpan.TotalSeconds : 0;
            _audioLabel.Visibility = _media.HasVideo ? Visibility.Collapsed : Visibility.Visible;
            _toolbar.Visibility = Visibility.Visible;
            ShowReady();
        };
        _media.MediaFailed += (_, _) => FailPlayback();
        _media.MediaEnded += (_, _) =>
        {
            if (IsDisposed) return;
            Pause();
            _media.Position = TimeSpan.Zero;
            UpdatePosition();
        };
        _play.Click += (_, _) =>
        {
            if (_playing) { Pause(); return; }
            _media.Play();
            _playing = true;
            _play.Content = "일시 정지";
            _timer.Start();
        };
        _position.ValueChanged += (_, _) =>
        {
            if (!_updatingPosition && _position.IsEnabled)
                _media.Position = TimeSpan.FromSeconds(_position.Value);
        };
        _timer.Tick += (_, _) => UpdatePosition();

        void Start(object sender, RoutedEventArgs e)
        {
            Loaded -= Start;
            try
            {
                _media.Source = new Uri(path, UriKind.Absolute);
                _media.Pause();
            }
            catch (Exception ex) when (FilePreviewService.IsPreviewError(ex) || ex is UriFormatException)
            {
                FailPlayback();
            }
        }
    }

    private void Pause()
    {
        _media.Pause();
        _playing = false;
        _play.Content = "재생";
        _timer.Stop();
    }

    private void UpdatePosition()
    {
        _updatingPosition = true;
        _position.Value = _media.Position.TotalSeconds;
        _updatingPosition = false;
    }

    private void FailPlayback()
    {
        if (IsDisposed) return;
        _toolbar.Visibility = _audioLabel.Visibility = Visibility.Collapsed;
        _timer.Stop();
        ShowError("오디오·동영상을 열 수 없습니다. 파일이 손상되었거나 이 PC에 재생에 필요한 코덱이 없을 수 있습니다.");
    }

    protected override void ReleaseResources()
    {
        _timer.Stop();
        _media.Close();
        _media.Source = null;
    }
}
