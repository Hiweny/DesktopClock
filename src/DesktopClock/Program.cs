using System;
using System.Windows.Forms;

namespace DesktopClock;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 全局异常兜底：任何未处理异常都写进日志，避免"双击没反应还查不到原因"
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("AppDomain.UnhandledException: " + e.ExceptionObject);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            Log.Error("Application.ThreadException", e.Exception);

        try
        {
            try
            {
                Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch { /* 清单已声明 DPI 感知，忽略 */ }

            float scale = 1f;
            try
            {
                uint dpi = Native.GetDpiForSystem();
                Log.Info("system DPI = " + dpi);
                if (dpi >= 96) scale = dpi / 96f;
            }
            catch { }

            Log.Info("=== DesktopClock starting, version "
                     + (typeof(Program).Assembly.GetName().Version?.ToString() ?? "?")
                     + ", OS " + Environment.OSVersion.VersionString
                     + ", 64bit=" + Environment.Is64BitProcess + " ===");

            ApplicationConfiguration.Initialize();
            Application.Run(new ClockForm(scale));

            Log.Info("=== exited normally ===");
        }
        catch (Exception ex)
        {
            Log.Error("Main crashed", ex);
            MessageBox.Show("DesktopClock 启动失败：\n" + ex.Message
                            + "\n\n详细日志：" + Log.FilePath,
                            "DesktopClock", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
