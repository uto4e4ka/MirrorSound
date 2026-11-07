namespace MirrorSound;

/// <summary>Start on login via an XDG autostart entry (~/.config/autostart/mirrorsound.desktop).</summary>
public static class Autostart
{
    private static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "mirrorsound.desktop");

    public static bool IsEnabled => File.Exists(FilePath);

    public static void Set(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(FilePath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, $"""
            [Desktop Entry]
            Type=Application
            Name=MirrorSound
            Comment=Звук телефона на компьютере
            Exec="{Environment.ProcessPath}" --minimized
            Icon=mirrorsound
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """);
    }
}
