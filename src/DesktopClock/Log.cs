using System;
using System.IO;
using System.Text;

namespace DesktopClock;

/// <summary>
/// 极简日志：写入 %LOCALAPPDATA%\DesktopClock\DesktopClock.log，
/// 便于在"打不开 / 看不到"时定位问题。
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string FilePath
    {
        get
        {
            if (_path != null) return _path;
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DesktopClock");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "DesktopClock.log");
            }
            catch
            {
                try { _path = Path.Combine(AppContext.BaseDirectory, "DesktopClock.log"); }
                catch { _path = "DesktopClock.log"; }
            }
            return _path;
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Error(string msg, Exception ex) => Write("ERROR", msg + " :: " + ex);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + msg + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
