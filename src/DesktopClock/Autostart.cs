using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DesktopClock;

/// <summary>
/// 开机自启动（写入 HKCU\...\Run，无需管理员权限）。
///
/// 关键点：每次启动都会把注册表里的路径校正为**当前 exe 的真实路径**。
/// 这样即便你换过下载位置、移动过文件、或用的是解压版，
/// 自启项也永远指向正确文件，不会再"静默失效"。
/// 同时会清除 Windows "启动应用"里的禁用标记，避免被系统自动禁用。
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "DesktopClock";

    /// <summary>当前 exe 的真实路径（单文件发布下同样有效）。</summary>
    public static string ExePath
    {
        get
        {
            string? p = Environment.ProcessPath;
            if (string.IsNullOrEmpty(p))
            {
                try { p = Process.GetCurrentProcess().MainModule?.FileName; } catch { }
            }
            return p ?? string.Empty;
        }
    }

    private static string Command => "\"" + ExePath + "\"";

    /// <summary>是否已启用，且指向的正是当前 exe。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key?.GetValue(ValueName) is not string v || v.Length == 0) return false;
            return Normalize(v) == Normalize(Command);
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
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey, true))
            {
                if (key == null) return;
                if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
                else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
            }

            // 同步 Windows「启动应用」的启用/禁用标记（12 字节，首字节 0x02 = 启用）
            try
            {
                using var ap = Registry.CurrentUser.CreateSubKey(ApprovedKey, true);
                if (ap != null)
                {
                    if (enabled)
                    {
                        ap.SetValue(ValueName, new byte[12] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                    }
                    else if (ap.GetValue(ValueName) != null)
                    {
                        ap.DeleteValue(ValueName, false);
                    }
                }
            }
            catch { }

            Log.Info("autostart " + (enabled ? "ON" : "OFF") + " -> " + (enabled ? Command : "(removed)"));
        }
        catch (Exception ex)
        {
            Log.Error("autostart write failed", ex);
        }
    }

    /// <summary>启动时校正：注册表缺失或路径不符就重写为当前 exe。</summary>
    public static void EnsureRegistered()
    {
        try
        {
            string? current = null;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                current = key?.GetValue(ValueName) as string;
            }
            catch { }

            Log.Info("autostart check: exe=" + ExePath + " | reg=" + (current ?? "(none)"));

            if (!IsEnabled())
            {
                SetEnabled(true);
                Log.Info("autostart corrected to current exe path");
            }
        }
        catch (Exception ex)
        {
            Log.Error("autostart ensure failed", ex);
        }
    }

    private static string Normalize(string s)
        => s.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
