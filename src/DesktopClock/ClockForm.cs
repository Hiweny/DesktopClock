using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopClock;

/// <summary>
/// 桌面透明时钟。
/// 实现要点：
///  - 无边框 + 逐像素 Alpha 分层窗口（UpdateLayeredWindow），只有文字、没有背板；
///  - WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW：不抢焦点、不进任务栏、不出现在 Alt+Tab；
///  - 始终保持在所有窗口的最底层（HWND_BOTTOM），因此位于桌面上、不会遮挡其它程序；
///  - 按住文字可拖动，右键弹出菜单；
///  - 透明像素处鼠标自动穿透，不影响点击桌面图标。
/// </summary>
public sealed class ClockForm : Form
{
    private const int BaseWidth = 1000;
    private const int BaseHeight = 380;

    private const float TimeSize = 132f;
    private const float DateSize = 28f;
    private const float PoemSize = 32f;
    private const float AttrSize = 24f;

    private const float YTime = 14f, HTime = 184f;
    private const float YDate = 202f, HDate = 44f;
    private const float YPoem = 250f, HPoem = 52f;
    private const float YAttr = 308f, HAttr = 40f;

    private readonly float _scale;
    private int _w, _h;

    private readonly PoemService _poemService = new();
    private Poem _current = Poem.Offline(DateTime.Now);

    private readonly System.Windows.Forms.Timer _tick = new();
    private int _lastMinute = -1;
    private DateTime _lastPoemFetch = DateTime.MinValue;

    private Font _timeFont = null!, _dateFont = null!, _poemFont = null!, _attrFont = null!;
    private static readonly CultureInfo Zh = new("zh-CN");

    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartWindow;

    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autoItem;

