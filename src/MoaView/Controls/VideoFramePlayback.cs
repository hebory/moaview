using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using NativePlayer = Windows.Media.Playback.MediaPlayer;

namespace MoaView.Controls;

// Frame-server mode keeps the decoder paused until actual decoded pixels have reached our
// WPF image. A native media clock or a WPF composition callback alone is not readiness.
internal sealed class VideoFramePlayback : IMediaPlayback
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly object _frameSync = new();
    private NativePlayer? _player;
    private MediaSource? _source;
    private MediaPlaybackItem? _item;
    private VideoFrameSurface? _surface;
    private VideoFrameSurface? _copyingSurface;
    private WriteableBitmap? _bitmap;
    private Uri? _uri;
    private int _generation;
    private int _queuedFrame;
    private long _nativeFrames;
    private long _seekBaseline;
    private bool _metadataReady;
    private bool _ready;
    private bool _disposed;
    private bool _requestedPlay;
    private bool _nativeBuffering;
    private bool _seeking;
    private bool _seekCompleted;
    private TimeSpan _seekPosition;
    private TimeSpan? _queuedSeek;
    private double _volume = 0.35;
    private double _speed = 1;
    private bool _muted;
    private long _lastFrameAt;
    private long _waitStartedAt;
    private TimeSpan _lastFramePosition;
    private double _frameTimeoutSeconds = 15;

    public VideoFramePlayback() => _watchdog.Tick += OnWatchdog;

    public FrameworkElement View => _image;
    public Uri? Source
    {
        get => _uri;
        set
        {
            Close();
            if (value is null || _disposed) return;
            ErrorMessage = null;
            NaturalDuration = Duration.Automatic;
            NaturalVideoWidth = NaturalVideoHeight = 0;
            HasAudio = false;
            _frameTimeoutSeconds = 15;
            PresentedFrameCount = 0;
            _uri = value;
            _ = OpenAsync(value, _generation);
        }
    }
    public Duration NaturalDuration { get; private set; } = Duration.Automatic;
    public bool HasAudio { get; private set; }
    public bool HasVideo => NaturalVideoWidth > 0 && NaturalVideoHeight > 0;
    public int NaturalVideoWidth { get; private set; }
    public int NaturalVideoHeight { get; private set; }
    public bool IsBuffering => _seeking || _nativeBuffering;
    internal long PresentedFrameCount { get; private set; }
    internal string? ErrorMessage { get; private set; }
    internal bool IsFrameReady => _ready && !_seeking;

    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_player is { } player) player.Volume = _volume; }
    }
    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; UpdateAudio(); }
    }
    public double SpeedRatio
    {
        get => _speed;
        set { _speed = value; if (_player is { } player) player.PlaybackSession.PlaybackRate = value; }
    }
    public TimeSpan Position
    {
        get => _queuedSeek ?? (_seeking ? _seekPosition : _player?.PlaybackSession.Position ?? TimeSpan.Zero);
        set
        {
            if (_player is not { } player || !_ready) return;
            if (_seeking) { _queuedSeek = value; return; }
            if (Math.Abs((player.PlaybackSession.Position - value).TotalMilliseconds) < 80) return;
            BeginSeek(player, value);
        }
    }

    private void BeginSeek(NativePlayer player, TimeSpan position)
    {
        // Serialize decoder seeks, coalescing rapid slider changes to their latest target.
        player.Pause();
        _seeking = true;
        _seekCompleted = false;
        _seekPosition = position;
        _seekBaseline = Interlocked.Read(ref _nativeFrames);
        _waitStartedAt = Stopwatch.GetTimestamp();
        UpdateAudio();
        BufferingStarted?.Invoke(this, EventArgs.Empty);
        player.PlaybackSession.Position = position;
    }

    public event EventHandler? Opened;
    public event EventHandler? Failed;
    public event EventHandler? Ended;
    public event EventHandler? BufferingStarted;
    public event EventHandler? BufferingEnded;

    private async Task OpenAsync(Uri uri, int generation)
    {
        try
        {
            if (!uri.IsFile) throw new InvalidOperationException("Only local video files are supported.");
            var file = await StorageFile.GetFileFromPathAsync(uri.LocalPath);
            if (_disposed || generation != _generation) return;
            _source = MediaSource.CreateFromStorageFile(file);
            _item = new MediaPlaybackItem(_source);
            var player = new NativePlayer
            {
                AutoPlay = false, IsVideoFrameServerEnabled = true,
                Volume = _volume, IsMuted = true,
            };
            lock (_frameSync) _player = player;
            // Preview audio should not take over the system media transport/session.
            player.CommandManager.IsEnabled = false;
            player.MediaOpened += OnNativeOpened;
            player.MediaFailed += OnNativeFailed;
            player.MediaEnded += OnNativeEnded;
            player.VideoFrameAvailable += OnVideoFrame;
            player.PlaybackSession.SeekCompleted += OnSeekCompleted;
            player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _waitStartedAt = Stopwatch.GetTimestamp();
            _watchdog.Start();
            player.Source = _item;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (generation == _generation && !_disposed) Fail("동영상을 열 수 없습니다. 파일과 설치된 영상 코덱을 확인해 주세요.");
        }
    }

    private void OnNativeOpened(NativePlayer player, object args) => Post(player, () =>
    {
        var session = player.PlaybackSession;
        NaturalVideoWidth = (int)session.NaturalVideoWidth;
        NaturalVideoHeight = (int)session.NaturalVideoHeight;
        NaturalDuration = new Duration(session.NaturalDuration);
        HasAudio = _item?.AudioTracks.Count > 0;
        if (!HasVideo)
        {
            Fail("영상 트랙을 읽을 수 없습니다. 이 PC에 영상 코덱이 없거나 파일이 손상되었을 수 있습니다.");
            return;
        }
        _metadataReady = true;
        if (_item?.VideoTracks.Count > 0)
        {
            var rate = _item.VideoTracks[0].GetEncodingProperties().FrameRate;
            if (rate.Numerator > 0 && rate.Denominator > 0)
                _frameTimeoutSeconds = Math.Max(3, 3.0 * rate.Denominator / rate.Numerator);
        }
        if (Interlocked.Read(ref _nativeFrames) > 0) QueueFrame(player);
    });

    private void OnNativeFailed(NativePlayer player, MediaPlayerFailedEventArgs args) =>
        Post(player, () => Fail("동영상을 재생할 수 없습니다. 파일이 손상되었거나 영상 코덱을 지원하지 않을 수 있습니다."));

    private void OnNativeEnded(NativePlayer player, object args) => Post(player, () =>
    {
        if (!_ready || _seeking || !_requestedPlay) return;
        _requestedPlay = false;
        Ended?.Invoke(this, EventArgs.Empty);
    });

    private void OnVideoFrame(NativePlayer player, object args)
    {
        lock (_frameSync)
        {
            if (!ReferenceEquals(player, _player)) return;
            Interlocked.Increment(ref _nativeFrames);
        }
        QueueFrame(player);
    }

    private void OnSeekCompleted(MediaPlaybackSession session, object args)
    {
        if (_player is not { } player) return;
        Post(player, () =>
        {
            if (!ReferenceEquals(session, player.PlaybackSession) || !_seeking) return;
            _seekCompleted = true;
            if (Interlocked.Read(ref _nativeFrames) > _seekBaseline) QueueFrame(player);
        });
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
    {
        if (_player is not { } player) return;
        Post(player, () =>
        {
            if (!ReferenceEquals(session, player.PlaybackSession)) return;
            var buffering = session.PlaybackState == MediaPlaybackState.Buffering;
            if (_nativeBuffering == buffering) return;
            _nativeBuffering = buffering;
            if (buffering) BufferingStarted?.Invoke(this, EventArgs.Empty);
            else if (!_seeking) BufferingEnded?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Post(NativePlayer player, Action action)
    {
        if (_disposed || !ReferenceEquals(player, _player) || _image.Dispatcher.HasShutdownStarted) return;
        _image.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && ReferenceEquals(player, _player)) action();
        });
    }

    private void QueueFrame(NativePlayer player)
    {
        if (_disposed || !ReferenceEquals(player, _player) || _image.Dispatcher.HasShutdownStarted ||
            Interlocked.Exchange(ref _queuedFrame, 1) != 0) return;
        var generation = _generation;
        _image.Dispatcher.BeginInvoke(new Action(async () => await CopyFrameAsync(player, generation)));
    }

    private async Task CopyFrameAsync(NativePlayer player, int generation)
    {
        VideoFrameSurface? surface = null;
        var frameNumber = Interlocked.Read(ref _nativeFrames);
        try
        {
            if (_disposed || generation != _generation || !ReferenceEquals(player, _player) || !_metadataReady ||
                _seeking && (!_seekCompleted || frameNumber <= _seekBaseline)) return;
            // Bound CPU/GPU transfer cost to a preview-sized image, preserving aspect ratio.
            var scale = Math.Min(1, Math.Min(1280.0 / NaturalVideoWidth, 720.0 / NaturalVideoHeight));
            var width = Math.Max(1, (int)Math.Round(NaturalVideoWidth * scale));
            var height = Math.Max(1, (int)Math.Round(NaturalVideoHeight * scale));
            surface = _surface ??= VideoFrameSurface.Create(width, height);
            _copyingSurface = surface;
            player.CopyFrameToVideoSurface(surface.Surface);
            var pixels = await surface.ReadPixelsAsync(CancellationToken.None);
            if (_disposed || generation != _generation || !ReferenceEquals(player, _player)) return;
            if (_seeking && (!_seekCompleted || frameNumber <= _seekBaseline)) return;
            if (_queuedSeek is { } target)
            {
                _queuedSeek = null;
                if (Math.Abs((player.PlaybackSession.Position - target).TotalMilliseconds) >= 80)
                {
                    BeginSeek(player, target);
                    return;
                }
            }
            _bitmap ??= new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            _image.Source = _bitmap;
            PresentedFrameCount++;
            _lastFrameAt = Stopwatch.GetTimestamp();
            _lastFramePosition = player.PlaybackSession.Position;
            if (!_ready)
            {
                // No Play call has occurred: both the clock and audio have remained paused.
                _ready = true;
                UpdateAudio();
                Opened?.Invoke(this, EventArgs.Empty);
                if (_requestedPlay && ReferenceEquals(player, _player)) Play();
            }
            if (_seeking)
            {
                _seeking = false;
                UpdateAudio();
                if (_requestedPlay) player.Play();
                BufferingEnded?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (generation == _generation && !_disposed)
                Fail("영상 프레임을 표시하지 못해 재생을 중단했습니다. 파일을 다시 열어 주세요.");
        }
        finally
        {
            if (generation != _generation) surface?.Dispose();
            if (ReferenceEquals(_copyingSurface, surface)) _copyingSurface = null;
            Interlocked.Exchange(ref _queuedFrame, 0);
            if (!_disposed && _player is { } current &&
                (generation != _generation && Interlocked.Read(ref _nativeFrames) > 0 ||
                 generation == _generation && Interlocked.Read(ref _nativeFrames) > frameNumber))
                QueueFrame(current);
        }
    }

    public void Play()
    {
        if (_disposed) return;
        _requestedPlay = true;
        if (!_ready || _seeking || _player is not { } player) return;
        _lastFrameAt = Stopwatch.GetTimestamp();
        _lastFramePosition = player.PlaybackSession.Position;
        UpdateAudio();
        player.PlaybackSession.PlaybackRate = _speed;
        player.Play();
    }

    public void Pause()
    {
        _requestedPlay = false;
        _player?.Pause();
    }

    private void UpdateAudio()
    {
        if (_player is { } player) player.IsMuted = _muted || !_ready || _seeking;
    }

    private void OnWatchdog(object? sender, EventArgs e)
    {
        if (_player is not { } player) return;
        if ((!_ready || _seeking) && Stopwatch.GetElapsedTime(_waitStartedAt).TotalSeconds > 15)
            Fail("영상 프레임 준비 시간이 초과되어 재생을 중단했습니다. 파일을 다시 열어 주세요.");
        else if (_ready && !_seeking && _requestedPlay && !_nativeBuffering &&
            Stopwatch.GetElapsedTime(_lastFrameAt).TotalSeconds > _frameTimeoutSeconds / Math.Min(1, _speed) &&
            (player.PlaybackSession.Position - _lastFramePosition).TotalSeconds > 2)
            Fail("영상 화면이 갱신되지 않아 소리 재생도 중단했습니다. 파일을 다시 열어 주세요.");
    }

    private void Fail(string message)
    {
        ErrorMessage = message;
        Close();
        Failed?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        _image.Dispatcher.VerifyAccess();
        _generation++;
        _watchdog.Stop();
        _requestedPlay = _ready = _metadataReady = _seeking = _nativeBuffering = false;
        _queuedSeek = null;
        _uri = null;
        NativePlayer? player;
        lock (_frameSync)
        {
            player = _player;
            _player = null;
            Interlocked.Exchange(ref _nativeFrames, 0);
        }
        if (player is not null)
        {
            player.MediaOpened -= OnNativeOpened;
            player.MediaFailed -= OnNativeFailed;
            player.MediaEnded -= OnNativeEnded;
            player.VideoFrameAvailable -= OnVideoFrame;
            player.PlaybackSession.SeekCompleted -= OnSeekCompleted;
            player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            player.Pause();
            player.Source = null;
            player.Dispose();
        }
        _item = null;
        _source?.Dispose();
        _source = null;
        if (!ReferenceEquals(_surface, _copyingSurface)) _surface?.Dispose();
        _surface = null;
        _image.Source = null;
        _bitmap = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        _watchdog.Tick -= OnWatchdog;
        Opened = Failed = Ended = BufferingStarted = BufferingEnded = null;
    }
}
