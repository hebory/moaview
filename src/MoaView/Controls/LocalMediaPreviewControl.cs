using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MoaView.Services;

namespace MoaView.Controls;

internal sealed class LocalMediaPreviewControl : SpecializedPreviewControl
{
    private const double DefaultVolume = 0.35;
    private const int SkipSeconds = 10;

    private readonly MediaElement _media = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Close,
        Stretch = Stretch.Uniform,
        Volume = DefaultVolume,
    };
    private readonly Button _play = CreateButton(AppIconKind.Play, "재생", "K", primary: true);
    private readonly Button _back = CreateButton(AppIconKind.SeekBack, "10초 뒤로", "←");
    private readonly Button _forward = CreateButton(AppIconKind.SeekForward, "10초 앞으로", "→");
    private readonly Button _mute = CreateButton(AppIconKind.Volume, "음소거", "M");
    private readonly Slider _position = CreateSlider("재생 위치", smallChange: 1, largeChange: SkipSeconds);
    private readonly Slider _volume = CreateSlider("볼륨", smallChange: 0.05, largeChange: 0.1);
    private readonly ComboBox _speed = new()
    {
        MinWidth = 68,
        MinHeight = 32,
        ToolTip = "재생 속도",
        ItemsSource = new[] { 0.5, 0.75, 1, 1.25, 1.5, 2 },
        ItemStringFormat = "{0:0.##}×",
        SelectedItem = 1.0,
    };
    private readonly TextBlock _elapsed = CreateLabel("00:00", "현재 재생 시간");
    private readonly TextBlock _duration = CreateLabel("--:--", "전체 재생 시간");
    private readonly TextBlock _volumeLabel = CreateLabel("", "볼륨 수준");
    private readonly TextBlock _audioLabel = new()
    {
        Text = "오디오 미리보기",
        FontSize = 22,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Border _controller;
    private PlaybackState _state;
    private bool _scrubbing;
    private bool _updatingPosition;

    public LocalMediaPreviewControl(string path)
    {
        _controller = CreateController();
        _audioLabel.SetResourceReference(TextBlock.ForegroundProperty, "AppTextBrush");

        var mediaArea = new Grid();
        mediaArea.Children.Add(_media);
        mediaArea.Children.Add(_audioLabel);
        var layout = new DockPanel();
        DockPanel.SetDock(_controller, Dock.Bottom);
        layout.Children.Add(_controller);
        layout.Children.Add(mediaArea);
        Children.Add(layout);

        Loaded += Start;
        _media.MediaOpened += (_, _) => OnMediaOpened();
        _media.MediaFailed += (_, _) => FailPlayback();
        _media.MediaEnded += (_, _) =>
        {
            if (_state != PlaybackState.Playing) return;
            _media.Pause();
            ChangeState(PlaybackState.Ended);
            RefreshPosition();
        };
        _timer.Tick += (_, _) => RefreshPosition();

        _play.Click += (_, _) => TogglePlayback();
        _back.Click += (_, _) => Skip(-SkipSeconds);
        _forward.Click += (_, _) => Skip(SkipSeconds);
        _mute.Click += (_, _) => ToggleMute();
        _volume.ValueChanged += (_, _) =>
        {
            _media.Volume = _volume.Value;
            if (_volume.Value > 0) _media.IsMuted = false;
            UpdateVolumeAppearance();
        };
        _speed.SelectionChanged += (_, _) =>
        {
            if (_state != PlaybackState.Playing || _speed.SelectedItem is not double rate) return;
            var position = _media.Position;
            _media.SpeedRatio = rate;
            if (CanSeek) _media.Position = position;
        };

        _position.ValueChanged += (_, _) =>
        {
            if (_updatingPosition || !CanSeek) return;
            if (_scrubbing) _elapsed.Text = FormatTime(_position.Value);
            else Seek(_position.Value);
        };
        _position.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _scrubbing = true));
        _position.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, e) =>
        {
            _scrubbing = false;
            if (e.Canceled) RefreshPosition();
            else Seek(_position.Value);
        }));

        void Start(object sender, RoutedEventArgs e)
        {
            Loaded -= Start;
            if (IsDisposed) return;
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

    internal bool HandleKeyInput(Key key) => key switch
    {
        Key.Space or Key.K => TogglePlayback(),
        Key.Left => Skip(-SkipSeconds),
        Key.Right => Skip(SkipSeconds),
        Key.M => ToggleMute(),
        _ => false,
    };

    internal bool SpeedMenuIsOpen => _speed.IsDropDownOpen;

    internal bool KeepsControlKey(Key key) =>
        (key is Key.Left or Key.Right or Key.PageUp or Key.PageDown &&
            (_position.IsKeyboardFocusWithin || _volume.IsKeyboardFocusWithin)) ||
        (_speed.IsKeyboardFocusWithin && key is Key.Space or Key.Left or Key.Right or Key.PageUp or Key.PageDown);

    private bool IsReady => _state is PlaybackState.Paused or PlaybackState.Playing or PlaybackState.Ended;
    private bool CanSeek => IsReady && _position.IsEnabled;

    private Border CreateController()
    {
        _duration.TextAlignment = TextAlignment.Right;
        _position.Margin = new Thickness(10, 0, 10, 0);
        DockPanel.SetDock(_elapsed, Dock.Left);
        DockPanel.SetDock(_duration, Dock.Right);
        var timeline = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        timeline.Children.Add(_elapsed);
        timeline.Children.Add(_duration);
        timeline.Children.Add(_position);

        _play.Margin = new Thickness(6, 0, 6, 0);
        var transport = new StackPanel { Orientation = Orientation.Horizontal };
        transport.Children.Add(_back);
        transport.Children.Add(_play);
        transport.Children.Add(_forward);

        _volume.Width = 94;
        _volume.Value = DefaultVolume;
        _volume.Margin = new Thickness(8, 0, 6, 0);
        _volumeLabel.MinWidth = 40;
        _speed.Margin = new Thickness(8, 0, 0, 0);
        _speed.SetResourceReference(StyleProperty, "ToolbarComboBoxStyle");
        AutomationProperties.SetName(_speed, "재생 속도");
        UpdateVolumeAppearance();

        var soundAndSpeed = new StackPanel { Orientation = Orientation.Horizontal };
        soundAndSpeed.Children.Add(_mute);
        soundAndSpeed.Children.Add(_volume);
        soundAndSpeed.Children.Add(_volumeLabel);
        soundAndSpeed.Children.Add(_speed);

        var actions = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(transport, Dock.Left);
        DockPanel.SetDock(soundAndSpeed, Dock.Right);
        actions.Children.Add(transport);
        actions.Children.Add(soundAndSpeed);

        var controls = new StackPanel();
        controls.Children.Add(timeline);
        controls.Children.Add(actions);
        var controller = new Border
        {
            Child = controls,
            Padding = new Thickness(16, 6, 16, 10),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        controller.SetResourceReference(Border.BackgroundProperty, "AppSurfaceBrush");
        controller.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        return controller;
    }

    private void OnMediaOpened()
    {
        if (_state != PlaybackState.Opening || IsDisposed) return;
        var length = _media.NaturalDuration;
        var canSeek = length.HasTimeSpan && length.TimeSpan > TimeSpan.Zero;
        _position.IsEnabled = _back.IsEnabled = _forward.IsEnabled = canSeek;
        _position.Maximum = canSeek ? length.TimeSpan.TotalSeconds : 0;
        _duration.Text = canSeek ? FormatTime(_position.Maximum) : "--:--";
        var hasVideo = _media.HasVideo;
        _audioLabel.Visibility = hasVideo ? Visibility.Collapsed : Visibility.Visible;
        if (hasVideo)
        {
            _media.ScrubbingEnabled = true;
            _media.Position = TimeSpan.Zero;
        }
        _volume.IsEnabled = _mute.IsEnabled = _media.HasAudio;
        ChangeState(PlaybackState.Paused);
        RefreshPosition();
        _controller.Visibility = Visibility.Visible;
        ShowReady();
    }

    private bool TogglePlayback()
    {
        if (!IsReady) return false;
        if (_state == PlaybackState.Playing)
        {
            _media.Pause();
            ChangeState(PlaybackState.Paused);
            RefreshPosition();
        }
        else
        {
            var restart = _state == PlaybackState.Ended || CanSeek && _position.Value >= _position.Maximum;
            if (restart) DisplayPosition(0);
            _media.Play();
            if (_speed.SelectedItem is double rate && _media.SpeedRatio != rate)
                _media.SpeedRatio = rate;
            // Reapply the seek after Play and rate changes, which can reset the media clock.
            if (restart || CanSeek && _position.Value > 0)
                _media.Position = TimeSpan.FromSeconds(_position.Value);
            ChangeState(PlaybackState.Playing);
        }
        return true;
    }

    private void ChangeState(PlaybackState state)
    {
        _state = state;
        var (label, icon) = state switch
        {
            PlaybackState.Playing => ("일시 정지", AppIconKind.Pause),
            PlaybackState.Ended => ("처음부터 다시 재생", AppIconKind.Replay),
            _ => ("재생", AppIconKind.Play),
        };
        SetButtonAppearance(_play, icon, label, "K");
        _timer.IsEnabled = state == PlaybackState.Playing;
    }

    private bool Skip(int seconds) => Seek(_position.Value + seconds);

    private bool Seek(double seconds)
    {
        if (!CanSeek) return false;
        var target = Math.Clamp(seconds, 0, _position.Maximum);
        _media.Position = TimeSpan.FromSeconds(target);
        if (_state != PlaybackState.Playing)
            ChangeState(target == _position.Maximum ? PlaybackState.Ended : PlaybackState.Paused);
        DisplayPosition(target);
        return true;
    }

    private void RefreshPosition()
    {
        if (!IsReady || _scrubbing) return;
        var seconds = _state == PlaybackState.Ended && CanSeek
            ? _position.Maximum
            : Math.Max(0, _media.Position.TotalSeconds);
        DisplayPosition(CanSeek ? Math.Min(seconds, _position.Maximum) : seconds);
    }

    private void DisplayPosition(double seconds)
    {
        _updatingPosition = true;
        _position.Value = seconds;
        _updatingPosition = false;
        _elapsed.Text = FormatTime(seconds);
    }

    private bool ToggleMute()
    {
        if (!IsReady || !_mute.IsEnabled) return false;
        _media.IsMuted = !_media.IsMuted;
        UpdateVolumeAppearance();
        return true;
    }

    private void UpdateVolumeAppearance()
    {
        _volumeLabel.Text = $"{_volume.Value * 100:0}%";
        _volume.ToolTip = $"볼륨 {_volumeLabel.Text}";
        SetButtonAppearance(_mute, _media.IsMuted ? AppIconKind.Muted : AppIconKind.Volume,
            _media.IsMuted ? "음소거 해제" : "음소거", "M");
    }

    private void FailPlayback()
    {
        if (_state == PlaybackState.Closed || IsDisposed) return;
        CloseMedia();
        _controller.Visibility = _audioLabel.Visibility = Visibility.Collapsed;
        ShowError("오디오·동영상을 열 수 없습니다. 파일이 손상되었거나 이 PC에 재생에 필요한 코덱이 없을 수 있습니다.");
    }

    protected override void ReleaseResources() => CloseMedia();

    private void CloseMedia()
    {
        if (_state == PlaybackState.Closed) return;
        _state = PlaybackState.Closed;
        _timer.Stop();
        _media.Close();
        _media.Source = null;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static Button CreateButton(AppIconKind icon, string name, string shortcut, bool primary = false)
    {
        var button = new Button { Content = new AppIcon(), MinWidth = 36, MinHeight = 32 };
        button.SetResourceReference(StyleProperty, primary ? "PrimaryToolbarButtonStyle" : "IconToolbarButtonStyle");
        SetButtonAppearance(button, icon, name, shortcut);
        return button;
    }

    private static void SetButtonAppearance(Button button, AppIconKind icon, string name, string shortcut)
    {
        ((AppIcon)button.Content).Kind = icon;
        button.ToolTip = $"{name} ({shortcut})";
        AutomationProperties.SetName(button, name);
    }

    private static Slider CreateSlider(string name, double smallChange, double largeChange)
    {
        var slider = new Slider
        {
            Maximum = 1,
            SmallChange = smallChange,
            LargeChange = largeChange,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.SetResourceReference(BackgroundProperty, "AppControlBrush");
        slider.SetResourceReference(Control.ForegroundProperty, "AppAccentBrush");
        slider.SetResourceReference(Control.BorderBrushProperty, "AppBorderBrush");
        AutomationProperties.SetName(slider, name);
        return slider;
    }

    private static TextBlock CreateLabel(string text, string name)
    {
        var label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedBrush");
        AutomationProperties.SetName(label, name);
        return label;
    }

    private enum PlaybackState { Opening, Paused, Playing, Ended, Closed }
}
