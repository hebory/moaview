using System.Windows;

namespace MoaView.Services;

public interface IFileDialogService
{
    string? PickFile(Window? owner = null);
}
