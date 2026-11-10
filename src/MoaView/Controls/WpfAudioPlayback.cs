using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoaView.Controls;

internal sealed class WpfAudioPlayback : IMediaPlayback
{
    private readonly MediaElement _media = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Close,
        Stretch = Stretch.Uniform,
    };

    private bool _disposed;

    internal WpfAudioPlayback()
    {
        _media.MediaOpened += OnMediaOpened;
        _media.MediaFailed += OnMediaFailed;
        _media.MediaEnded += OnMediaEnded;
        _media.BufferingStarted += OnBufferingStarted;
        _media.BufferingEnded += OnBufferingEnded;
    }

    public FrameworkElement View => _media;

    public Uri? Source
    {
        get => _media.Source;
        set => _media.Source = value;
    }

    public Duration NaturalDuration => _media.NaturalDuration;
    public bool HasAudio => _media.HasAudio;
    public bool HasVideo => _media.HasVideo;
    public bool IsBuffering => _media.IsBuffering;
    public int NaturalVideoWidth => _media.NaturalVideoWidth;
    public int NaturalVideoHeight => _media.NaturalVideoHeight;
    public double Volume { get => _media.Volume; set => _media.Volume = value; }
    public double SpeedRatio { get => _media.SpeedRatio; set => _media.SpeedRatio = value; }
    public bool IsMuted { get => _media.IsMuted; set => _media.IsMuted = value; }
    public TimeSpan Position { get => _media.Position; set => _media.Position = value; }

    public event EventHandler? Opened;
    public event EventHandler? Failed;
    public event EventHandler? Ended;
    public event EventHandler? BufferingStarted;
    public event EventHandler? BufferingEnded;

    public void Play() => _media.Play();

    public void Pause() => _media.Pause();

    public void Close()
    {
        if (_disposed) return;
        _media.Close();
        _media.Source = null;
    }

    private void OnMediaOpened(object? sender, RoutedEventArgs e) =>
        Opened?.Invoke(this, EventArgs.Empty);

    private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e) =>
        Failed?.Invoke(this, EventArgs.Empty);

    private void OnMediaEnded(object? sender, RoutedEventArgs e) =>
        Ended?.Invoke(this, EventArgs.Empty);

    private void OnBufferingStarted(object? sender, RoutedEventArgs e) =>
        BufferingStarted?.Invoke(this, EventArgs.Empty);

    private void OnBufferingEnded(object? sender, RoutedEventArgs e) =>
        BufferingEnded?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _media.MediaOpened -= OnMediaOpened;
        _media.MediaFailed -= OnMediaFailed;
        _media.MediaEnded -= OnMediaEnded;
        _media.BufferingStarted -= OnBufferingStarted;
        _media.BufferingEnded -= OnBufferingEnded;

        _media.Close();
        _media.Source = null;

        Opened = null;
        Failed = null;
        Ended = null;
        BufferingStarted = null;
        BufferingEnded = null;
    }
}
