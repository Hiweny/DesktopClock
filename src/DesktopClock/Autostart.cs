using System;
using Microsoft.Win32;

namespace DesktopClock;

/// <summary>
/// 通过 HKCU\...\Run 注册表项实现开机自启动（无需管理员权限）。
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopClock";

    public static string ExePath
    {
        get
        {
            string? p = Environment.ProcessPath;
            if (string.IsNullOrEmpty(p))
            {
                p = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            }
            return p ?? string.Empty;
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var v = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(v);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
            {
                key.SetValue(ValueName, "\"" + ExePath + "\"");
            }
            else
            {
                if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // 忽略：注册表不可写时静默失败
        }
    }
}
