using System.IO;
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

    public PreviewResult Load(string? filePath)
    {
        var path = filePath?.Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(path)) return _images.Load(filePath);
        try
        {
            path = Path.GetFullPath(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (Directory.Exists(path) || !File.Exists(path) ||
                !(extension is ".zip" or ".riv" or ".pur" || IsTextExtension(extension) || IsDocumentExtension(extension)))
                return _images.Load(path);

            var limit = IsTextExtension(extension) ? PreviewLimits.MaxTextBytes :
                extension == ".riv" ? 16 * 1024 * 1024 : PreviewLimits.MaxContainerBytes;
            var bytes = PreviewLimits.ReadFile(path, limit);
            if (extension == ".pur")
            {
                var pur = PurPreviewReader.Read(bytes, path);
                return LoadPurImage(pur, pur.Images[0]);
            }
            if (extension == ".zip")
            {
                using var zip = new SafeZipArchive(bytes);
                return new PreviewResult
                {
                    State = PreviewStateKind.ArchiveLoaded, FilePath = path, FileName = Path.GetFileName(path),
                    Extension = extension, FileSizeBytes = bytes.LongLength,
                    Archive = new ArchivePreview(path, zip.Entries) { Data = bytes },
                    Title = zip.Entries.Count == 0 ? "비어 있는 ZIP 파일입니다." : "ZIP 내부 항목을 선택하세요",
                    Message = "왼쪽 목록에서 이미지 또는 일반 텍스트 파일을 선택하면 압축을 풀지 않고 표시합니다.",
                };
            }
            return LoadContent(bytes, Path.GetFileName(path)) with { FilePath = path };
        }
        catch (Exception ex) when (IsPreviewError(ex))
        {
            return Error(path, ex);
        }
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
            using var zip = new SafeZipArchive(archive.Data);
            if ((uint)entry.Index >= zip.Entries.Count || zip.Entries[entry.Index] != entry)
                throw new InvalidDataException("ZIP 항목 정보가 일치하지 않습니다.");
            if (entry.IsDirectory)
                result = new PreviewResult { State = PreviewStateKind.ArchiveLoaded, Title = "ZIP 내부 폴더입니다.", Message = "목록에서 파일을 선택해 주세요." };
            else if (entry.IsEncrypted)
                throw new EncryptedPreviewException("암호화된 ZIP 항목은 미리보기를 지원하지 않습니다.");
            else
            {
                var extension = Path.GetExtension(entry.Name);
                if (!IsTextExtension(extension) && !SupportedFileFormats.TryGetImageFormatByExtension(extension, out _))
                    result = PreviewResult.Unsupported(archive.FilePath, entry.Name, extension, "지원하지 않는 ZIP 내부 항목입니다.",
                        "ZIP 내부에서는 JPEG·PNG·BMP·GIF·TIFF 이미지와 일반 텍스트만 미리볼 수 있습니다.");
                else
                    result = LoadContent(zip.Read(entry.Index, IsTextExtension(extension) ? PreviewLimits.MaxTextBytes : PreviewLimits.MaxEntryBytes), entry.Name);
            }
        }
        catch (Exception ex) when (IsPreviewError(ex))
        {
            result = Error(archive.FilePath, ex);
        }
        return result with { Archive = archive, ArchiveEntryName = entry.Name, FilePath = archive.FilePath,
            FileName = $"{Path.GetFileName(archive.FilePath)} › {entry.Name}" };
    }

    private PreviewResult LoadContent(byte[] bytes, string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (SupportedFileFormats.TryGetImageFormatByExtension(extension, out _)) return _images.LoadBytes(bytes, name);
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

    internal static bool IsPreviewError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        NotSupportedException or ArgumentException or InvalidOperationException or XmlException or
        DecoderFallbackException or OverflowException or PreviewLimitException or EncryptedPreviewException or System.Runtime.InteropServices.COMException;

    private static PreviewResult Error(string? path, Exception ex) => PreviewResult.Failure(path, Path.GetFileName(path), Path.GetExtension(path),
        ex is EncryptedPreviewException ? "암호화된 파일입니다." : ex is PreviewLimitException ? "미리보기 제한을 넘었습니다." : "파일 내용을 표시할 수 없습니다.",
        ex is EncryptedPreviewException or PreviewLimitException or NotSupportedException ? ex.Message :
        ex is DecoderFallbackException ? "지원하는 UTF-8 또는 BOM이 있는 Unicode 텍스트가 아닙니다." :
        "파일이 손상되었거나 올바른 형식이 아니거나 읽을 수 없습니다.");
}
