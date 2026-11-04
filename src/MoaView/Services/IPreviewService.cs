using MoaView.Models;

namespace MoaView.Services;

public interface IPreviewService
{
    PreviewResult Load(string? filePath);
}