    public ClockForm(float scale)
    {
        _scale = scale <= 0 ? 1f : scale;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = false;
        Text = "DesktopClock";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _w = Math.Max(200, (int)Math.Round(BaseWidth * _scale));
        _h = Math.Max(120, (int)Math.Round(BaseHeight * _scale));

        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Size = new Size(_w, _h);
        Location = CenterIn(screen);

        BuildFonts();

        _menu = new ContextMenuStrip { ShowImageMargin = false };
        _menu.Items.Add(new ToolStripMenuItem("换一句诗词", null, (_, _) => RefreshPoem()));
        _menu.Items.Add(new ToolStripMenuItem("回到屏幕中央", null, (_, _) => { Recenter(); RenderNow(); }));
        _autoItem = new ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = Autostart.IsEnabled() };
        _autoItem.Click += (_, _) => Autostart.SetEnabled(_autoItem.Checked);
        _menu.Items.Add(_autoItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => Application.Exit()));

        _tick.Interval = 1000;
        _tick.Tick += OnTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    // 绘制完全交给 UpdateLayeredWindow，屏蔽系统擦除/重绘
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void WndProc(ref Message m)
    {
        const int WM_ERASEBKGND = 0x0014;
        if (m.Msg == WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            Log.Info("#1 screen bounds = " + Screen.PrimaryScreen?.Bounds);
            RenderNow();
            Log.Info("#2 first render done");

            // 沉到所有窗口的最底层，且不抢焦点
            bool ok = Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Log.Info("#3 SetWindowPos(BOTTOM) = " + ok + ", err=" + Marshal.GetLastWin32Error());

            if (Native.GetWindowRect(Handle, out var r))
                Log.Info("#4 rect = " + r.Left + "," + r.Top + "  " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
        }
        catch (Exception ex)
        {
            Log.Error("OnHandleCreated failed", ex);
        }

        try
        {
            if (!_autoItem.Checked && !Autostart.IsEnabled())
            {
                Autostart.SetEnabled(true);
                _autoItem.Checked = true;
                Log.Info("autostart enabled");
            }
        }
        catch (Exception ex) { Log.Error("autostart failed", ex); }

        _tick.Start();
        _lastPoemFetch = DateTime.Now;
        RefreshPoem();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Stop();
        _tick.Dispose();
        base.OnFormClosed(e);
    }

    private Point CenterIn(Rectangle screen)
    {
        int x = screen.X + Math.Max(0, (screen.Width - _w) / 2);
        int y = screen.Y + Math.Max(0, (screen.Height - _h) / 2);
        return new Point(x, y);
    }

    private void Recenter()
    {
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = CenterIn(screen);
        Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // ------------------------------------------------------------------
    // 定时刷新
    // ------------------------------------------------------------------
    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var now = DateTime.Now;
            if (now.Minute != _lastMinute)
            {
                _lastMinute = now.Minute;
                RenderNow();
            }
            if ((now - _lastPoemFetch).TotalMinutes >= 10)
            {
                _lastPoemFetch = now;
                RefreshPoem();
            }
        }
        catch (Exception ex) { Log.Error("OnTick failed", ex); }
    }

    // ------------------------------------------------------------------
    // 绘制 & 提交
    // ------------------------------------------------------------------
    private void RenderNow()
    {
        if (_w <= 0 || _h <= 0 || !IsHandleCreated) return;

        try
        {
            using var bmp = new Bitmap(_w, _h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                var now = DateTime.Now;
                string time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
                string date = now.ToString("dddd", Zh) + " · " + now.ToString("M月d日", Zh);
                string poem = _current.Text ?? string.Empty;
                string attr = _current.Attribution ?? string.Empty;

                DrawCentered(g, time, _timeFont, Brushes.White, YTime * _scale, HTime * _scale);
                DrawCentered(g, date, _dateFont, new SolidBrush(Color.FromArgb(238, 255, 255, 255)), YDate * _scale, HDate * _scale);
                DrawCentered(g, poem, _poemFont, new SolidBrush(Color.FromArgb(238, 255, 255, 255)), YPoem * _scale, HPoem * _scale);
                if (!string.IsNullOrEmpty(attr))
                    DrawCentered(g, attr, _attrFont, new SolidBrush(Color.FromArgb(190, 255, 255, 255)), YAttr * _scale, HAttr * _scale);
            }

            Blit(bmp);
        }
        catch (Exception ex)
        {
            Log.Error("RenderNow failed", ex);
        }
    }

    private void DrawCentered(Graphics g, string text, Font font, Brush brush, float y, float height)
    {
        using var sf = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var rect = new RectangleF(0, y, _w, height);
        g.DrawString(text, font, brush, rect, sf);
    }

    private unsafe void Blit(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);

        var bmi = new Native.BITMAPINFO
        {
            bmiHeader = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            },
            bmiColors = 0
        };

        IntPtr hBitmap = Native.CreateDIBSection(memDc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero)
        {
            Log.Error("CreateDIBSection failed, err=" + Marshal.GetLastWin32Error());
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            return;
        }

        IntPtr old = Native.SelectObject(memDc, hBitmap);

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* src = (byte*)data.Scan0;
            byte* dst = (byte*)bits;
            for (int y = 0; y < h; y++)
            {
                byte* srow = src + y * data.Stride;
                byte* drow = dst + y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    byte b = srow[i + 0], gg = srow[i + 1], r = srow[i + 2], a = srow[i + 3];
                    drow[i + 0] = (byte)(b * a / 255);
                    drow[i + 1] = (byte)(gg * a / 255);
                    drow[i + 2] = (byte)(r * a / 255);
                    drow[i + 3] = a;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        var size = new Native.SIZE(w, h);
        var srcPt = new Native.POINT(0, 0);
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = Native.AC_SRC_ALPHA
        };

        bool ok = Native.UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref srcPt, 0, ref blend, Native.ULW_ALPHA);
        if (!ok) Log.Error("UpdateLayeredWindow failed, err=" + Marshal.GetLastWin32Error());

        Native.SelectObject(memDc, old);
        Native.DeleteObject(hBitmap);
        Native.DeleteDC(memDc);
        Native.ReleaseDC(IntPtr.Zero, screenDc);
    }

    private async void RefreshPoem()
    {
        try
        {
            var p = await _poemService.FetchAsync();
            _current = p;
            Log.Info("poem: " + p.Text + "  [" + p.Attribution + "] offline=" + p.IsOffline);
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(new Action(RenderNow));
            else RenderNow();
        }
        catch (Exception ex)
        {
            Log.Error("RefreshPoem failed", ex);
        }
    }

    // ------------------------------------------------------------------
    // 拖动 / 右键
    // ------------------------------------------------------------------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStartCursor = Cursor.Position;
            _dragStartWindow = Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var cur = Cursor.Position;
        Location = new Point(_dragStartWindow.X + (cur.X - _dragStartCursor.X),
                             _dragStartWindow.Y + (cur.Y - _dragStartCursor.Y));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            _menu.Show(Cursor.Position);
        }
        _dragging = false;
    }

    // ------------------------------------------------------------------
    // 字体
    // ------------------------------------------------------------------
    private void BuildFonts()
    {
        string latin = PickFont("Georgia", "Times New Roman", "Constantia", "Cambria", "SimSun", "宋体");
        string cjk = PickFont("Source Han Serif SC", "Noto Serif CJK SC", "Songti SC", "STSong", "SimSun", "宋体", "NSimSun", "KaiTi", "楷体");
        Log.Info("fonts: latin=" + latin + ", cjk=" + cjk);

        _timeFont = new Font(latin, TimeSize * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _dateFont = new Font(cjk, DateSize * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _poemFont = new Font(cjk, PoemSize * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _attrFont = new Font(cjk, AttrSize * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static string PickFont(params string[] candidates)
    {
        try
        {
            using var col = new InstalledFontCollection();
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in col.Families) available.Add(f.Name);
            foreach (var c in candidates)
            {
                if (available.Contains(c)) return c;
            }
        }
        catch { }
        return FontFamily.GenericSerif.Name;
    }
}
