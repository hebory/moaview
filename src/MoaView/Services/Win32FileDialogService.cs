using System.Windows;
using Microsoft.Win32;

namespace MoaView.Services;

public sealed class Win32FileDialogService : IFileDialogService
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
}
