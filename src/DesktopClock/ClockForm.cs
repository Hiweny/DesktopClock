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
/// 壁纸层透明时钟。窗口被挂到桌面的 WorkerW 上：
///  - 位于壁纸之上、所有普通窗口之下，不会遮挡其它程序；
///  - 隐藏桌面图标 / Win+D 显示桌面时依然可见；
///  - 采用逐像素 Alpha 的分层窗口，实现"只有文字、没有背景块"的真透明。
/// </summary>
public sealed class ClockForm : Form
{
    // ---- 逻辑基准尺寸（单位：96 DPI 下的像素），实际显示会按屏幕 DPI 缩放 ----
    private const int BaseWidth = 1000;
    private const int BaseHeight = 380;

    private const float TimeSize = 132f;
    private const float DateSize = 28f;
    private const float PoemSize = 32f;
    private const float AttrSize = 24f;

    private const float YTime = 14f;
    private const float HTime = 184f;
    private const float YDate = 202f;
    private const float HDate = 44f;
    private const float YPoem = 250f;
    private const float HPoem = 52f;
    private const float YAttr = 308f;
    private const float HAttr = 40f;

    private readonly float _scale;
    private int _w;
    private int _h;

    private readonly PoemService _poemService = new();
    private Poem _current = Poem.Offline(DateTime.Now);

    private readonly System.Windows.Forms.Timer _tick = new();
    private int _lastMinute = -1;
    private DateTime _lastPoemFetch = DateTime.MinValue;

    private IntPtr _parent = IntPtr.Zero;

    private Font _timeFont = null!;
    private Font _dateFont = null!;
    private Font _poemFont = null!;
    private Font _attrFont = null!;

    private static readonly CultureInfo Zh = new("zh-CN");

    // 拖动
    private bool _dragging;
    private bool _moved;
    private Point _dragStartScreen;
    private Point _winStartRel;

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
        BackColor = Color.Black;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Location = new Point(-20000, -20000); // 先藏到屏幕外，避免启动瞬间闪烁

        BuildFonts();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("换一句诗词", null, (_, _) => RefreshPoem()));
        _autoItem = new ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = Autostart.IsEnabled() };
        _autoItem.Click += (_, _) => Autostart.SetEnabled(_autoItem.Checked);
        _menu.Items.Add(_autoItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => { _menu.Dispose(); Application.Exit(); }));

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

    // 所有绘制都由 UpdateLayeredWindow 完成，这里屏蔽系统擦除/重绘，避免闪烁。
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void WndProc(ref Message m)
    {
        const int WM_ERASEBKGND = 0x0014;
        if (m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EmbedIntoDesktop();
        ApplyLayout();
        RenderNow();

        // 首次运行自动登记开机自启动（用户可在右键菜单关闭）
        if (!_autoItem.Checked && !Autostart.IsEnabled())
        {
            Autostart.SetEnabled(true);
            _autoItem.Checked = true;
        }

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

    // ------------------------------------------------------------------
    // 桌面层嵌入
    // ------------------------------------------------------------------
    private void EmbedIntoDesktop()
    {
        // 请求 Progman 生成一个可供寄宿的 WorkerW
        IntPtr progman = Native.FindWindow("Progman", null);
        Native.SendMessageTimeout(progman, Native.WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        // 找到承载壁纸的 WorkerW：它应当是带有 SHELLDLL_DefView 的顶层窗口的下一个兄弟窗口
        IntPtr workerw = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            if (Native.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                workerw = Native.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        _parent = workerw != IntPtr.Zero ? workerw : progman;
        Native.SetParent(Handle, _parent);
        Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void ApplyLayout()
    {
        _w = Math.Max(200, (int)Math.Round(BaseWidth * _scale));
        _h = Math.Max(120, (int)Math.Round(BaseHeight * _scale));

        int cw = _w, ch = _h;
        if (_parent != IntPtr.Zero && Native.GetClientRect(_parent, out var rc))
        {
            int pw = rc.Right - rc.Left;
            int ph = rc.Bottom - rc.Top;
            if (pw > 0) cw = pw;
            if (ph > 0) ch = ph;
        }

        int x = (cw - _w) / 2;
        int y = (ch - _h) / 2;

        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, _w, _h,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    // ------------------------------------------------------------------
    // 绘制
    // ------------------------------------------------------------------
    private void OnTick(object? sender, EventArgs e)
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

    private void RenderNow()
    {
        if (_w <= 0 || _h <= 0 || !IsHandleCreated) return;

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

    private void DrawCentered(Graphics g, string text, Font font, Brush brush, float y, float height)
    {
        var sf = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var rect = new RectangleF(0, y, _w, height);
        g.DrawString(text, font, brush, rect, sf);
    }

    /// <summary>
    /// 把 32bpp ARGB 位图（预乘 Alpha 后）通过 UpdateLayeredWindow 提交到分层窗口。
    /// 位置不在此处改变（pptDst = NULL），由 SetWindowPos 控制。
    /// </summary>
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
                biHeight = -h, // 负值 = 自上而下
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            },
            bmiColors = 0
        };

        IntPtr hBitmap = Native.CreateDIBSection(memDc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero)
        {
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

        Native.UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref srcPt, 0, ref blend, Native.ULW_ALPHA);

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
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(new Action(RenderNow));
            else RenderNow();
        }
        catch
        {
            // 忽略：保留上一条内容
        }
    }

    // ------------------------------------------------------------------
    // 拖动 / 菜单
    // ------------------------------------------------------------------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _moved = false;
            _dragStartScreen = Cursor.Position;
            _winStartRel = GetWindowPosRelativeToParent();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var cur = Cursor.Position;
        int dx = cur.X - _dragStartScreen.X;
        int dy = cur.Y - _dragStartScreen.Y;
        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) _moved = true;

        Native.SetWindowPos(Handle, IntPtr.Zero, _winStartRel.X + dx, _winStartRel.Y + dy, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
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

    private Point GetWindowPosRelativeToParent()
    {
        if (!Native.GetWindowRect(Handle, out var r)) return Point.Empty;
        var p = new Native.POINT(r.Left, r.Top);
        if (_parent != IntPtr.Zero) Native.ScreenToClient(_parent, ref p);
        return new Point(p.X, p.Y);
    }

    // ------------------------------------------------------------------
    // 字体
    // ------------------------------------------------------------------
    private void BuildFonts()
    {
        string latin = PickFont("Georgia", "Times New Roman", "Constantia", "Cambria", "SimSun", "宋体");
        string cjk = PickFont("Source Han Serif SC", "Noto Serif CJK SC", "Songti SC", "STSong", "SimSun", "宋体", "NSimSun", "KaiTi", "楷体");

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
        catch
        {
            // 忽略
        }
        return FontFamily.GenericSerif.Name;
    }
}
