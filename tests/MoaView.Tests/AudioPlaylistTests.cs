using System.IO;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class AudioPlaylistTests
{
    [Fact]
    public void FromFolder_IncludesOnlyTopLevelAudioAndUsesNaturalOrder()
    {
        using var temp = new TempTestDirectory();
        var first = CreateTrack(temp, "song 1.mp3");
        var current = CreateTrack(temp, "song 2.FLAC");
        var last = CreateTrack(temp, "song 10.wav");
        File.WriteAllBytes(temp.GetPath("clip.mp4"), []);
        Directory.CreateDirectory(temp.GetPath("album"));
        File.WriteAllBytes(Path.Combine(temp.GetPath("album"), "nested.mp3"), []);

        var playlist = AudioPlaylist.FromFolder(current);

        Assert.Equal(new[] { first, current, last }, playlist.Tracks);
        Assert.Equal(current, playlist.CurrentPath);
        Assert.Equal(1, playlist.CurrentIndex);
    }

    [Fact]
    public void FromFolder_WhenFolderCannotBeRead_RetainsCurrentTrack()
    {
        using var temp = new TempTestDirectory();
        var current = temp.GetPath("missing.mp3");

        var playlist = AudioPlaylist.FromFolder(current);

        Assert.Equal(new[] { current }, playlist.Tracks);
        Assert.Null(playlist.MoveNext(automatic: true));
    }

    [Fact]
    public void FromFolder_ObservesCancellation()
    {
        using var temp = new TempTestDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => AudioPlaylist.FromFolder(temp.GetPath("current.mp3"), cancellation.Token));
    }

    [Fact]
    public void RepeatOffStopsAtEndButManualNavigationCycles()
    {
        using var temp = new TempTestDirectory();
        var tracks = CreateTracks(temp, "01-one.mp3", "02-two.mp3", "03-three.mp3");
        var playlist = new AudioPlaylist(tracks, tracks[1]);

        Assert.Equal(tracks[2], playlist.MoveNext(automatic: true));
        Assert.Null(playlist.MoveNext(automatic: true));
        Assert.Equal(tracks[0], playlist.MoveNext());
        Assert.Equal(tracks[2], playlist.MovePrevious());
    }

    [Fact]
    public void RepeatAllLoopsAndRepeatOneRestartsOnlyOnAutomaticAdvance()
    {
        using var temp = new TempTestDirectory();
        var tracks = CreateTracks(temp, "01-one.mp3", "02-two.wav");
        var playlist = new AudioPlaylist(tracks, tracks[1]);
        playlist.RepeatMode = RepeatMode.All;
        Assert.Equal(tracks[0], playlist.MoveNext(automatic: true));
        Assert.Equal(tracks[1], playlist.MoveNext(automatic: true));

        playlist.RepeatMode = RepeatMode.One;
        Assert.Equal(tracks[1], playlist.MoveNext(automatic: true));
        Assert.Equal(tracks[0], playlist.MoveNext());
    }

    [Fact]
    public void ShuffleVisitsEveryTrackBeforeRepeatingAndPreviousRestoresHistory()
    {
        using var temp = new TempTestDirectory();
        var tracks = CreateTracks(temp, "01-one.mp3", "02-two.mp3", "03-three.mp3", "04-four.mp3");
        var playlist = new AudioPlaylist(tracks, tracks[0], new Random(7)) { Shuffle = true };
        var played = new List<string> { playlist.CurrentPath };

        for (var i = 0; i < tracks.Length - 1; i++) played.Add(playlist.MoveNext()!);
        Assert.Equal(tracks.Length, played.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Null(playlist.MoveNext(automatic: true));

        var latest = playlist.CurrentPath;
        var previous = playlist.MovePrevious();
        Assert.NotEqual(latest, previous);
        Assert.Equal(latest, playlist.MoveNext());
    }

    [Fact]
    public void ShuffleRepeatAllStartsAnotherPassWithoutRepeatingCurrentImmediately()
    {
        using var temp = new TempTestDirectory();
        var tracks = CreateTracks(temp, "01-one.mp3", "02-two.mp3", "03-three.mp3");
        var playlist = new AudioPlaylist(tracks, tracks[0], new Random(13))
        {
            Shuffle = true,
            RepeatMode = RepeatMode.All,
        };
        var previous = playlist.CurrentPath;

        for (var i = 0; i < tracks.Length * 3; i++)
        {
            var next = playlist.MoveNext(automatic: true);
            Assert.NotNull(next);
            Assert.NotEqual(previous, next);
            previous = next!;
        }
    }

    [Fact]
    public void Select_ChangesCurrentTrackAndRejectsItemsOutsideSnapshot()
    {
        using var temp = new TempTestDirectory();
        var tracks = CreateTracks(temp, "01-one.mp3", "02-two.mp3");
        var outside = CreateTrack(temp, "new.mp3");
        var playlist = new AudioPlaylist(tracks, tracks[0], new Random(2)) { Shuffle = true };

        Assert.False(playlist.Select(outside));
        Assert.True(playlist.Select(tracks[1]));
        Assert.Equal(tracks[1], playlist.CurrentPath);
        Assert.Equal(1, playlist.CurrentIndex);
        Assert.Null(playlist.MovePrevious());
    }

    private static string[] CreateTracks(TempTestDirectory temp, params string[] names) =>
        names.Select(name => CreateTrack(temp, name)).ToArray();

    private static string CreateTrack(TempTestDirectory temp, string name)
    {
        var path = temp.GetPath(name);
        File.WriteAllBytes(path, []);
        return path;
    }
}
