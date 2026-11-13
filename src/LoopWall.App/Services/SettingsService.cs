using System.IO;
using System.Text.Json;
using LoopWall.App.Models;

namespace LoopWall.App.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopWall", "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream)
                   ?? new AppSettings();
        }
        catch
        {
            // A damaged settings file should not prevent the application from opening.
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings,
                new JsonSerializerOptions { WriteIndented = true });
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
