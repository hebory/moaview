using System.IO;

namespace MoaView.Services;

/// <summary>Serves only the chosen model and bounded companion files below its directory.</summary>
internal sealed class Model3DResources
{
    internal const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxTotalBytes = 128 * 1024 * 1024;
    internal const string Origin = "https://moaview-3d.invalid/";
    private readonly string? _sourcePath;
    private readonly string? _directory;
    private readonly byte[]? _embedded;
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private long _totalBytes;
    private int _requests;

    internal Model3DResources(string? sourcePath, string name, byte[]? embedded)
    {
        _sourcePath = sourcePath;
        _embedded = embedded;
        _directory = embedded is null && sourcePath is not null ? Path.GetDirectoryName(Path.GetFullPath(sourcePath)) : null;
        ModelUrl = Origin + "model/" + Uri.EscapeDataString(Path.GetFileName(name));
    }

    internal string ModelUrl { get; }

    internal async Task<ModelResource?> ReadAsync(Uri uri, CancellationToken cancellation)
    {
        if (uri.Scheme != "https" || uri.Host != "moaview-3d.invalid" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0) return null;
        await _readLock.WaitAsync(cancellation);
        try
        {
            if (++_requests > 128) throw new PreviewLimitException("3D 미리보기에서 읽을 수 있는 리소스 수(128개)를 넘었습니다.");
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            byte[] bytes;
            string type;
            if (uri.GetLeftPart(UriPartial.Path) == ModelUrl)
            {
                bytes = _embedded ?? await ReadFileAsync(_sourcePath!, cancellation);
                type = "application/octet-stream";
            }
            else if (path.StartsWith("/model/", StringComparison.Ordinal))
            {
                if (_directory is null) return null;
                var relative = path[7..];
                type = CompanionType(Path.GetExtension(relative));
                if (type.Length == 0) return null;
                var file = SafePath(_directory, relative);
                if (file is null) return null;
                bytes = await ReadFileAsync(file, cancellation);
            }
            else
            {
                var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "ThreeD");
                type = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".js" => "text/javascript; charset=utf-8",
                    ".css" => "text/css; charset=utf-8",
                    _ => "",
                };
                if (type.Length == 0) return null;
                var file = SafePath(assets, path.TrimStart('/'));
                if (file is null) return null;
                bytes = await ReadFileAsync(file, cancellation);
            }
            _totalBytes += bytes.LongLength;
            if (_totalBytes > MaxTotalBytes || bytes.Length > MaxFileBytes)
                throw new PreviewLimitException("3D 모델과 리소스의 미리보기 크기 제한(개별 64 MB, 합계 128 MB)을 넘었습니다.");
            return new(bytes, type);
        }
        finally { _readLock.Release(); }
    }

    private static Task<byte[]> ReadFileAsync(string path, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        var bytes = PreviewLimits.ReadFile(path, MaxFileBytes);
        cancellation.ThrowIfCancellationRequested();
        return bytes;
    }, cancellation);

    private static string CompanionType(string extension) => extension.ToLowerInvariant() switch
    {
        ".bin" => "application/octet-stream",
        ".mtl" => "text/plain; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".avif" => "image/avif",
        _ => "",
    };

    private static string? SafePath(string root, string relative)
    {
        // Reject traversal, drive/UNC paths, alternate streams and reparse points before opening.
        if (relative.Length == 0 || relative.Contains(':') || relative.Contains('\\') ||
            relative.Split('/').Any(part => part is ".." or "." or "")) return null;
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        for (var current = full; current.Length >= prefix.Length - 1; current = Path.GetDirectoryName(current)!)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            if (current.Length == prefix.Length - 1) break;
        }
        return full;
    }

    internal sealed record ModelResource(byte[] Bytes, string ContentType);
}
