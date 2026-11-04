using System.IO;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class FolderImageNavigationTests
{
    private readonly FolderImageNavigationService _navigationService = new();

    [Fact]
    public void ResolveContext_FiltersSupportedImagesInNaturalSortOrder_ExcludingUnsupportedAndNestedFiles()
    {
        using var temp = new TempTestDirectory();

        var img10 = SampleImageFactory.WriteValidImage(temp.GetPath("photo_10.jpg"), ImageFormatKind.Jpeg, 16, 12);
        var img2 = SampleImageFactory.WriteValidImage(temp.GetPath("photo_2.png"), ImageFormatKind.Png, 16, 12);
        var img1 = SampleImageFactory.WriteValidImage(temp.GetPath("photo_1.bmp"), ImageFormatKind.Bmp, 16, 12);
        var img03 = SampleImageFactory.WriteValidImage(temp.GetPath("Photo_03.gif"), ImageFormatKind.Gif, 16, 12);
        var img20 = SampleImageFactory.WriteValidImage(temp.GetPath("photo_20.tiff"), ImageFormatKind.Tiff, 16, 12);

        File.WriteAllText(temp.GetPath("00_notes.txt"), "text");
        File.WriteAllText(temp.GetPath("photo_4.webp"), "RIFF....WEBP");
        File.WriteAllText(temp.GetPath("archive.zip"), "PK");

        var nestedDir = Path.Combine(temp.DirectoryPath, "nested");
        Directory.CreateDirectory(nestedDir);
        SampleImageFactory.WriteValidImage(Path.Combine(nestedDir, "photo_0.png"), ImageFormatKind.Png, 12, 12);

        var context = _navigationService.ResolveContext(img03);

        Assert.Equal(5, context.TotalCount);
        Assert.True(context.HasCurrentInList);
        Assert.Equal(2, context.CurrentIndex);
        Assert.True(context.CanMovePrevious);
        Assert.True(context.CanMoveNext);
        Assert.Equal(
            [img1, img2, img03, img10, img20],
            context.SiblingImagePaths);
    }

    [Fact]
    public void GetNextAndPreviousImagePath_WrapsAroundFirstAndLastImages()
    {
        using var temp = new TempTestDirectory();
        var first = SampleImageFactory.WriteValidImage(temp.GetPath("01.jpg"), ImageFormatKind.Jpeg, 20, 20);
        var second = SampleImageFactory.WriteValidImage(temp.GetPath("02.png"), ImageFormatKind.Png, 20, 20);
        var third = SampleImageFactory.WriteValidImage(temp.GetPath("03.bmp"), ImageFormatKind.Bmp, 20, 20);

        Assert.Equal(second, _navigationService.GetNextImagePath(first));
        Assert.Equal(third, _navigationService.GetNextImagePath(second));
        Assert.Equal(first, _navigationService.GetNextImagePath(third));

        Assert.Equal(third, _navigationService.GetPreviousImagePath(first));
        Assert.Equal(second, _navigationService.GetPreviousImagePath(third));
        Assert.Equal(first, _navigationService.GetPreviousImagePath(second));
    }

    [Fact]
    public void SingleImageOrEmptyOrDirectoryInput_DisablesSiblingNavigation()
    {
        using var temp = new TempTestDirectory();
        var onlyOne = SampleImageFactory.WriteValidImage(temp.GetPath("alone.png"), ImageFormatKind.Png, 24, 24);

        var singleContext = _navigationService.ResolveContext(onlyOne);
        Assert.Equal(1, singleContext.TotalCount);
        Assert.Equal(0, singleContext.CurrentIndex);
        Assert.False(singleContext.CanMoveNext);
        Assert.False(singleContext.CanMovePrevious);
        Assert.Null(_navigationService.GetNextImagePath(onlyOne));
        Assert.Null(_navigationService.GetPreviousImagePath(onlyOne));

        var folderContext = _navigationService.ResolveContext(temp.DirectoryPath);
        Assert.Equal(0, folderContext.TotalCount);
        Assert.False(folderContext.CanMoveNext);

        var blankContext = _navigationService.ResolveContext("   ");
        Assert.Equal(0, blankContext.TotalCount);
        Assert.False(blankContext.CanMovePrevious);
    }

    [Fact]
    public void UnsupportedOrDeletedCurrentFile_MovesToAdjacentSupportedImagesInSameFolder()
    {
        using var temp = new TempTestDirectory();
        var a = SampleImageFactory.WriteValidImage(temp.GetPath("01_alpha.jpg"), ImageFormatKind.Jpeg, 16, 16);
        var midUnsupported = temp.GetPath("02_readme.txt");
        File.WriteAllText(midUnsupported, "not an image");
        var b = SampleImageFactory.WriteValidImage(temp.GetPath("03_beta.png"), ImageFormatKind.Png, 16, 16);

        var contextFromTxt = _navigationService.ResolveContext(midUnsupported);
        Assert.Equal(2, contextFromTxt.TotalCount);
        Assert.False(contextFromTxt.HasCurrentInList);
        Assert.True(contextFromTxt.CanMoveNext);
        Assert.True(contextFromTxt.CanMovePrevious);

        Assert.Equal(b, _navigationService.GetNextImagePath(midUnsupported));
        Assert.Equal(a, _navigationService.GetPreviousImagePath(midUnsupported));
    }
}
