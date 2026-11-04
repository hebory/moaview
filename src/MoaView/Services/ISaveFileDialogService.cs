using System.Windows;
using MoaView.Models;

namespace MoaView.Services;

public interface ISaveFileDialogService
{
    string? PickSaveFile(
        string suggestedFilePath,
        ImageFormatKind targetFormat,
        Window? owner = null);
}
