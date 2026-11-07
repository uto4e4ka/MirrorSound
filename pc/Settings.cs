using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace MirrorSound;

/// <summary>User settings, kept in %APPDATA%\MirrorSound\settings.json.</summary>
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

/// <summary>Start with Windows via the per-user Run key (the installer writes the same value).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "MirrorSound";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(Name, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(Name, false);
    }
}
