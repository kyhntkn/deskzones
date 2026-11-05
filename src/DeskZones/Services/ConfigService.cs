using System.IO;
using System.Text.Json;
using DeskZones.Models;

namespace DeskZones.Services;

public static class ConfigService
{
    private static readonly string Dir =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskZones");
    private static readonly string FilePath = System.IO.Path.Combine(Dir, "config.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? new AppConfig();
        }
        catch { /* bozuk dosya: sıfırdan başla */ }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* yazılamadıysa sessizce geç */ }
    }
}
