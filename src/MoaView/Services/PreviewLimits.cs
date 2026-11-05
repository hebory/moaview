using System.IO;

namespace MoaView.Services;

internal static class PreviewLimits
{
    public const int MaxContainerBytes = 128 * 1024 * 1024;
    public const int MaxEntryBytes = 32 * 1024 * 1024;
    public const int MaxTextBytes = 4 * 1024 * 1024;
    public const int MaxXmlBytes = 8 * 1024 * 1024;
    public const int MaxEntries = 4000;
    public const int MaxTextCharacters = 300_000;

    public static byte[] ReadFile(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > limit)
        {
            throw new PreviewLimitException($"파일이 미리보기 크기 제한({limit / 1024 / 1024} MB)을 넘습니다.");
        }
        return Read(stream, limit);
    }

    public static byte[] Read(Stream stream, int limit)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = stream.Read(buffer, 0, Math.Min(buffer.Length, limit - (int)output.Length + 1))) != 0)
        {
            if (output.Length + count > limit)
            {
                throw new PreviewLimitException($"압축 해제된 내용이 미리보기 크기 제한({limit / 1024 / 1024} MB)을 넘습니다.");
            }
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}

internal sealed class PreviewLimitException(string message) : Exception(message);
internal sealed class EncryptedPreviewException(string message) : Exception(message);
