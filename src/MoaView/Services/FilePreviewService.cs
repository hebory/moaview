using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using MoaView.Models;

namespace MoaView.Services;

public sealed class FilePreviewService : IPreviewService
{
    private readonly ImagePreviewService _images = new();
    public static bool IsTextExtension(string extension) => TextExtensions.Contains(extension);
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".md", ".csv", ".tsv", ".log", ".json", ".xml", ".yaml", ".yml", ".ini", ".cs", ".js", ".ts", ".py", ".html", ".css" };
    public static bool IsDocumentExtension(string extension) => extension.ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx" or ".hwp" or ".hwpx";
    private static bool IsMediaExtension(string extension) => extension is
        ".mp4" or ".m4v" or ".mov" or ".mkv" or ".avi" or ".wmv" or ".webm" or ".mpeg" or ".mpg" or
        ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".wma" or ".ogg" or ".opus" or ".aiff";

    internal static string? BrowserImageContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        ".apng" => "image/apng",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bpg" => BpgPreviewReader.ContentType,
        _ => null,
    };

    public PreviewResult Load(string? filePath)
    {
        var path = filePath?.Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(path)) return _images.Load(filePath);
        try
        {
            path = Path.GetFullPath(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (Directory.Exists(path)) return FileDetailsPreviewReader.ReadDirectory(path);
            if (!File.Exists(path) || SupportedFileFormats.TryGetImageFormatByExtension(extension, out _)) return _images.Load(path);

            if (IsMediaExtension(extension) || extension is ".ttf" or ".otf")
            {
                var size = new FileInfo(path).Length;
                var media = IsMediaExtension(extension);
                if (!media && size > PreviewLimits.MaxEntryBytes) return FileDetailsPreviewReader.ReadFile(path);
                return Specialized(Path.GetFileName(path), extension, size,
                    media ? SpecializedPreviewKind.Media : SpecializedPreviewKind.Font,
                    media ? "오디오·동영상" : "글꼴") with { FilePath = path };
            }

            var compressedTar = extension == ".tgz" || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var archiveFormat = extension is ".zip" or ".cbz" or ".epub" or ".jar" or ".apk" or ".odt" or ".ods" or
                ".odp" or ".nupkg" or ".vsix" or ".whl" or ".kmz" ? "ZIP" : AdditionalArchiveReader.GetFormat(extension);
            if (compressedTar) archiveFormat = "TAR";
            if (archiveFormat is null && extension != ".pur" && GetContentLimit(extension) is null)
                return FileDetailsPreviewReader.ReadFile(path);

            var limit = GetContentLimit(extension) ?? PreviewLimits.MaxContainerBytes;
            var bytes = PreviewLimits.ReadFile(path, limit);
            if (extension == ".pur")
            {
                var pur = PurPreviewReader.Read(bytes, path);
                return LoadPurImage(pur, pur.Images[0]);
            }
            if (archiveFormat is not null)
            {
                var archive = archiveFormat == "ZIP"
                    ? ReadZip(bytes, path)
                    : AdditionalArchiveReader.Read(compressedTar ? DecompressTar(bytes) : bytes, path, archiveFormat);
                return new PreviewResult
                {
                    State = PreviewStateKind.ArchiveLoaded, FilePath = path, FileName = Path.GetFileName(path),
                    Extension = extension, FileSizeBytes = bytes.LongLength,
                    Archive = archive,
                    Title = archive.Entries.Count == 0 ? "비어 있는 압축 파일입니다." : "압축 내부 항목을 선택하세요",
                    Message = "왼쪽 목록에서 파일을 선택하면 내용 또는 파일 정보를 표시합니다.",
                };
            }
            return LoadContent(bytes, Path.GetFileName(path)) with { FilePath = path };
        }
        catch (Exception ex) when (IsPreviewError(ex))
        {
            return Error(path, ex);
        }
    }

    private static byte[] DecompressTar(byte[] bytes)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return PreviewLimits.Read(gzip, PreviewLimits.MaxContainerBytes);
    }

    private static ArchivePreview ReadZip(byte[] bytes, string path)
    {
        using var zip = new SafeZipArchive(bytes);
        return new ArchivePreview(path, zip.Entries) { Data = bytes };
    }

    public PreviewResult LoadPurImage(PurPreview pur, PurImageEntry entry)
    {
        PreviewResult result;
        if ((uint)entry.Index >= pur.Images.Count || pur.Images[entry.Index] != entry ||
            entry.Offset < 0 || entry.Length < 0 || entry.Offset > pur.Data.Length - entry.Length)
            result = Error(pur.FilePath, new InvalidDataException());
        else
            result = _images.LoadBytes(pur.Data.AsSpan(entry.Offset, entry.Length).ToArray(),
                entry.Format == "PNG" ? "image.png" : "image.jpg");
        return result with { FilePath = pur.FilePath, FileName = Path.GetFileName(pur.FilePath),
            Extension = ".pur", FileSizeBytes = pur.Data.LongLength, Pur = pur, PurImage = entry,
            ContentDescription = "포함된 PNG/JPEG 이미지 보기 · 보드 배치·메모·외부 연결 이미지 제외. 저장된 썸네일과 원본 이미지가 함께 표시될 수 있습니다." +
                (pur.UnreadableCandidates > 0 ? " 일부 이미지 데이터를 읽지 못해 제외했습니다." : "") };
    }

    public PreviewResult LoadArchiveEntry(ArchivePreview archive, ArchiveEntryInfo entry)
    {
        PreviewResult result;
        try
        {
            if ((uint)entry.Index >= archive.Entries.Count || archive.Entries[entry.Index] != entry)
                throw new InvalidDataException("압축 파일 항목 정보가 일치하지 않습니다.");
            if (entry.IsDirectory)
                result = new PreviewResult { State = PreviewStateKind.ArchiveLoaded, Title = "압축 내부 폴더입니다.", Message = "목록에서 파일을 선택해 주세요." };
            else if (entry.IsEncrypted)
                throw new EncryptedPreviewException("암호화된 압축 항목은 열 수 없습니다.");
            else
            {
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                var contentLimit = GetContentLimit(extension);
                var limit = Math.Min(contentLimit ?? PreviewLimits.MaxEntryBytes, PreviewLimits.MaxEntryBytes);
                byte[]? data = null;
                string? unavailable = null;
                try
                {
                    data = archive.Format == "ZIP"
                        ? ReadZipEntry(archive, entry, limit, contentLimit is null)
                        : AdditionalArchiveReader.ReadEntry(archive, entry,
                            contentLimit is null ? FileDetailsPreviewReader.MaxTextPrefix : limit, contentLimit is null);
                }
                catch (NotSupportedException ex) when (archive.Format != "ZIP")
                {
                    unavailable = ex.Message;
                }
                result = data is not null && contentLimit.HasValue
                    ? LoadContent(data, entry.Name)
                    : FileDetailsPreviewReader.ReadBytes(data ?? [], entry.Name, entry.Size, unavailable: unavailable);
            }
        }
        catch (Exception ex) when (IsPreviewError(ex))
        {
            result = Error(archive.FilePath, ex);
        }
        return result with { Archive = archive, ArchiveEntryName = entry.Name, FilePath = archive.FilePath,
            FileName = $"{Path.GetFileName(archive.FilePath)} › {entry.Name}" };
    }

    private static byte[] ReadZipEntry(ArchivePreview archive, ArchiveEntryInfo entry, int limit, bool detailsOnly)
    {
        if (detailsOnly && entry.Size > limit) return [];
        using var zip = new SafeZipArchive(archive.Data);
        return zip.Read(entry.Index, limit);
    }

    private static int? GetContentLimit(string extension)
    {
        if (IsTextExtension(extension) || extension == ".svg") return PreviewLimits.MaxTextBytes;
        if (extension == ".riv") return 16 * 1024 * 1024;
        if (extension == ".pdf" || IsDocumentExtension(extension)) return PreviewLimits.MaxContainerBytes;
        if (BrowserImageContentType(extension) is not null || SupportedFileFormats.TryGetImageFormatByExtension(extension, out _))
            return PreviewLimits.MaxEntryBytes;
        return null;
    }

    private PreviewResult LoadContent(byte[] bytes, string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (SupportedFileFormats.TryGetImageFormatByExtension(extension, out _)) return _images.LoadBytes(bytes, name);
        if (extension == ".pdf")
        {
            if (bytes.AsSpan(0, Math.Min(1024, bytes.Length)).IndexOf("%PDF-"u8) < 0)
                throw new InvalidDataException();
            return Specialized(name, extension, bytes.LongLength, SpecializedPreviewKind.Pdf, "PDF", bytes);
        }
        if (BrowserImageContentType(extension) is not null)
        {
            if (extension == ".svg") ValidateSvg(bytes);
            if (extension == ".bpg") BpgPreviewReader.GetDecoderFile(bytes);
            return Specialized(name, extension, bytes.LongLength, SpecializedPreviewKind.BrowserImage,
                extension[1..].ToUpperInvariant(), bytes);
        }
        if (extension == ".riv")
        {
            if (bytes.Length < 8 || !bytes.AsSpan(0, 4).SequenceEqual("RIVE"u8))
                throw new InvalidDataException("Rive 파일 서명이 올바르지 않습니다.");
            return new PreviewResult { State = PreviewStateKind.RiveLoaded, FileName = name, Extension = extension,
                FileSizeBytes = bytes.Length, RiveData = bytes, Title = name, ContentDescription = "Rive 애니메이션" };
        }
        var sections = IsDocumentExtension(extension)
            ? new DocumentPreviewReader().Read(bytes, extension)
            : new[] { new DocumentSection(name, TextPreviewReader.Read(bytes)) };
        return new PreviewResult { State = PreviewStateKind.TextLoaded, FileName = name, Extension = extension,
            FileSizeBytes = bytes.LongLength, Sections = sections, Title = name,
            ContentDescription = IsDocumentExtension(extension) ? $"{extension.TrimStart('.').ToUpperInvariant()} · 내용 미리보기 (원본 서식 제외)" : "일반 텍스트 · UTF-8 / Unicode" };
    }

    private static void ValidateSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var xml = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = PreviewLimits.MaxTextBytes,
        });
        if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "svg" || xml.NamespaceURI != "http://www.w3.org/2000/svg")
            throw new InvalidDataException();
        while (xml.Read()) { }
    }

    private static PreviewResult Specialized(string name, string extension, long size, SpecializedPreviewKind kind, string description,
        byte[]? bytes = null) => new()
    {
        State = PreviewStateKind.SpecializedLoaded, FileName = name, Extension = extension, FileSizeBytes = size,
        Title = name, ContentDescription = description, SpecializedKind = kind, SpecializedData = bytes,
    };

    internal static bool IsPreviewError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        NotSupportedException or ArgumentException or InvalidOperationException or XmlException or
        DecoderFallbackException or OverflowException or PreviewLimitException or EncryptedPreviewException or
        System.Runtime.InteropServices.COMException or SharpCompress.Common.SharpCompressException;

    private static PreviewResult Error(string? path, Exception ex) => PreviewResult.Failure(path, Path.GetFileName(path), Path.GetExtension(path),
        ex is EncryptedPreviewException ? "암호화된 파일입니다." : ex is PreviewLimitException ? "미리보기 제한을 넘었습니다." : "파일 내용을 표시할 수 없습니다.",
        ex is EncryptedPreviewException or PreviewLimitException or NotSupportedException ? ex.Message :
        ex is DecoderFallbackException ? "지원하는 UTF-8 또는 BOM이 있는 Unicode 텍스트가 아닙니다." :
        "파일이 손상되었거나 올바른 형식이 아니거나 읽을 수 없습니다.");
}
