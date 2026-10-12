using Microsoft.Win32;

namespace NobreakMonitor.App;

/// <summary>Inicia o app (minimizado na bandeja) junto com o login do usuário.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NobreakMonitor";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
