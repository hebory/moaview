using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class AudioTrackInfoReaderTests
{
    [Fact]
    public async Task ReadAsync_WhenFileIsMissing_UsesFileNameAsTitle()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("untagged song.mp3");

        var info = await AudioTrackInfoReader.ReadAsync(path);

        Assert.Equal("untagged song", info.Title);
        Assert.Null(info.Artist);
        Assert.Null(info.Album);
    }

    [Fact]
    public async Task ReadAsync_ObservesCancellation()
    {
        using var temp = new TempTestDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AudioTrackInfoReader.ReadAsync(temp.GetPath("cancelled.mp3"), cancellation.Token));
    }
}
