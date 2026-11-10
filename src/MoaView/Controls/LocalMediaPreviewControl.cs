using System.IO;
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

    private IMediaPlayback _media = new WpfAudioPlayback { Volume = DefaultVolume };
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
    private readonly TextBlock _trackTitle = CreateLabel("", "곡 제목");
    private readonly TextBlock _trackArtist = CreateLabel("", "아티스트와 앨범");
    private readonly TextBlock _trackSummary = CreateLabel("", "곡 순서");
    private readonly Button _previousTrack = CreateButton(AppIconKind.PreviousTrack, "이전 곡", "B");
    private readonly Button _nextTrack = CreateButton(AppIconKind.NextTrack, "다음 곡", "N");
    private readonly Button _stop = CreateButton(AppIconKind.Stop, "정지", "X");
    private readonly Button _repeat = CreateButton(AppIconKind.Repeat, "반복 꺼짐", "R");
    private readonly Button _shuffle = CreateButton(AppIconKind.Shuffle, "셔플 꺼짐", "S");
    private readonly Button _playlistButton = CreateButton(AppIconKind.Playlist, "곡 목록 표시", "L");
    private readonly ListBox _trackList = new()
    {
        DisplayMemberPath = nameof(TrackItem.Name),
        SelectedValuePath = nameof(TrackItem.Path),
        MaxHeight = 160,
        BorderThickness = new Thickness(1),
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 16, 0, 0),
    };
    private readonly Grid _mediaArea = new();
    private readonly FrameworkElement _audioPanel;
    private readonly WrapPanel _musicActions = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _metadataCancellation;
    private AudioPlaylist? _playlist;
    private TaskCompletionSource<bool> _trackReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _openingTimeout = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly Border _controller;
    private PlaybackState _state;
    private bool _scrubbing;
    private bool _updatingPosition;
    private bool _updatingTrackSelection;
    private bool _loadStarted;
    private bool _playWhenOpened;
    private int _trackGeneration;

    internal string CurrentPath { get; private set; }
    internal bool IsMusic { get; }
    internal Task<bool> TrackReady => _trackReady.Task;
    internal IMediaPlayback Playback => _media;
    internal event EventHandler<string>? TrackChanged;

    public LocalMediaPreviewControl(string path)
    {
        CurrentPath = path;
        IsMusic = SupportedFileFormats.IsAudioExtension(Path.GetExtension(path));
        _controller = CreateController();
        _audioPanel = CreateAudioPanel();
        _mediaArea.Children.Add(_media.View);
        _mediaArea.Children.Add(_audioPanel);
        var layout = new DockPanel();
        DockPanel.SetDock(_controller, Dock.Bottom);
        layout.Children.Add(_controller);
        layout.Children.Add(_mediaArea);
        Children.Add(layout);

        Loaded += Start;
        _timer.Tick += (_, _) => RefreshPosition();
        _openingTimeout.Tick += (_, _) => FailPlayback("미디어 준비 시간이 초과되었습니다. 파일을 다시 열거나 다른 플레이어에서 재생해 주세요.");

        _play.Click += (_, _) => TogglePlayback();
        _back.Click += (_, _) => Skip(-SkipSeconds);
        _forward.Click += (_, _) => Skip(SkipSeconds);
        _mute.Click += (_, _) => ToggleMute();
        _previousTrack.Click += (_, _) => MoveTrack(previous: true);
        _nextTrack.Click += (_, _) => MoveTrack(previous: false);
        _stop.Click += (_, _) => StopPlayback();
        _repeat.Click += (_, _) => CycleRepeat();
        _shuffle.Click += (_, _) => ToggleShuffle();
        _playlistButton.Click += (_, _) => TogglePlaylist();
        _trackList.SelectionChanged += (_, _) =>
        {
            if (_updatingTrackSelection || _playlist is null || _trackList.SelectedValue is not string selected ||
                string.Equals(selected, CurrentPath, StringComparison.OrdinalIgnoreCase)) return;
            var playing = _state == PlaybackState.Playing || _playWhenOpened;
            if (_playlist.Select(selected)) LoadTrack(selected, playing, notifyHost: true);
        };
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
            LoadTrack(path, autoplay: !IsMusic, notifyHost: false);
            if (IsMusic) _ = LoadPlaylistAsync(path);
        }
    }

    internal bool HandleKeyInput(Key key) => key switch
    {
        Key.Space or Key.K => TogglePlayback(),
        Key.Left => Skip(-SkipSeconds),
        Key.Right => Skip(SkipSeconds),
        Key.M => ToggleMute(),
        Key.N when IsMusic => MoveTrack(previous: false),
        Key.B when IsMusic => MoveTrack(previous: true),
        Key.X when IsMusic => StopPlayback(),
        Key.R when IsMusic => CycleRepeat(),
        Key.S when IsMusic => ToggleShuffle(),
        Key.L when IsMusic => TogglePlaylist(),
        _ => false,
    };

    internal bool SpeedMenuIsOpen => _speed.IsDropDownOpen;

    internal bool KeepsControlKey(Key key) =>
        (_trackList.IsKeyboardFocusWithin && key != Key.Escape) ||
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
        foreach (var button in new[] { _previousTrack, _nextTrack, _stop, _repeat, _shuffle, _playlistButton })
        {
            button.Margin = new Thickness(3, 0, 3, 0);
            _musicActions.Children.Add(button);
        }
        _musicActions.Visibility = IsMusic ? Visibility.Visible : Visibility.Collapsed;
        controls.Children.Add(_musicActions);
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

    private FrameworkElement CreateAudioPanel()
    {
        var note = new AppIcon { Kind = AppIconKind.Music, Width = 56, Height = 56, Margin = new Thickness(0, 0, 0, 16) };
        note.SetResourceReference(Control.ForegroundProperty, "AppAccentTextBrush");
        _trackTitle.FontSize = 22;
        _trackTitle.FontWeight = FontWeights.SemiBold;
        _trackTitle.SetResourceReference(TextBlock.ForegroundProperty, "AppTextBrush");
        foreach (var text in new[] { _trackTitle, _trackArtist, _trackSummary })
        {
            text.TextAlignment = TextAlignment.Center;
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, 3, 0, 3);
        }
        _trackTitle.Text = Path.GetFileNameWithoutExtension(CurrentPath);
        _trackSummary.Text = IsMusic ? "같은 폴더의 음악을 찾고 있습니다." : "오디오 미리보기";
        _trackList.SetResourceReference(Control.BackgroundProperty, "AppSurfaceBrush");
        _trackList.SetResourceReference(Control.ForegroundProperty, "AppTextBrush");
        _trackList.SetResourceReference(Control.BorderBrushProperty, "AppBorderBrush");
        _trackList.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "PreviewEntryStyle");
        ScrollViewer.SetHorizontalScrollBarVisibility(_trackList, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(_trackList, true);
        VirtualizingPanel.SetVirtualizationMode(_trackList, VirtualizationMode.Recycling);
        AutomationProperties.SetName(_trackList, "같은 폴더의 음악");
        var card = new StackPanel { MaxWidth = 640, Margin = new Thickness(24) };
        card.Children.Add(note);
        card.Children.Add(_trackTitle);
        card.Children.Add(_trackArtist);
        card.Children.Add(_trackSummary);
        card.Children.Add(_trackList);
        return new ScrollViewer
        {
            Content = card,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = IsMusic ? Visibility.Visible : Visibility.Collapsed,
        };
    }

    private async Task LoadPlaylistAsync(string path)
    {
        try
        {
            var token = _lifetime.Token;
            var playlist = await Task.Run(() => AudioPlaylist.FromFolder(path, token), token);
            if (IsDisposed) return;
            _playlist = playlist;
            _updatingTrackSelection = true;
            _trackList.ItemsSource = playlist.Tracks.Select(track => new TrackItem(track, Path.GetFileNameWithoutExtension(track))).ToArray();
            _updatingTrackSelection = false;
            UpdateMusicAppearance();
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadTrackInfoAsync(string path, int generation, CancellationToken token)
    {
        try
        {
            var info = await AudioTrackInfoReader.ReadAsync(path, token);
            if (IsDisposed || generation != _trackGeneration) return;
            _trackTitle.Text = info.Title;
            _trackArtist.Text = string.Join(" · ", new[] { info.Artist, info.Album }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        catch (OperationCanceledException) { }
    }

    private void LoadTrack(string path, bool autoplay, bool notifyHost)
    {
        if (IsDisposed) return;
        if (_loadStarted)
        {
            _trackReady.TrySetResult(false);
            _trackReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        _loadStarted = true;
        var muted = _media.IsMuted;
        _openingTimeout.Stop();
        _timer.Stop();
        _media.Dispose();
        _mediaArea.Children.Remove(_media.View);
        IMediaPlayback player = IsMusic ? new WpfAudioPlayback() : new VideoFramePlayback();
        player.Volume = _volume.Value;
        player.IsMuted = muted;
        _media = player;
        _mediaArea.Children.Insert(0, player.View);
        // Ignore events queued by a previous native player after rapid track changes.
        player.Opened += (_, _) => { if (ReferenceEquals(player, _media)) OnMediaOpened(); };
        player.Failed += (_, _) => { if (ReferenceEquals(player, _media)) FailPlayback((player as VideoFramePlayback)?.ErrorMessage); };
        player.Ended += (_, _) => { if (ReferenceEquals(player, _media)) OnMediaEnded(); };
        player.BufferingStarted += (_, _) =>
        {
            if (!ReferenceEquals(player, _media) || IsDisposed || _state is PlaybackState.Failed or PlaybackState.Closed) return;
            ShowLoading("재생할 내용을 불러오고 있습니다…");
        };
        player.BufferingEnded += (_, _) =>
        {
            if (ReferenceEquals(player, _media) && IsReady) HideStatus();
        };
        CurrentPath = path;
        _playWhenOpened = autoplay;
        _scrubbing = false;
        ChangeState(PlaybackState.Opening);
        _play.IsEnabled = _back.IsEnabled = _forward.IsEnabled = _position.IsEnabled = false;
        _stop.IsEnabled = IsMusic;
        DisplayPosition(0);
        _position.Maximum = 0;
        _duration.Text = "--:--";
        _trackTitle.Text = Path.GetFileNameWithoutExtension(path);
        _trackTitle.ToolTip = path;
        _trackArtist.Text = "";
        UpdateMusicAppearance();
        ShowLoading(IsMusic ? "음악을 준비하고 있습니다…" : "동영상을 준비하고 있습니다…");
        _openingTimeout.Start();
        var generation = ++_trackGeneration;
        _metadataCancellation?.Cancel();
        _metadataCancellation?.Dispose();
        _metadataCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if (IsMusic) _ = LoadTrackInfoAsync(path, generation, _metadataCancellation.Token);
        if (notifyHost) TrackChanged?.Invoke(this, path);
        if (IsDisposed) return;
        try
        {
            player.Source = new Uri(path, UriKind.Absolute);
            player.Pause();
        }
        catch (Exception ex) when (FilePreviewService.IsPreviewError(ex) || ex is UriFormatException)
        {
            FailPlayback();
        }
    }

    private bool MoveTrack(bool previous)
    {
        if (!IsMusic || _playlist is null || IsDisposed) return false;
        var playing = _state == PlaybackState.Playing || _playWhenOpened;
        var path = previous ? _playlist.MovePrevious() : _playlist.MoveNext();
        if (path is null) return false;
        LoadTrack(path, playing, notifyHost: true);
        return true;
    }

    private bool StopPlayback()
    {
        if (!IsMusic || IsDisposed) return false;
        _playWhenOpened = false;
        if (_state == PlaybackState.Opening) return true;
        if (!IsReady) return false;
        _media.Pause();
        if (CanSeek) _media.Position = TimeSpan.Zero;
        ChangeState(PlaybackState.Paused);
        DisplayPosition(0);
        return true;
    }

    private bool CycleRepeat()
    {
        if (_playlist is null || IsDisposed) return false;
        _playlist.RepeatMode = _playlist.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off,
        };
        UpdateMusicAppearance();
        return true;
    }

    private bool ToggleShuffle()
    {
        if (_playlist is null || IsDisposed) return false;
        _playlist.Shuffle = !_playlist.Shuffle;
        UpdateMusicAppearance();
        return true;
    }

    private bool TogglePlaylist()
    {
        if (_playlist is null || IsDisposed) return false;
        _trackList.Visibility = _trackList.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        UpdateMusicAppearance();
        if (_trackList.IsVisible && _trackList.SelectedItem is { } item) _trackList.ScrollIntoView(item);
        return true;
    }

    private void UpdateMusicAppearance()
    {
        _previousTrack.IsEnabled = _playlist?.CanMovePrevious == true;
        _nextTrack.IsEnabled = _playlist?.CanMoveNext == true;
        _repeat.IsEnabled = _shuffle.IsEnabled = _playlistButton.IsEnabled = _playlist is not null;
        if (_playlist is null) return;
        _trackSummary.Text = $"{Path.GetExtension(CurrentPath).TrimStart('.').ToUpperInvariant()} · {_playlist.CurrentIndex + 1} / {_playlist.Tracks.Count}곡";
        _updatingTrackSelection = true;
        _trackList.SelectedValue = CurrentPath;
        _updatingTrackSelection = false;
        var mode = _playlist.RepeatMode;
        SetButtonAppearance(_repeat, mode == RepeatMode.One ? AppIconKind.RepeatOne : AppIconKind.Repeat,
            mode switch { RepeatMode.All => "전체 반복", RepeatMode.One => "한 곡 반복", _ => "반복 꺼짐" }, "R");
        SetButtonAppearance(_shuffle, AppIconKind.Shuffle, _playlist.Shuffle ? "셔플 켜짐" : "셔플 꺼짐", "S");
        SetButtonAppearance(_playlistButton, AppIconKind.Playlist, _trackList.Visibility == Visibility.Visible ? "곡 목록 숨기기" : "곡 목록 표시", "L");
        Highlight(_repeat, mode != RepeatMode.Off);
        Highlight(_shuffle, _playlist.Shuffle);
        Highlight(_playlistButton, _trackList.Visibility == Visibility.Visible);

        static void Highlight(Button button, bool active)
        {
            button.SetResourceReference(Control.BackgroundProperty, active ? "AppAccentBrush" : "AppControlBrush");
            button.SetResourceReference(Control.ForegroundProperty, active ? "AppOnAccentBrush" : "AppTextBrush");
        }
    }

    private void OnMediaEnded()
    {
        if (_state != PlaybackState.Playing || IsDisposed) return;
        _media.Pause();
        if (IsMusic && _playlist?.MoveNext(automatic: true) is { } next)
        {
            LoadTrack(next, autoplay: true, notifyHost: true);
            return;
        }
        ChangeState(PlaybackState.Ended);
        RefreshPosition();
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
        // A video container with an unsupported video codec may open its audio stream only.
        if (!IsMusic && (!hasVideo || _media.NaturalVideoWidth <= 0 || _media.NaturalVideoHeight <= 0))
        {
            FailPlayback("영상 트랙을 읽을 수 없습니다. 이 PC에 영상 코덱이 없거나 파일이 손상되었을 수 있습니다. 다른 플레이어에서 확인해 주세요.");
            return;
        }
        _audioPanel.Visibility = hasVideo ? Visibility.Collapsed : Visibility.Visible;
        _volume.IsEnabled = _mute.IsEnabled = _media.HasAudio;
        _controller.Visibility = Visibility.Visible;
        // The video backend raises Opened only after decoded pixels reach the WPF image.
        CompleteOpening();
    }

    private void CompleteOpening()
    {
        _openingTimeout.Stop();
        ChangeState(PlaybackState.Paused);
        _play.IsEnabled = _stop.IsEnabled = true;
        RefreshPosition();
        ShowReady();
        if (_playWhenOpened) TogglePlayback();
        _playWhenOpened = false;
        _trackReady.TrySetResult(true);
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

    private void FailPlayback(string? message = null)
    {
        if (_state == PlaybackState.Closed || IsDisposed) return;
        _openingTimeout.Stop();
        _timer.Stop();
        _playWhenOpened = false;
        _media.Close();
        _media.Source = null;
        _state = PlaybackState.Failed;
        _trackReady.TrySetResult(false);
        _play.IsEnabled = _position.IsEnabled = _back.IsEnabled = _forward.IsEnabled = _stop.IsEnabled = false;
        _controller.Visibility = IsMusic ? Visibility.Visible : Visibility.Collapsed;
        _audioPanel.Visibility = Visibility.Collapsed;
        UpdateMusicAppearance();
        ShowError(message ?? "오디오·동영상을 열 수 없습니다. 파일이 손상되었거나 이 PC에 재생에 필요한 코덱이 없을 수 있습니다.");
    }

    protected override void ReleaseResources() => CloseMedia();

    private void CloseMedia()
    {
        if (_state == PlaybackState.Closed) return;
        _openingTimeout.Stop();
        _state = PlaybackState.Closed;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _metadataCancellation?.Cancel();
        _metadataCancellation?.Dispose();
        _trackReady.TrySetResult(false);
        _timer.Stop();
        _media.Dispose();
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

    private sealed record TrackItem(string Path, string Name)
    {
        public override string ToString() => Name;
    }
    private enum PlaybackState { Opening, Paused, Playing, Ended, Failed, Closed }
}
