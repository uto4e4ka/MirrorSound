using Microsoft.Win32;

namespace MirrorSound;

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
