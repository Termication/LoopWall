namespace LoopWall.App.Models;

public sealed class AppSettings
{
    public List<string> VideoPaths { get; set; } = [];
    public string? LastSelectedVideo { get; set; }
    public bool LoopPreview { get; set; } = true;
    public bool MutePreview { get; set; } = true;
    public int PreviewVolume { get; set; } = 0;
}
