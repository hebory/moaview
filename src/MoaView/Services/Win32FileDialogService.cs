using System.IO;
using System.Windows;
using Microsoft.Win32;
using MoaView.Models;

namespace MoaView.Services;

public sealed class Win32FileDialogService : IFileDialogService, ISaveFileDialogService
{
    public string? PickFile(Window? owner = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "미리 볼 파일 선택 - 모아뷰 (MoaView)",
            Filter = SupportedFileFormats.OpenFileDialogFilter,
            FilterIndex = 1,
            CheckFileExists = true,
            Multiselect = false,
        };

        var accepted = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(
        string suggestedFilePath,
        ImageFormatKind targetFormat,
        Window? owner = null)
    {
        var defaultExt = SupportedFileFormats.IsConvertibleFormat(targetFormat)
            ? SupportedFileFormats.GetDefaultExtension(targetFormat)
            : ".png";

        var initialDirectory = !string.IsNullOrWhiteSpace(suggestedFilePath)
            ? Path.GetDirectoryName(suggestedFilePath)
            : null;
        var initialFileName = !string.IsNullOrWhiteSpace(suggestedFilePath)
            ? Path.GetFileName(suggestedFilePath)
            : $"converted{defaultExt}";

        var dialog = new SaveFileDialog
        {
            Title = "변환 이미지 저장 - 모아뷰 (MoaView)",
            Filter = SupportedFileFormats.BuildSaveDialogFilter(targetFormat),
            FilterIndex = 1,
            DefaultExt = defaultExt,
            AddExtension = true,
            OverwritePrompt = false,
            FileName = initialFileName,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var accepted = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }
}
