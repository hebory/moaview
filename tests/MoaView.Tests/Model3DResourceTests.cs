using System.IO;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class Model3DResourceTests
{
    [Fact]
    public async Task SelectedModelAndNestedCompanions_AreReadWithoutHoldingFileLocks()
    {
        using var temp = new TempTestDirectory();
        var model = temp.GetPath("한글 모델 #1.gltf");
        File.WriteAllText(model, "{}");
        Directory.CreateDirectory(temp.GetPath("textures"));
        var texture = temp.GetPath("textures/표지.png");
        File.WriteAllBytes(texture, [1, 2, 3]);
        var resources = new Model3DResources(model, Path.GetFileName(model), null);
        Assert.Equal("{}", System.Text.Encoding.UTF8.GetString((await resources.ReadAsync(new Uri(resources.ModelUrl), default))!.Bytes));
        var read = await resources.ReadAsync(new Uri(Model3DResources.Origin + "model/textures/%ED%91%9C%EC%A7%80.png"), default);
        Assert.Equal(new byte[] { 1, 2, 3 }, read!.Bytes);
        Assert.Equal("image/png", read.ContentType);
        using var exclusive = new FileStream(texture, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("file:///C:/private.png")]
    [InlineData("https://moaview-3d.invalid:8443/model/texture.png")]
    [InlineData("https://moaview-3d.invalid/model/../outside.bin")]
    [InlineData("https://moaview-3d.invalid/model/%2e%2e%5coutside.bin")]
    [InlineData("https://moaview-3d.invalid/model/C:/private.bin")]
    [InlineData("https://moaview-3d.invalid/model/secret.txt")]
    [InlineData("https://moaview-3d.invalid/model/image.png?source=remote")]
    public async Task DisallowedReferences_ReturnNoFile(string url)
    {
        using var temp = new TempTestDirectory();
        var source = temp.GetPath("scene.gltf");
        var resources = new Model3DResources(source, "scene.gltf", null);
        Assert.Null(await resources.ReadAsync(new Uri(url), default));
    }

    [Fact]
    public async Task ArchiveModels_UseOnlyEmbeddedBytesAndCannotReadCompanions()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("model.bin");
        File.WriteAllBytes(path, [1, 2]);
        var resources = new Model3DResources(path, "folder/scene.glb", [3, 4]);
        Assert.Equal(new byte[] { 3, 4 }, (await resources.ReadAsync(new Uri(resources.ModelUrl), default))!.Bytes);
        Assert.Null(await resources.ReadAsync(new Uri(Model3DResources.Origin + "model/model.bin"), default));
    }

    [Fact]
    public async Task ResourceCountAndFileSize_AreBounded()
    {
        using var temp = new TempTestDirectory();
        var resources = new Model3DResources(null, "scene.glb", [1]);
        for (var i = 0; i < 128; i++)
            Assert.NotNull(await resources.ReadAsync(new Uri(resources.ModelUrl), default));
        await Assert.ThrowsAsync<PreviewLimitException>(() => resources.ReadAsync(new Uri(resources.ModelUrl), default));

        var oversized = temp.GetPath("too-big.glb");
        using (var stream = File.Create(oversized)) stream.SetLength(Model3DResources.MaxFileBytes + 1L);
        var result = new FilePreviewService().Load(oversized);
        Assert.Equal(PreviewStateKind.Error, result.State);
        Assert.Contains("64 MB", result.Message);
        var fromDisk = new Model3DResources(oversized, Path.GetFileName(oversized), null);
        await Assert.ThrowsAsync<PreviewLimitException>(() => fromDisk.ReadAsync(new Uri(fromDisk.ModelUrl), default));
    }
}
