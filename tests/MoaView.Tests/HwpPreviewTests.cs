using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class HwpPreviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hwp_ReadsCompoundStorageAndCompressedOrRawBody(bool compressed)
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("sample.hwp");
        WriteSample(path, compressed ? 1u : 0u);
        var result = new FilePreviewService().Load(path);
        Assert.Equal(PreviewStateKind.TextLoaded, result.State);
        var text = Assert.Single(result.Sections).Text;
        Assert.Contains("한글 본문\t탭 다음", text);
        Assert.Contains("[표]", text);
        Assert.Contains("셀 내용", text);
        File.Delete(path);
        Assert.Contains("한글 본문", text);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(4u)]
    [InlineData(16u)]
    [InlineData(256u)]
    [InlineData(1024u)]
    public void Hwp_RejectsPasswordDistributionAndDrm(uint flags)
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("protected.hwp");
        WriteSample(path, flags);
        var result = new FilePreviewService().Load(path);
        Assert.Equal(PreviewStateKind.Error, result.State);
        Assert.Contains("보호", result.Message);
    }

    [Fact]
    public void Hwp_TruncatedRecordsAndInvalidContainerBecomeErrorResults()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("broken.hwp");
        WriteSample(path, 0, malformed: true);
        Assert.Equal(PreviewStateKind.Error, new FilePreviewService().Load(path).State);
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        Assert.Equal(PreviewStateKind.Error, new FilePreviewService().Load(path).State);
    }

    private static void WriteSample(string path, uint flags, bool malformed = false)
    {
        Marshal.ThrowExceptionForHR(StgCreateDocfile(path, 0x1012, 0, out var root));
        try
        {
            var header = new byte[256];
            Encoding.ASCII.GetBytes("HWP Document File").CopyTo(header, 0);
            BitConverter.GetBytes(0x05000000u).CopyTo(header, 32);
            BitConverter.GetBytes(flags).CopyTo(header, 36);
            WriteStream(root, "FileHeader", header);
            root.CreateStorage("BodyText", 0x1012, 0, 0, out var body);
            try
            {
                using var raw = new MemoryStream();
                using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
                {
                    Record(writer, 66, new byte[22]);
                    using var paragraph = new MemoryStream();
                    paragraph.Write(Encoding.Unicode.GetBytes("한글 본문"));
                    paragraph.Write(new byte[] { 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9, 0 });
                    paragraph.Write(Encoding.Unicode.GetBytes("탭 다음\r"));
                    Record(writer, 67, paragraph.ToArray());
                    Record(writer, 77, new byte[16]);
                    Record(writer, 67, Encoding.Unicode.GetBytes("셀 내용\r"), extended: true);
                    if (malformed) writer.Write((1024u << 20) | 67u);
                }
                var records = raw.ToArray();
                if ((flags & 1) != 0)
                {
                    using var packed = new MemoryStream();
                    using (var compressor = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(records);
                    records = packed.ToArray();
                }
                WriteStream(body, "Section0", records);
                body.Commit(0);
            }
            finally { Marshal.ReleaseComObject(body); }
            root.Commit(0);
        }
        finally { Marshal.ReleaseComObject(root); }
    }

    private static void Record(BinaryWriter writer, uint tag, byte[] bytes, bool extended = false)
    {
        writer.Write(((extended ? 0xFFFu : (uint)bytes.Length) << 20) | (1u << 10) | tag);
        if (extended) writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteStream(IStorage storage, string name, byte[] bytes)
    {
        storage.CreateStream(name, 0x1012, 0, 0, out var stream);
        try { stream.Write(bytes, bytes.Length, 0); stream.Commit(0); }
        finally { Marshal.ReleaseComObject(stream); }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgCreateDocfile(string path, uint mode, uint reserved, out IStorage storage);
    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStream stream);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, nint reserved1, uint mode, uint reserved2, out IStream stream);
        void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStorage storage);
        void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage? priority, uint mode, nint exclude, uint reserved, out IStorage storage);
        void CopyTo(uint count, nint interfaces, nint exclude, IStorage destination);
        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, uint flags);
        void Commit(uint flags);
    }
}
