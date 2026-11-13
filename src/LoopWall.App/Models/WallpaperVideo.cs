using System.IO;

namespace LoopWall.App.Models;

public sealed class WallpaperVideo
{
    public string FullPath { get; set; } = "";
    public string FileName => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    public string Location => FullPath;
}
