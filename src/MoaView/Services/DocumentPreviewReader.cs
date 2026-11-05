using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MoaView.Models;

namespace MoaView.Services;

internal sealed class DocumentPreviewReader
{
    private int _remainingXmlBytes = 32 * 1024 * 1024;
    private int _remainingCharacters = PreviewLimits.MaxTextCharacters;
    private const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public IReadOnlyList<DocumentSection> Read(byte[] data, string extension)
    {
        if (extension == ".hwp") return HwpPreviewReader.Read(data);
        if (data.AsSpan().StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }))
            throw new EncryptedPreviewException("암호화된 Office 문서는 미리볼 수 없습니다. 암호를 해제한 문서를 선택해 주세요.");
        using var zip = new SafeZipArchive(data);
        if (zip.Entries.Any(e => e.IsEncrypted)) throw new EncryptedPreviewException("암호화된 문서는 미리보기를 지원하지 않습니다.");
        if (extension == ".hwpx") return ReadHwpx(zip);
        var rootRelations = ReadRelations(zip, "");
        var main = rootRelations.Values.SingleOrDefault(r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal));
        if (main is null) throw new InvalidDataException("Office 문서 관계가 없습니다.");
        var document = ReadXml(zip, main.Path);
        return extension switch
        {
            ".docx" => ReadWord(document),
            ".xlsx" => ReadWorkbook(zip, main.Path, document),
            ".pptx" => ReadPresentation(zip, main.Path, document),
            _ => throw new NotSupportedException("지원하지 않는 문서 형식입니다."),
        };
    }

    private IReadOnlyList<DocumentSection> ReadWord(XDocument document)
    {
        RequireRoot(document, "document", "wordprocessingml");
        var body = document.Root!.Elements().SingleOrDefault(e => e.Name.LocalName == "body")
            ?? throw new InvalidDataException("Word 본문이 없습니다.");
        return [Section("본문 · 표", ReadBlocks(body, _remainingCharacters))];
    }

    private IReadOnlyList<DocumentSection> ReadWorkbook(SafeZipArchive zip, string main, XDocument workbook)
    {
        RequireRoot(workbook, "workbook", "spreadsheetml");
        var relationships = ReadRelations(zip, main);
        var sharedPart = relationships.Values.FirstOrDefault(r => r.Type.EndsWith("/sharedStrings", StringComparison.Ordinal));
        var strings = sharedPart is null ? [] : ReadXml(zip, sharedPart.Path).Root!.Elements()
            .Where(e => e.Name.LocalName == "si").Select(e => string.Concat(e.Descendants().Where(n => n.Name.LocalName == "t").Select(n => n.Value))).ToArray();
        var result = new List<DocumentSection>();
        foreach (var sheet in workbook.Root!.Elements().Where(e => e.Name.LocalName == "sheets").Elements())
        {
            CheckSectionCount(result.Count);
            var relation = ResolveRelation(relationships, sheet, "/worksheet");
            var xml = ReadXml(zip, relation.Path);
            RequireRoot(xml, "worksheet", "spreadsheetml");
            var text = new StringBuilder("셀 주소\t값 (수식은 저장된 결과)\n");
            foreach (var row in xml.Root!.Elements().Where(e => e.Name.LocalName == "sheetData").Elements())
            {
                foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
                {
                    var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
                    var type = (string?)cell.Attribute("t");
                    if (type == "s")
                    {
                        if (!int.TryParse(value, out var index) || (uint)index >= strings.Length)
                            throw new InvalidDataException("공유 문자열 인덱스가 올바르지 않습니다.");
                        value = strings[index];
                    }
                    else if (type == "inlineStr") value = string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                    else if (type == "b") value = value == "1" ? "TRUE" : "FALSE";
                    var formula = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "f")?.Value;
                    if (formula is not null) value += $"  [수식: ={formula}]";
                    Append(text, $"{(string?)cell.Attribute("r") ?? "?"}\t{value}\n", _remainingCharacters);
                    if (text.Length >= _remainingCharacters) break;
                }
                if (text.Length >= _remainingCharacters) break;
            }
            result.Add(Section((string?)sheet.Attribute("name") ?? $"시트 {result.Count + 1}", text.ToString()));
            if (_remainingCharacters == 0) break;
        }
        if (result.Count == 0) throw new InvalidDataException("워크시트가 없습니다.");
        return result;
    }

    private IReadOnlyList<DocumentSection> ReadPresentation(SafeZipArchive zip, string main, XDocument presentation)
    {
        RequireRoot(presentation, "presentation", "presentationml");
        var relationships = ReadRelations(zip, main);
        var result = new List<DocumentSection>();
        foreach (var slide in presentation.Root!.Elements().Where(e => e.Name.LocalName == "sldIdLst").Elements())
        {
            CheckSectionCount(result.Count);
            var part = ResolveRelation(relationships, slide, "/slide");
            var xml = ReadXml(zip, part.Path);
            RequireRoot(xml, "sld", "presentationml");
            var tree = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "spTree")
                ?? throw new InvalidDataException("슬라이드 내용이 없습니다.");
            result.Add(Section($"슬라이드 {result.Count + 1}", ReadBlocks(tree, _remainingCharacters)));
            if (_remainingCharacters == 0) break;
        }
        if (result.Count == 0) return [Section("슬라이드", "슬라이드가 없는 문서입니다.")];
        return result;
    }

    private IReadOnlyList<DocumentSection> ReadHwpx(SafeZipArchive zip)
    {
        var package = ReadXml(zip, "Contents/content.hpf");
        XNamespace opf = "http://www.idpf.org/2007/opf";
        if (package.Root?.Name != opf + "package") throw new InvalidDataException("HWPX 패키지가 올바르지 않습니다.");
        var items = package.Root.Elements(opf + "manifest").Elements(opf + "item").ToDictionary(
            e => (string?)e.Attribute("id") ?? throw new InvalidDataException("HWPX 항목 ID가 없습니다."),
            e =>
            {
                var href = (string?)e.Attribute("href") ?? "";
                var relative = ResolvePart("Contents/content.hpf", href);
                return zip.Contains(relative) ? relative : ResolvePart("", href);
            });
        var result = new List<DocumentSection>();
        foreach (var reference in package.Root.Elements(opf + "spine").Elements(opf + "itemref"))
        {
            if (!items.TryGetValue((string?)reference.Attribute("idref") ?? "", out var path))
                throw new InvalidDataException("HWPX 구역 관계가 올바르지 않습니다.");
            var xml = ReadXml(zip, path);
            if (xml.Root?.Name.LocalName != "sec") continue;
            if (xml.Root.Name.NamespaceName is not ("http://www.hancom.co.kr/hwpml/2011/section" or "http://www.owpml.org/owpml/2021/section"))
                throw new InvalidDataException("HWPX 구역 네임스페이스가 올바르지 않습니다.");
            CheckSectionCount(result.Count);
            result.Add(Section($"구역 {result.Count + 1}", ReadBlocks(xml.Root, _remainingCharacters)));
            if (_remainingCharacters == 0) break;
        }
        if (result.Count == 0) throw new InvalidDataException("HWPX 본문 구역이 없습니다.");
        return result;
    }

    private static string ReadBlocks(XElement root, int limit)
    {
        var text = new StringBuilder();
        Visit(root, 0);
        return text.ToString();

        void Visit(XElement element, int depth)
        {
            if (depth > 64) throw new PreviewLimitException("문서의 중첩 구조가 너무 깊습니다.");
            if (text.Length >= limit) return;
            var name = element.Name.LocalName;
            if (!IsContentNamespace(element.Name.NamespaceName))
            {
                foreach (var child in element.Elements()) Visit(child, depth + 1);
                return;
            }
            if (name is "del" or "instrText") return;
            if (name == "tbl")
            {
                Append(text, "\n[표]\n", limit);
                foreach (var row in element.Elements().Where(e => e.Name.LocalName == "tr"))
                {
                    var first = true;
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "tc"))
                    {
                        if (!first) Append(text, "\t│\t", limit);
                        first = false;
                        Visit(cell, depth + 1);
                    }
                    Append(text, "\n", limit);
                }
                Append(text, "[/표]\n", limit);
                return;
            }
            if (name == "t")
            {
                foreach (var node in element.Nodes())
                {
                    if (node is XText value) Append(text, value.Value, limit);
                    else if (node is XElement child) Visit(child, depth + 1);
                }
                return;
            }
            if (name is "tab") { Append(text, "\t", limit); return; }
            if (name is "br" or "cr" or "lineBreak") { Append(text, "\n", limit); return; }
            if (name is "nbSpace" or "fwSpace") { Append(text, " ", limit); return; }
            foreach (var child in element.Elements()) Visit(child, depth + 1);
            if (name == "p") Append(text, "\n", limit);
        }
    }

    private DocumentSection Section(string title, string text)
    {
        var length = Math.Min(text.Length, _remainingCharacters);
        var value = text[..length];
        _remainingCharacters -= length;
        if (_remainingCharacters == 0 || length < text.Length)
            value += "\n[문서 미리보기 길이 제한으로 나머지 내용은 생략했습니다.]";
        if (string.IsNullOrWhiteSpace(value)) value = "표시할 텍스트가 없습니다. 이미지·도형만 있는 내용은 표시하지 않습니다.";
        return new DocumentSection(title, value);
    }

    private static void Append(StringBuilder text, string value, int limit)
    {
        var available = limit - text.Length;
        if (available > 0) text.Append(value.AsSpan(0, Math.Min(available, value.Length)));
    }

    private XDocument ReadXml(SafeZipArchive zip, string path)
    {
        var data = zip.Read(path, Math.Min(PreviewLimits.MaxXmlBytes, _remainingXmlBytes));
        _remainingXmlBytes -= data.Length;
        using var stream = new MemoryStream(data, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = PreviewLimits.MaxXmlBytes, IgnoreComments = true,
        });
        var document = XDocument.Load(reader);
        if (document.Root is null) throw new InvalidDataException("빈 XML 문서입니다.");
        if (document.Descendants().Any(e => e.Ancestors().Take(65).Count() > 64))
            throw new PreviewLimitException("XML 중첩 구조가 너무 깊습니다.");
        return document;
    }

    private Dictionary<string, PartRelation> ReadRelations(SafeZipArchive zip, string part)
    {
        var separator = part.LastIndexOf('/');
        var path = part.Length == 0 ? "_rels/.rels" : part[..(separator + 1)] + "_rels/" + part[(separator + 1)..] + ".rels";
        var xml = ReadXml(zip, path);
        if (xml.Root?.Name != XName.Get("Relationships", Relationships)) throw new InvalidDataException("관계 파일이 올바르지 않습니다.");
        var result = new Dictionary<string, PartRelation>(StringComparer.Ordinal);
        foreach (var relation in xml.Root.Elements())
        {
            if ((string?)relation.Attribute("TargetMode") == "External") continue;
            var id = (string?)relation.Attribute("Id") ?? throw new InvalidDataException("관계 ID가 없습니다.");
            result.Add(id, new PartRelation((string?)relation.Attribute("Type") ?? "", ResolvePart(part, (string?)relation.Attribute("Target") ?? "")));
        }
        return result;
    }

    private static PartRelation ResolveRelation(Dictionary<string, PartRelation> relations, XElement element, string typeSuffix)
    {
        var id = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "id" && a.Name.NamespaceName.EndsWith("/relationships", StringComparison.Ordinal))?.Value;
        if (id is null || !relations.TryGetValue(id, out var part) || !part.Type.EndsWith(typeSuffix, StringComparison.Ordinal))
            throw new InvalidDataException("내부 문서 관계를 찾을 수 없습니다.");
        return part;
    }

    private static string ResolvePart(string parent, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains('\\') || target.Contains(':') || target.Contains('#') || target.Contains('?'))
            throw new InvalidDataException("외부 또는 올바르지 않은 문서 경로입니다.");
        target = Uri.UnescapeDataString(target);
        if (target.Contains('\\') || target.Contains(':') || target.Contains('#') || target.Contains('?') || target.Any(char.IsControl))
            throw new InvalidDataException("문서 경로 인코딩이 올바르지 않습니다.");
        var combined = target.StartsWith('/') ? target[1..] : parent[..(parent.LastIndexOf('/') + 1)] + target;
        var segments = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidDataException("문서 경로가 패키지 밖을 가리킵니다.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    private static void RequireRoot(XDocument xml, string name, string namespacePart)
    {
        var transitional = $"http://schemas.openxmlformats.org/{namespacePart}/2006/main";
        var strict = $"http://purl.oclc.org/ooxml/{namespacePart}/main";
        if (xml.Root?.Name.LocalName != name || (xml.Root.Name.NamespaceName != transitional && xml.Root.Name.NamespaceName != strict))
            throw new InvalidDataException("문서 종류와 실제 내용이 일치하지 않습니다.");
    }

    private static bool IsContentNamespace(string ns) => ns is
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main" or "http://purl.oclc.org/ooxml/wordprocessingml/main" or
        "http://schemas.openxmlformats.org/drawingml/2006/main" or "http://purl.oclc.org/ooxml/drawingml/main" or
        "http://www.hancom.co.kr/hwpml/2011/paragraph" or "http://www.owpml.org/owpml/2021/paragraph";

    private static void CheckSectionCount(int count)
    {
        if (count >= 256) throw new PreviewLimitException("문서 구역·시트·슬라이드가 256개 제한을 넘습니다.");
    }
    private sealed record PartRelation(string Type, string Path);
}
