using System.IO;

namespace MoaView.Services;

public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>A bounded, top-level audio-file snapshot with repeat and shuffle navigation.</summary>
public sealed class AudioPlaylist
{
    public const int MaxTracks = 5000;
    private readonly IReadOnlyList<string> _tracks;
    private readonly Random _random;
    private readonly List<string> _shuffleHistory = [];
    private readonly List<string> _shuffleBag = [];
    private int _historyIndex;
    private bool _shuffle;
    private int _currentIndex;
    private RepeatMode _repeatMode;

    public AudioPlaylist(IEnumerable<string> paths, string currentPath, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        CurrentPath = Path.GetFullPath(currentPath);
        _random = random ?? new Random();

        var tracks = new List<string>(Math.Min(MaxTracks, 256));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var containsCurrent = false;
        foreach (var path in paths)
        {
            if (tracks.Count == MaxTracks) break;
            if (string.IsNullOrWhiteSpace(path) || !SupportedFileFormats.IsAudioExtension(Path.GetExtension(path))) continue;
            var fullPath = Path.GetFullPath(path);
            if (!seen.Add(fullPath)) continue;
            tracks.Add(fullPath);
            if (string.Equals(fullPath, CurrentPath, StringComparison.OrdinalIgnoreCase)) containsCurrent = true;
        }

        if (!containsCurrent)
        {
            if (tracks.Count == MaxTracks) tracks.RemoveAt(tracks.Count - 1);
            tracks.Add(CurrentPath);
        }

        tracks.Sort(FolderImageNavigationService.NaturalFilePathComparer.Instance);
        _tracks = Array.AsReadOnly(tracks.ToArray());
        _currentIndex = IndexOf(CurrentPath);
        ResetShuffleHistory();
    }

    public static AudioPlaylist FromFolder(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var tracks = new List<string>();
        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                foreach (var entry in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SupportedFileFormats.IsAudioExtension(Path.GetExtension(entry))) continue;
                    tracks.Add(entry);
                    if (tracks.Count == MaxTracks) break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            tracks.Clear();
        }

        return new AudioPlaylist(tracks, fullPath);
    }

    public IReadOnlyList<string> Tracks => _tracks;
    public string CurrentPath { get; private set; }
    public int CurrentIndex => _currentIndex;
    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value) return;
            _shuffle = value;
            ResetShuffleHistory();
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _repeatMode = value;
        }
    }

    public bool CanMovePrevious => _tracks.Count > 1 && (!_shuffle || _historyIndex > 0);

    public bool CanMoveNext => _tracks.Count > 1;

    /// <param name="automatic">True only when playback reaches the end of a track.</param>
    public string? MoveNext(bool automatic = false)
    {
        if (automatic && RepeatMode == RepeatMode.One) return CurrentPath;
        if (_tracks.Count <= 1) return automatic && RepeatMode == RepeatMode.All ? CurrentPath : null;

        if (Shuffle)
        {
            if (_historyIndex < _shuffleHistory.Count - 1)
            {
                CurrentPath = _shuffleHistory[++_historyIndex];
                _currentIndex = IndexOf(CurrentPath);
                return CurrentPath;
            }

            if (_shuffleBag.Count == 0)
            {
                if (RepeatMode != RepeatMode.All && automatic) return null;
                FillShuffleBag(excludeCurrent: true);
            }

            var next = _shuffleBag[^1];
            _shuffleBag.RemoveAt(_shuffleBag.Count - 1);
            _shuffleHistory.Add(next);
            if (_shuffleHistory.Count > MaxTracks) _shuffleHistory.RemoveAt(0);
            _historyIndex = _shuffleHistory.Count - 1;
            CurrentPath = next;
            _currentIndex = IndexOf(next);
            return CurrentPath;
        }

        if (_currentIndex + 1 < _tracks.Count)
            return SetCurrent(_tracks[_currentIndex + 1]);
        if (automatic && RepeatMode == RepeatMode.Off) return null;
        return SetCurrent(_tracks[0]);
    }

    public string? MovePrevious()
    {
        if (_tracks.Count <= 1) return null;
        if (Shuffle)
        {
            if (_historyIndex <= 0) return null;
            CurrentPath = _shuffleHistory[--_historyIndex];
            _currentIndex = IndexOf(CurrentPath);
            return CurrentPath;
        }

        return SetCurrent(_tracks[(_currentIndex - 1 + _tracks.Count) % _tracks.Count]);
    }

    public bool Select(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

        var index = IndexOf(fullPath);
        if (index < 0) return false;
        CurrentPath = _tracks[index];
        _currentIndex = index;
        ResetShuffleHistory();
        return true;
    }

    private string SetCurrent(string path)
    {
        CurrentPath = path;
        _currentIndex = IndexOf(path);
        ResetShuffleHistory();
        return CurrentPath;
    }

    private int IndexOf(string path)
    {
        for (var i = 0; i < _tracks.Count; i++)
            if (string.Equals(_tracks[i], path, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private void ResetShuffleHistory()
    {
        _shuffleHistory.Clear();
        _shuffleHistory.Add(CurrentPath);
        _historyIndex = 0;
        _shuffleBag.Clear();
        if (_shuffle) FillShuffleBag(excludeCurrent: true);
    }

    private void FillShuffleBag(bool excludeCurrent)
    {
        _shuffleBag.Clear();
        foreach (var path in _tracks)
            if (!excludeCurrent || !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase)) _shuffleBag.Add(path);

        for (var i = _shuffleBag.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_shuffleBag[i], _shuffleBag[j]) = (_shuffleBag[j], _shuffleBag[i]);
        }
    }
}
