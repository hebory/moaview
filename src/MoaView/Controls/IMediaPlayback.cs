using System.Windows;

namespace MoaView.Controls;

internal interface IMediaPlayback : IDisposable
{
    FrameworkElement View { get; }
    Uri? Source { get; set; }
    Duration NaturalDuration { get; }
    bool HasAudio { get; }
    bool HasVideo { get; }
    bool IsBuffering { get; }
    int NaturalVideoWidth { get; }
    int NaturalVideoHeight { get; }
    double Volume { get; set; }
    double SpeedRatio { get; set; }
    bool IsMuted { get; set; }
    TimeSpan Position { get; set; }

    event EventHandler? Opened;
    event EventHandler? Failed;
    event EventHandler? Ended;
    event EventHandler? BufferingStarted;
    event EventHandler? BufferingEnded;

    void Play();
    void Pause();
    void Close();
}
