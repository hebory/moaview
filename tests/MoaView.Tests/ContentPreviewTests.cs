using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class ContentPreviewTests
{
    private const string Relations = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string SlideNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void Zip_ListsAndPreviewsImageTextAndBinaryEntriesWithoutExtractionOrFileLocks()
    {
        using var temp = new TempTestDirectory();
        var image = SampleImageFactory.WriteValidImage(temp.GetPath("source.png"), ImageFormatKind.Png, 42, 28);
        var path = WriteZip(temp.GetPath("sample.zip"), new Dictionary<string, byte[]>
        {
            ["folder/"] = [], ["folder/image.png"] = File.ReadAllBytes(image),
            ["../outside.txt"] = Encoding.UTF8.GetBytes("안녕하세요\nZIP 텍스트"), ["file.exe"] = [1, 2, 3],
        });
        var service = new FilePreviewService();
        var result = service.Load(path);
        Assert.Equal(PreviewStateKind.ArchiveLoaded, result.State);
        var archive = Assert.IsType<ArchivePreview>(result.Archive);
        Assert.Equal(4, archive.Entries.Count);
        File.Delete(path); // The complete snapshot has no open archive handle.
        var loaded = service.LoadArchiveEntry(archive, archive.Entries[1]);
        Assert.Equal(PreviewStateKind.ImageLoaded, loaded.State);
        Assert.Equal(42, loaded.PixelWidth);
        Assert.True(loaded.Bitmap!.IsFrozen);
        Assert.Equal(path, loaded.FilePath);
        Assert.Equal("folder/image.png", loaded.ArchiveEntryName);
        var text = service.LoadArchiveEntry(archive, archive.Entries[2]);
        Assert.Contains("안녕하세요", Assert.Single(text.Sections).Text);
        Assert.False(File.Exists(temp.GetPath("outside.txt")));
        var binary = service.LoadArchiveEntry(archive, archive.Entries[3]);
        Assert.Equal(PreviewStateKind.TextLoaded, binary.State);
        Assert.Contains("01 02 03", binary.Sections[1].Text);
        Assert.Equal(PreviewStateKind.ArchiveLoaded, service.LoadArchiveEntry(archive, archive.Entries[0]).State);
    }

    [Theory]
    [InlineData("encrypted", "암호")]
    [InlineData("crc", "손상")]
    [InlineData("oversized", "제한")]
    [InlineData("method", "손상")]
    public void Zip_RejectsEncryptedDamagedAndOversizedEntries(string damage, string message)
    {
        using var temp = new TempTestDirectory();
        var path = WriteZip(temp.GetPath("damaged.zip"), new Dictionary<string, byte[]> { ["a.txt"] = Encoding.UTF8.GetBytes("content") });
        var bytes = File.ReadAllBytes(path);
        var central = FindSignature(bytes, 0x02014b50);
        if (damage == "encrypted") { bytes[6] |= 1; bytes[central + 8] |= 1; }
        if (damage == "crc") bytes[central + 16] ^= 0x40;
        if (damage == "oversized") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), 100 * 1024 * 1024);
        if (damage == "method") { bytes[8] = 99; bytes[central + 10] = 99; }
        File.WriteAllBytes(path, bytes);
        var service = new FilePreviewService();
        var list = service.Load(path);
        Assert.NotNull(list.Archive);
        var selected = service.LoadArchiveEntry(list.Archive, list.Archive.Entries[0]);
        Assert.Equal(PreviewStateKind.Error, selected.State);
        Assert.Contains(message, selected.Title + selected.Message);
        Assert.NotNull(selected.Archive); // Other entries remain selectable after an error.
    }

    [Fact]
    public void Zip_EmptyAndBrokenContainersAreDistinct()
    {
        using var temp = new TempTestDirectory();
        var empty = WriteZip(temp.GetPath("empty.zip"), new Dictionary<string, byte[]>());
        var service = new FilePreviewService();
        var result = service.Load(empty);
        Assert.Equal(PreviewStateKind.ArchiveLoaded, result.State);
        Assert.Empty(result.Archive!.Entries);
        File.WriteAllBytes(temp.GetPath("broken.zip"), [0x50, 0x4B, 3, 4]);
        Assert.Equal(PreviewStateKind.Error, service.Load(temp.GetPath("broken.zip")).State);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf32")]
    public void Text_ReadsUnicodeAndKeepsMarkupAsPlainText(string encodingName)
    {
        using var temp = new TempTestDirectory();
        var encoding = encodingName == "utf16" ? Encoding.Unicode : encodingName == "utf32" ? Encoding.UTF32 : new UTF8Encoding(true);
        var path = temp.GetPath("note.txt");
        File.WriteAllText(path, "한글\t<script>alert(1)</script>", encoding);
        var loaded = new FilePreviewService().Load(path);
        Assert.Equal(PreviewStateKind.TextLoaded, loaded.State);
        Assert.Equal("한글\t<script>alert(1)</script>", Assert.Single(loaded.Sections).Text);
    }

    [Fact]
    public void Text_RejectsBinaryInvalidEncodingAndLimitsLargeContent()
    {
        using var temp = new TempTestDirectory();
        var service = new FilePreviewService();
        var path = temp.GetPath("note.txt");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        Assert.Equal(PreviewStateKind.Error, service.Load(path).State);
        File.WriteAllBytes(path, [0xFF, 0xC0, 0xAF]);
        Assert.Equal(PreviewStateKind.Error, service.Load(path).State);
        File.WriteAllText(path, new string('a', 310_000));
        var truncated = Assert.Single(service.Load(path).Sections).Text;
        Assert.Contains("생략", truncated);
        Assert.True(truncated.Length < 301_000);
    }

    [Fact]
    public void Docx_ResolvesMainPartAndReadsParagraphsAndTableInOrder()
    {
        using var temp = new TempTestDirectory();
        var parts = OfficePackage("custom/main.xml");
        parts["custom/main.xml"] = $"""
            <w:document xmlns:w="{WordNs}"><w:body>
            <w:p><w:r><w:t>제목</w:t><w:tab/><w:t>본문</w:t></w:r></w:p>
            <w:tbl><w:tr><w:tc><w:p><w:r><w:t>이름</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>모아뷰</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            <w:p><w:del><w:r><w:delText>삭제됨</w:delText></w:r></w:del><w:r><w:t>끝</w:t></w:r></w:p>
            </w:body></w:document>
            """;
        var result = new FilePreviewService().Load(WriteXmlZip(temp.GetPath("document.docx"), parts));
        Assert.Equal(PreviewStateKind.TextLoaded, result.State);
        var text = Assert.Single(result.Sections).Text;
        Assert.Contains("제목\t본문", text);
        Assert.Contains("[표]", text);
        Assert.True(text.IndexOf("이름", StringComparison.Ordinal) < text.IndexOf("모아뷰", StringComparison.Ordinal));
        Assert.EndsWith("끝\n", text);
        Assert.DoesNotContain("삭제됨", text);
    }

    [Fact]
    public void Xlsx_UsesWorkbookOrderSharedStringsCellAddressesAndCachedFormulaValues()
    {
        using var temp = new TempTestDirectory();
        var parts = OfficePackage("xl/workbook.xml");
        parts["xl/workbook.xml"] = $"""<workbook xmlns="{SheetNs}" xmlns:r="{OfficeRelations}"><sheets><sheet name="두번째 파일 먼저" r:id="b"/><sheet name="첫번째 파일 나중" r:id="a"/></sheets></workbook>""";
        parts["xl/_rels/workbook.xml.rels"] = RelationshipXml(("a", "worksheet", "worksheets/a.xml"), ("b", "worksheet", "worksheets/b.xml"), ("strings", "sharedStrings", "sharedStrings.xml"));
        parts["xl/sharedStrings.xml"] = $"""<sst xmlns="{SheetNs}"><si><r><t>한글</t></r><r><t> 제목</t></r></si></sst>""";
        parts["xl/worksheets/b.xml"] = $"""<worksheet xmlns="{SheetNs}"><sheetData><row r="2"><c r="A2" t="s"><v>0</v></c><c r="C2"><f>1+2</f><v>3</v></c><c r="E2" t="inlineStr"><is><t>직접 값</t></is></c></row></sheetData></worksheet>""";
        parts["xl/worksheets/a.xml"] = $"""<worksheet xmlns="{SheetNs}"><sheetData><row><c r="B1" t="b"><v>1</v></c></row></sheetData></worksheet>""";
        var result = new FilePreviewService().Load(WriteXmlZip(temp.GetPath("workbook.xlsx"), parts));
        Assert.Equal(PreviewStateKind.TextLoaded, result.State);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal("두번째 파일 먼저", result.Sections[0].Title);
        Assert.Contains("A2\t한글 제목", result.Sections[0].Text);
        Assert.Contains("C2\t3  [수식: =1+2]", result.Sections[0].Text);
        Assert.Contains("E2\t직접 값", result.Sections[0].Text);
        Assert.Contains("B1\tTRUE", result.Sections[1].Text);
    }

    [Fact]
    public void Pptx_UsesPresentationOrderRatherThanPartFileNames()
    {
        using var temp = new TempTestDirectory();
        var parts = OfficePackage("ppt/presentation.xml");
        parts["ppt/presentation.xml"] = $"""<p:presentation xmlns:p="{SlideNs}" xmlns:r="{OfficeRelations}"><p:sldIdLst><p:sldId id="256" r:id="z"/><p:sldId id="257" r:id="a"/></p:sldIdLst></p:presentation>""";
        parts["ppt/_rels/presentation.xml.rels"] = RelationshipXml(("a", "slide", "slides/slide1.xml"), ("z", "slide", "slides/slide9.xml"));
        parts["ppt/slides/slide9.xml"] = Slide("첫 슬라이드");
        parts["ppt/slides/slide1.xml"] = Slide("두번째 슬라이드");
        var result = new FilePreviewService().Load(WriteXmlZip(temp.GetPath("deck.pptx"), parts));
        Assert.Equal(PreviewStateKind.TextLoaded, result.State);
        Assert.Equal(2, result.Sections.Count);
        Assert.Contains("첫 슬라이드", result.Sections[0].Text);
        Assert.Contains("두번째 슬라이드", result.Sections[1].Text);
    }

    [Fact]
    public void Hwpx_ReadsSpineOrderMixedTextAndNestedTableContent()
    {
        using var temp = new TempTestDirectory();
        var path = WriteXmlZip(temp.GetPath("한글.hwpx"), new Dictionary<string, string>
        {
            ["Contents/content.hpf"] = """<opf:package xmlns:opf="http://www.idpf.org/2007/opf"><opf:metadata><opf:itemref idref="ignore"/></opf:metadata><opf:manifest><opf:item id="s2" href="section2.xml"/><opf:item id="s1" href="Contents/section1.xml"/></opf:manifest><opf:spine><opf:itemref idref="s2"/><opf:itemref idref="s1"/></opf:spine></opf:package>""",
            ["Contents/section2.xml"] = HwpxSection("<hp:p><hp:run><hp:t>앞<hp:tab/>뒤<hp:lineBreak/>다음 줄</hp:t><hp:tbl><hp:tr><hp:tc><hp:subList><hp:p><hp:run><hp:t>표 안의 내용</hp:t></hp:run></hp:p></hp:subList></hp:tc></hp:tr></hp:tbl></hp:run></hp:p>"),
            ["Contents/section1.xml"] = HwpxSection("<hp:p><hp:run><hp:t>두번째 구역</hp:t></hp:run></hp:p>"),
        });
        var result = new FilePreviewService().Load(path);
        Assert.Equal(PreviewStateKind.TextLoaded, result.State);
        Assert.Contains("앞\t뒤\n다음 줄", result.Sections[0].Text);
        Assert.Contains("표 안의 내용", result.Sections[0].Text);
        Assert.Contains("두번째 구역", result.Sections[1].Text);
    }

    [Theory]
    [InlineData("dtd")]
    [InlineData("external")]
    [InlineData("traversal")]
    [InlineData("wrongRoot")]
    public void Documents_RejectUnsafeOrInvalidPackageStructure(string variant)
    {
        using var temp = new TempTestDirectory();
        var parts = OfficePackage("word/document.xml");
        parts["word/document.xml"] = $"""<w:document xmlns:w="{WordNs}"><w:body/></w:document>""";
        if (variant == "dtd") parts["word/document.xml"] = "<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///C:/secret.txt'>]><x>&secret;</x>";
        if (variant == "external") parts["_rels/.rels"] = $"""<Relationships xmlns="{Relations}"><Relationship Id="main" Type="{OfficeRelations}/officeDocument" Target="https://example.invalid/secret" TargetMode="External"/></Relationships>""";
        if (variant == "traversal") parts = OfficePackage("../../outside.xml");
        if (variant == "wrongRoot") parts["word/document.xml"] = "<workbook/>";
        var result = new FilePreviewService().Load(WriteXmlZip(temp.GetPath("unsafe.docx"), parts));
        Assert.Equal(PreviewStateKind.Error, result.State);
    }

    [Fact]
    public void ViewModelAndWindow_RenderZipSelectionDocumentTextAndReturnToImage()
    {
        using var temp = new TempTestDirectory();
        var image = SampleImageFactory.WriteValidImage(temp.GetPath("photo.png"), ImageFormatKind.Png, 48, 32);
        var zip = WriteZip(temp.GetPath("browse.zip"), new Dictionary<string, byte[]>
        { ["a.png"] = File.ReadAllBytes(image), ["b.txt"] = Encoding.UTF8.GetBytes("ZIP 안의 텍스트"), ["c.bin"] = [1, 2] });
        StaTestRunner.Run(() =>
        {
            var model = new MainWindowViewModel();
            var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                model.OpenFile(zip);
                Render(window);
                Assert.True(window.VerifyRenderedState());
                var list = (ListBox)window.FindName("ArchiveEntryList");
                list.SelectedIndex = 0;
                Render(window);
                Assert.Equal(PreviewStateKind.ImageLoaded, model.State);
                Assert.True(window.VerifyRenderedState());
                Assert.False(model.CanExecuteConversion);
                Assert.False(model.MoveNextImage());
                var output = temp.GetPath("should-not-exist.png");
                Assert.False(model.ConvertCurrentImageTo(output).IsSuccess);
                Assert.False(File.Exists(output));
                list.SelectedIndex = 1;
                Render(window);
                Assert.Equal("ZIP 안의 텍스트", model.TextContent);
                Assert.True(window.VerifyRenderedState());
                list.SelectedIndex = 2;
                Render(window);
                Assert.Equal(PreviewStateKind.TextLoaded, model.State);
                Assert.Contains("c.bin", model.TextContent);
                Assert.True(model.IsArchiveVisible);
                Assert.True(window.VerifyRenderedState());
                model.OpenFile(image);
                Render(window);
                Assert.False(model.IsArchiveVisible);
                Assert.Empty(model.DocumentSections);
                Assert.True(model.CanExecuteConversion);
                Assert.True(window.VerifyRenderedState());
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Rive_ValidatesSignatureAndRejectsWrongDataWithoutStartingBrowser()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("animation.riv");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Equal(PreviewStateKind.Error, new FilePreviewService().Load(path).State);
        File.WriteAllBytes(path, [82, 73, 86, 69, 7, 0, 0, 0]);
        var result = new FilePreviewService().Load(path);
        Assert.Equal(PreviewStateKind.RiveLoaded, result.State);
        Assert.Equal(8, result.RiveData!.Length);
    }

    [Fact]
    public void DocumentWindow_SectionPickerSwitchesDisplayedSheetAndDisablesImageCommands()
    {
        using var temp = new TempTestDirectory();
        var parts = OfficePackage("xl/workbook.xml");
        parts["xl/workbook.xml"] = $"""<workbook xmlns="{SheetNs}" xmlns:r="{OfficeRelations}"><sheets><sheet name="처음" r:id="a"/><sheet name="다음" r:id="b"/></sheets></workbook>""";
        parts["xl/_rels/workbook.xml.rels"] = RelationshipXml(("a", "worksheet", "one.xml"), ("b", "worksheet", "two.xml"));
        parts["xl/one.xml"] = $"""<worksheet xmlns="{SheetNs}"><sheetData><row><c r="A1"><v>123</v></c></row></sheetData></worksheet>""";
        parts["xl/two.xml"] = $"""<worksheet xmlns="{SheetNs}"><sheetData><row><c r="B2"><v>456</v></c></row></sheetData></worksheet>""";
        var path = WriteXmlZip(temp.GetPath("sheets.xlsx"), parts);
        StaTestRunner.Run(() =>
        {
            var model = new MainWindowViewModel();
            var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                model.OpenFile(path);
                window.Show();
                Render(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Contains("A1\t123", ((TextBox)window.FindName("PreviewText")).Text);
                ((ComboBox)window.FindName("DocumentSectionPicker")).SelectedIndex = 1;
                Render(window);
                Assert.Contains("B2\t456", ((TextBox)window.FindName("PreviewText")).Text);
                Assert.True(window.VerifyRenderedState());
                Assert.False(model.ZoomInCommand.CanExecute(null));
                Assert.False(model.CanExecuteConversion);
                Assert.False(model.CanNavigateNext);
            }
            finally { window.Close(); }
        });
    }

    internal static string WriteZip(string path, Dictionary<string, byte[]> entries)
    {
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            foreach (var (name, data) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(data);
            }
        return path;
    }
    private static string WriteXmlZip(string path, Dictionary<string, string> parts) =>
        WriteZip(path, parts.ToDictionary(p => p.Key, p => Encoding.UTF8.GetBytes(p.Value)));
    private static Dictionary<string, string> OfficePackage(string main) => new()
    { ["_rels/.rels"] = RelationshipXml(("main", "officeDocument", main)) };
    private static string RelationshipXml(params (string Id, string Type, string Target)[] entries) =>
        $"<Relationships xmlns=\"{Relations}\">" + string.Concat(entries.Select(e => $"<Relationship Id=\"{e.Id}\" Type=\"{OfficeRelations}/{e.Type}\" Target=\"{e.Target}\"/>")) + "</Relationships>";
    private static string Slide(string text) => $"""<p:sld xmlns:p="{SlideNs}" xmlns:a="{DrawingNs}"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{text}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>""";
    private static string HwpxSection(string body) => $"""<hs:sec xmlns:hs="http://www.hancom.co.kr/hwpml/2011/section" xmlns:hp="http://www.hancom.co.kr/hwpml/2011/paragraph">{body}</hs:sec>""";
    private static int FindSignature(byte[] bytes, uint signature)
    {
        for (var i = 0; i <= bytes.Length - 4; i++) if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)) == signature) return i;
        throw new InvalidDataException();
    }
    private static void Render(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
