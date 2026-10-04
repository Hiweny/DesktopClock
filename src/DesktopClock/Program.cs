using System;
using System.Windows.Forms;

namespace DesktopClock;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // 忽略：清单已声明 DPI 感知
        }

        float scale = 1f;
        try
        {
            uint dpi = Native.GetDpiForSystem();
            if (dpi >= 96) scale = dpi / 96f;
        }
        catch
        {
            // 忽略
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new ClockForm(scale));
    }
}
