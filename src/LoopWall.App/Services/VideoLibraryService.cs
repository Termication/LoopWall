using System.IO;
using LoopWall.App.Models;

namespace LoopWall.App.Services;

public sealed class VideoLibraryService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".wmv", ".avi", ".mov", ".mkv", ".webm", ".mpeg", ".mpg" };

    public IEnumerable<WallpaperVideo> CreateEntries(IEnumerable<string> paths)
    {
        return paths
            .Where(File.Exists)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new WallpaperVideo { FullPath = path });
    }
}
