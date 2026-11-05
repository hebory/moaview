using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class PurPreviewTests
{
    [Fact]
    public void Pur_SelectsEmbeddedImagesFromSnapshotAndReleasesFile()
    {
        using var temp = new TempTestDirectory();
        var png = ImageBytes(temp, ImageFormatKind.Png, 40, 30, Colors.Red);
        var jpeg = ImageBytes(temp, ImageFormatKind.Jpeg, 24, 18, Colors.Blue);
        var path = WriteContainer(temp.GetPath("board.PUR"), png, jpeg);
        var original = File.ReadAllBytes(path);
        var service = new FilePreviewService();
        var first = service.Load(path);
        Assert.Equal(PreviewStateKind.ImageLoaded, first.State);
        Assert.Equal(40, first.PixelWidth);
        Assert.Equal(Colors.Red, SampleImageFactory.ReadPixelColor(first.Bitmap!, 0, 0));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(first.Bitmap!.IsFrozen);
        Assert.Equal(".pur", first.Extension);
        Assert.Equal(original.Length, first.FileSizeBytes);
        var pur = Assert.IsType<PurPreview>(first.Pur);
        Assert.Equal(2, pur.Images.Count);
        File.Delete(path);
        var second = service.LoadPurImage(pur, pur.Images[1]);
        Assert.Equal(PreviewStateKind.ImageLoaded, second.State);
        Assert.Equal(24, second.PixelWidth);
        Assert.True(SampleImageFactory.ReadPixelColor(second.Bitmap!, 0, 0).B > 240);
        Assert.Equal(path, second.FilePath);
        Assert.Equal("board.PUR", second.FileName);
        Assert.Contains("보드 배치", second.ContentDescription);
        Assert.Equal(PreviewStateKind.Error, service.LoadPurImage(pur, pur.Images[0] with { Offset = 0 }).State);
    }

    [Fact]
    public void Pur_UsesPngChunkBoundariesAndJpegSegmentsNotEmbeddedEndMarkers()
    {
        using var temp = new TempTestDirectory();
        var png = ImageBytes(temp, ImageFormatKind.Png, 32, 16, Colors.Green);
        var jpeg = ImageBytes(temp, ImageFormatKind.Jpeg, 16, 8, Colors.Blue);
        byte[] fakeEnds = [0xFF, 0xD9, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130];
        var payload = fakeEnds.Concat(png).ToArray();
        var chunk = PngChunk("raNd", payload);
        var withChunk = png[..33].Concat(chunk).Concat(png[33..]).ToArray();
        var segment = new byte[4 + payload.Length];
        segment[0] = 255; segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        var withSegment = jpeg[..2].Concat(segment).Concat(jpeg[2..]).ToArray();
        var result = new FilePreviewService().Load(WriteContainer(temp.GetPath("markers.pur"), withChunk, withSegment));
        Assert.Equal(PreviewStateKind.ImageLoaded, result.State);
        Assert.Equal(2, result.Pur!.Images.Count);
        Assert.Equal(withChunk.Length, result.Pur.Images[0].Length);
        Assert.Equal(withSegment.Length, result.Pur.Images[1].Length);
        Assert.Equal(PreviewStateKind.ImageLoaded, new FilePreviewService().LoadPurImage(result.Pur, result.Pur.Images[1]).State);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("header")]
    [InlineData("crc")]
    [InlineData("png-truncated")]
    [InlineData("jpeg-truncated")]
    [InlineData("chunk-overflow")]
    public void Pur_RejectsCorruptDataWithoutLeakingPreviousImage(string damage)
    {
        using var temp = new TempTestDirectory();
        var image = ImageBytes(temp, damage == "jpeg-truncated" ? ImageFormatKind.Jpeg : ImageFormatKind.Png, 12, 8, Colors.Red);
        var path = WriteContainer(temp.GetPath("bad.pur"), image);
        var bytes = File.ReadAllBytes(path);
        if (damage == "signature") bytes[3] = 255;
        if (damage == "header") bytes = bytes[..100];
        if (damage == "crc") bytes[224 + 29] ^= 1;
        if (damage is "png-truncated" or "jpeg-truncated") bytes = bytes[..^5];
        if (damage == "chunk-overflow") BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(232), uint.MaxValue);
        File.WriteAllBytes(path, bytes);
        var result = new FilePreviewService().Load(path);
        Assert.Equal(PreviewStateKind.Error, result.State);
        Assert.Null(result.Bitmap);
    }

    [Fact]
    public void Pur_RecoversAfterFalseImageSignaturesWithPartialContentNotice()
    {
        using var temp = new TempTestDirectory();
        var png = ImageBytes(temp, ImageFormatKind.Png, 20, 10, Colors.Red);
        byte[] falseSignatures = [137, 80, 78, 71, 13, 10, 26, 10, 255, 255, 255, 255, 0xFF, 0xD8, 0xFF, 0, 0, 0];
        var path = WriteContainer(temp.GetPath("partial.pur"), falseSignatures, png);
        var result = new FilePreviewService().Load(path);
        Assert.Equal(PreviewStateKind.ImageLoaded, result.State);
        Assert.Single(result.Pur!.Images);
        Assert.Equal(20, result.PixelWidth);
        Assert.Contains("제외했습니다", result.ContentDescription);
    }

    [Fact]
    public void Pur_ExplainsMissingImagesAndUnsupportedVersions()
    {
        using var temp = new TempTestDirectory();
        var path = WriteContainer(temp.GetPath("notes.pur"), Encoding.BigEndianUnicode.GetBytes("https://example.invalid/image.png"));
        var service = new FilePreviewService();
        var empty = service.Load(path);
        Assert.Equal(PreviewStateKind.Error, empty.State);
        Assert.Contains("포함된 PNG/JPEG", empty.Message);
        var bytes = File.ReadAllBytes(path);
        Encoding.BigEndianUnicode.GetBytes("9.99").CopyTo(bytes, 4);
        File.WriteAllBytes(path, bytes);
        Assert.Contains("저장 버전", service.Load(path).Message);
    }

    [Fact]
    public void Pur_EnforcesContainerPixelAndImageCountLimits()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("large.pur");
        using (var file = File.Create(path)) file.SetLength(128L * 1024 * 1024 + 1);
        var service = new FilePreviewService();
        Assert.Contains("제한", service.Load(path).Title);
        var png = ImageBytes(temp, ImageFormatKind.Png, 2, 2, Colors.Red);
        var ihdr = png.AsSpan(16, 13).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 10000);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 10000);
        var huge = png[..8].Concat(PngChunk("IHDR", ihdr)).Concat(png[33..]).ToArray();
        WriteContainer(path, huge);
        Assert.Contains("픽셀", service.Load(path).Message);
        WriteContainer(path, Enumerable.Repeat(png, 513).ToArray());
        Assert.Contains("512개", service.Load(path).Message);
    }

    [Fact]
    public void Pur_WindowSelectsZoomsAndTransitionsWithoutConversionOrFolderNavigation()
    {
        using var temp = new TempTestDirectory();
        var png = ImageBytes(temp, ImageFormatKind.Png, 40, 30, Colors.Red);
        var jpeg = ImageBytes(temp, ImageFormatKind.Jpeg, 24, 18, Colors.Blue);
        var path = WriteContainer(temp.GetPath("board.pur"), png, jpeg);
        var textPath = temp.GetPath("note.txt");
        File.WriteAllText(textPath, "다른 미리보기");
        StaTestRunner.Run(() =>
        {
            var model = new MainWindowViewModel();
            var window = new MainWindow(model);
            try
            {
                window.Show();
                model.OpenFile(path);
                Render(window);
                Assert.True(window.VerifyRenderedState());
                Assert.True(((FrameworkElement)window.FindName("PurPanel")).IsVisible);
                var list = (ListBox)window.FindName("PurImageList");
                Assert.Equal(2, list.Items.Count);
                model.ResetZoom(); model.RotateClockwise();
                Assert.Equal(90, model.RotationDegrees);
                list.SelectedIndex = 1;
                Render(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(24, model.DisplayPixelWidth);
                Assert.Equal(0, model.RotationDegrees);
                Assert.True(model.IsFitToWindow);
                Assert.Contains("PUR · 2/2", model.MetadataSummary);
                Assert.False(model.CanExecuteConversion);
                Assert.False(model.CanConvertLoadedFormat);
                Assert.False(model.MoveNextImage());
                Assert.False(model.MovePreviousImage());
                var output = temp.GetPath("output.png");
                Assert.Equal(ConversionStatusKind.UnsupportedCombination, model.ConvertCurrentImageTo(output).Status);
                Assert.False(File.Exists(output));
                model.OpenFile(textPath);
                Render(window);
                Assert.True(window.VerifyRenderedState());
                Assert.False(model.IsPurVisible);
                Assert.Null(model.SelectedPurImage);
                Assert.False(((FrameworkElement)window.FindName("PurPanel")).IsVisible);
                model.OpenFile(path);
                model.OpenFile(temp.GetPath("source.png"));
                Render(window);
                Assert.True(window.VerifyRenderedState());
                Assert.False(model.IsPurVisible);
                Assert.True(model.CanConvertLoadedFormat);
            }
            finally { window.Close(); }
        });
    }

    // Synthetic payload containers, not a fixture exported by the proprietary PureRef app.
    // The known 1.10 header layout is sufficient for this content-only reader.
    internal static string WriteContainer(string path, params byte[][] images)
    {
        var header = new byte[224];
        BinaryPrimitives.WriteUInt32BigEndian(header, 8);
        Encoding.BigEndianUnicode.GetBytes("1.10").CopyTo(header, 4);
        using var stream = File.Create(path);
        stream.Write(header);
        foreach (var bytes in images) stream.Write(bytes);
        return path;
    }

    private static byte[] ImageBytes(TempTestDirectory temp, ImageFormatKind format, int width, int height, Color color) =>
        File.ReadAllBytes(SampleImageFactory.WriteValidImage(temp.GetPath(format == ImageFormatKind.Png ? "source.png" : "source.jpg"), format, width, height, color));

    private static byte[] PngChunk(string type, byte[] payload)
    {
        var chunk = new byte[payload.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)payload.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        var crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, payload.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        return chunk;
    }

    private static void Render(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
