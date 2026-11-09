using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage;

namespace MoaView.Services;

public sealed record AudioTrackInfo(string Title, string? Artist, string? Album);

/// <summary>Reads embedded music tags through Windows Shell properties.</summary>
public static class AudioTrackInfoReader
{
    public static async Task<AudioTrackInfo> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var fallback = Path.GetFileNameWithoutExtension(path);

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path))
                .AsTask(cancellationToken);
            var properties = await file.Properties.GetMusicPropertiesAsync().AsTask(cancellationToken);

            return new AudioTrackInfo(
                Clean(properties.Title) ?? fallback,
                Clean(properties.Artist),
                Clean(properties.Album));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            return new AudioTrackInfo(fallback, null, null);
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
