using System.IO;
using System.Text;
using MoaView.Models;

namespace MoaView.Services;

internal static class FileDetailsPreviewReader
{
    internal const int MaxTextPrefix = 512 * 1024;
    private const int MaxHexBytes = 1024;
    private const int MaxFolderItems = 500;

    public static PreviewResult ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var size = stream.Length;
        var data = new byte[(int)Math.Min(size, MaxTextPrefix)];
        var read = stream.ReadAtLeast(data, data.Length, false);
        if (read != data.Length) Array.Resize(ref data, read);
        return ReadBytes(data, Path.GetFileName(path), size, File.GetLastWriteTime(path)) with { FilePath = path };
    }

    public static PreviewResult ReadBytes(byte[] data, string name, long size, DateTime? modified = null, string? unavailable = null)
    {
        var extension = Path.GetExtension(name);
        string details = $"이름\t{name}\n형식\t{(extension.Length == 0 ? "확장자 없음" : extension)}\n크기\t{size:N0} B";
        if (modified is { } date) details += $"\n수정 시각\t{date:g}";
        if (data.Length > 0)
        {
            try
            {
                var textPrefix = data.Length <= MaxTextPrefix ? data : data[..MaxTextPrefix];
                var text = TextPreviewReader.Read(textPrefix, isPrefix: textPrefix.LongLength < size);
                return Text(name, size, "텍스트로 읽기", [new("텍스트", text), new("파일 정보", details)]);
            }
            catch (Exception ex) when (ex is DecoderFallbackException or NotSupportedException) { }
        }

        var prefix = data.AsSpan(0, Math.Min(data.Length, MaxHexBytes));
        IReadOnlyList<DocumentSection> sections = prefix.IsEmpty
            ? [new("파일 정보", details + "\n\n" + (unavailable ?? (size == 0 ? "비어 있는 파일입니다." : "파일 앞부분을 읽을 수 없습니다.")))]
            : [new("파일 정보", details + "\n\n전용 미리보기가 없는 형식입니다. ‘파일 앞부분’에서 데이터를 확인할 수 있습니다."),
               new("파일 앞부분", Hex(prefix))];
        return Text(name, size, "파일 정보", sections);
    }

    public static PreviewResult ReadDirectory(string path)
    {
        var folder = new DirectoryInfo(path);
        var items = folder.EnumerateFileSystemInfos().Take(MaxFolderItems + 1).ToArray();
        var text = new StringBuilder();
        foreach (var item in items.Take(MaxFolderItems))
            text.Append(item is DirectoryInfo ? "폴더\t" : "파일\t").AppendLine(item.Name);
        if (items.Length == 0) text.Append("비어 있는 폴더입니다.");
        else if (items.Length > MaxFolderItems) text.Append("\n앞의 500개 항목만 표시합니다.");
        return Text(folder.Name, 0, "폴더 내용", [new("항목", text.ToString())]) with { FilePath = path, Extension = string.Empty };
    }

    private static PreviewResult Text(string name, long size, string description, IReadOnlyList<DocumentSection> sections) => new()
    {
        State = PreviewStateKind.TextLoaded, FileName = name, Extension = Path.GetExtension(name), Title = name,
        FileSizeBytes = size, ContentDescription = description, Sections = sections,
    };

    private static string Hex(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder("위치      16진수 바이트                                      텍스트\n");
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            var row = data.Slice(offset, Math.Min(16, data.Length - offset));
            output.Append(offset.ToString("X8")).Append("  ");
            foreach (var value in row) output.Append(value.ToString("X2")).Append(' ');
            output.Append(' ', 3 * (16 - row.Length) + 2);
            foreach (var value in row) output.Append(value is >= 32 and <= 126 ? (char)value : '.');
            output.AppendLine();
        }
        return output.ToString();
    }
}
