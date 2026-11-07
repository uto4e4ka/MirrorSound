using System.IO;
using System.Text.Json;

namespace MirrorSound;

/// <summary>User settings, kept in %APPDATA%\MirrorSound\settings.json (~/.config/MirrorSound on Linux).</summary>
public sealed class Settings
{
    public double Volume { get; set; } = 100;
    public string? Network { get; set; }
    public bool TrayHintShown { get; set; }

    private static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MirrorSound");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
