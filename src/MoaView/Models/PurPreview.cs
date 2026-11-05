namespace MoaView.Models;

public sealed record PurImageEntry(int Index, int Offset, int Length, string Format, int Width, int Height)
{
    public string Name => $"포함 이미지 {Index + 1}";
    public string Description => $"{Format} · {Width} × {Height} px";
}

public sealed record PurPreview(string FilePath, IReadOnlyList<PurImageEntry> Images)
{
    internal byte[] Data { get; init; } = [];
    public int UnreadableCandidates { get; init; }
}
