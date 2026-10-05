using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DesktopClock;

/// <summary>
/// 桌面透明时钟。
///  - 逐像素 Alpha 分层窗口，只有文字、没有背板；
///  - 始终沉在最底层，位于桌面上、不遮挡其它程序；
///  - 被其它窗口遮挡后回到桌面时自动重绘，不会"消失"；
///  - 系统托盘图标常驻，可随时显示/隐藏、换诗、退出；
///  - 诗词按设定间隔轮换，切换带淡入淡出。
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

    private const double FadeStep = 0.08;

    private readonly float _scale;
    private int _w, _h;

    private readonly PoemService _poemService = new();
    private Poem _current = Poem.Offline(DateTime.Now);
    private Poem? _pending;
    private double _alpha = 1.0;
    private double _targetAlpha = 1.0;

    private readonly System.Windows.Forms.Timer _tick = new();  // 1 秒：时钟 + 持续重绘
    private readonly System.Windows.Forms.Timer _anim = new();  // 33 毫秒：淡入淡出

    private int _lastMinute = -1;
    private DateTime _lastRotate = DateTime.MinValue;
    private int _rotateSeconds = 60;

    private Font _timeFont = null!, _dateFont = null!, _poemFont = null!, _attrFont = null!;
    private static readonly CultureInfo Zh = new("zh-CN");

    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartWindow;
    private bool _hidden;

    private NotifyIcon _tray = null!;
    private ContextMenuStrip _menu = null!;
    private readonly List<ToolStripMenuItem> _autoItems = new();
    private readonly List<ToolStripMenuItem> _intervalItems = new();
    private readonly List<ToolStripMenuItem> _toggleItems = new();

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

        _menu = BuildMenu();
        _tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "DesktopClock · 桌面时钟",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleClock(); };

        _tick.Interval = 1000;
        _tick.Tick += OnTick;
        _anim.Interval = 33;
        _anim.Tick += OnAnim;
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

    protected override void OnPaintBackground(PaintEventArgs e) { }

    // 被遮挡后恢复显示时系统会发 WM_PAINT —— 在这里重新提交内容，时钟就不会"消失"
    protected override void OnPaint(PaintEventArgs e) => RenderNow();

    protected override void WndProc(ref Message m)
    {
        const int WM_ERASEBKGND = 0x0014;
        const int WM_SIZE = 0x0005;
        const int SIZE_MINIMIZED = 1;

        if (m.Msg == WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }

        // "显示桌面"(Win+D / 右下角按钮) 会最小化所有顶层窗口。
        // 我们立刻把自己恢复回来，让时钟在桌面上始终可见。
        if (m.Msg == WM_SIZE && m.WParam.ToInt64() == SIZE_MINIMIZED)
        {
            try { BeginInvoke(new Action(RestoreFromShowDesktop)); } catch { }
        }

        base.WndProc(ref m);
    }

    private void RestoreFromShowDesktop()
    {
        if (!IsHandleCreated || _hidden) return;
        try
        {
            Native.ShowWindow(Handle, Native.SW_RESTORE);
            SendToBottom();
            RenderNow();
            Log.Info("restored after 'show desktop'");
        }
        catch (Exception ex) { Log.Error("RestoreFromShowDesktop failed", ex); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            Log.Info("#0 hwnd = " + Handle.ToInt64());
            Log.Info("#1 screen = " + Screen.PrimaryScreen?.Bounds);
            RenderNow();
            bool ok = Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Log.Info("#2 SetWindowPos(BOTTOM) = " + ok + ", err=" + Marshal.GetLastWin32Error());
            if (Native.GetWindowRect(Handle, out var r))
                Log.Info("#3 rect = " + r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
        }
        catch (Exception ex) { Log.Error("OnHandleCreated failed", ex); }

        try
        {
            if (!Autostart.IsEnabled())
            {
                Autostart.SetEnabled(true);
                SyncAuto();
                Log.Info("autostart enabled");
            }
        }
        catch (Exception ex) { Log.Error("autostart failed", ex); }

        _lastRotate = DateTime.Now;
        _tick.Start();
        InitialLoadAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Stop();
        _anim.Stop();
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        base.OnFormClosed(e);
    }

    private Point CenterIn(Rectangle screen)
        => new(screen.X + Math.Max(0, (screen.Width - _w) / 2),
               screen.Y + Math.Max(0, (screen.Height - _h) / 2));

    private void Recenter()
    {
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = CenterIn(screen);
        SendToBottom();
    }

    private void SendToBottom()
    {
        try
        {
            Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        catch { }
    }

    // ------------------------------------------------------------------
    // 菜单 / 托盘
    // ------------------------------------------------------------------
    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip { ShowImageMargin = false };

        var toggle = new ToolStripMenuItem(_hidden ? "显示时钟" : "隐藏时钟", null, (_, _) => ToggleClock());
        _toggleItems.Add(toggle);
        m.Items.Add(toggle);

        m.Items.Add(new ToolStripMenuItem("换一句诗词", null, (_, _) => RotatePoem(true)));
        m.Items.Add(new ToolStripMenuItem("回到屏幕中央", null, (_, _) => { Recenter(); RenderNow(); }));

        var interval = new ToolStripMenuItem("诗词轮换间隔");
        foreach (var pair in new[] { ("30 秒", 30), ("1 分钟", 60), ("5 分钟", 300), ("10 分钟", 600) })
        {
            var it = new ToolStripMenuItem(pair.Item1) { Checked = _rotateSeconds == pair.Item2, Tag = pair.Item2 };
            it.Click += (s, _) =>
            {
                if (s is ToolStripMenuItem mi && mi.Tag is int secs)
                {
                    _rotateSeconds = secs;
                    _lastRotate = DateTime.Now;
                    SyncIntervals();
                }
            };
            _intervalItems.Add(it);
            interval.DropDownItems.Add(it);
        }
        m.Items.Add(interval);

        var auto = new ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = Autostart.IsEnabled() };
        auto.Click += (_, _) => { Autostart.SetEnabled(auto.Checked); SyncAuto(); };
        _autoItems.Add(auto);
        m.Items.Add(auto);

        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApp()));
        return m;
    }

    private void SyncAuto()
    {
        bool v = Autostart.IsEnabled();
        foreach (var it in _autoItems) it.Checked = v;
    }

    private void SyncIntervals()
    {
        foreach (var it in _intervalItems)
            if (it.Tag is int s) it.Checked = (s == _rotateSeconds);
    }

    private void SyncToggleText()
    {
        string t = _hidden ? "显示时钟" : "隐藏时钟";
        foreach (var it in _toggleItems) it.Text = t;
    }

    private void ToggleClock()
    {
        _hidden = !_hidden;
        Visible = !_hidden;
        if (!_hidden)
        {
            SendToBottom();
            RenderNow();
        }
        SyncToggleText();
    }

    private void ExitApp()
    {
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        Application.Exit();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            string? p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p))
            {
                var ic = Icon.ExtractAssociatedIcon(p);
                if (ic != null) return ic;
            }
        }
        catch { }
        try
        {
            var ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (ic != null) return ic;
        }
        catch { }
        return SystemIcons.Application;
    }

    // ------------------------------------------------------------------
    // 定时：时钟秒针 + 轮换 + 持续重绘
    // ------------------------------------------------------------------
    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var now = DateTime.Now;
            if (now.Minute != _lastMinute) _lastMinute = now.Minute;

            if ((now - _lastRotate).TotalSeconds >= _rotateSeconds)
            {
                _lastRotate = now;
                RotatePoem(true);
            }

            // 万一被"显示桌面"最小化（漏掉 WM_SIZE 时兜底），也自动恢复
            if (Native.IsIconic(Handle)) RestoreFromShowDesktop();

            RenderNow(); // 持续重绘：保证被遮挡恢复后内容仍在
        }
        catch (Exception ex) { Log.Error("OnTick failed", ex); }
    }

    private void OnAnim(object? sender, EventArgs e)
    {
        try
        {
            if (_targetAlpha < _alpha)
            {
                _alpha -= FadeStep;
                if (_alpha <= 0)
                {
                    _alpha = 0;
                    if (_pending != null) { _current = _pending; _pending = null; _targetAlpha = 1; }
                }
            }
            else if (_targetAlpha > _alpha)
            {
                _alpha += FadeStep;
            }

            if (Math.Abs(_alpha - _targetAlpha) < 0.001)
            {
                _alpha = _targetAlpha;
                _anim.Stop();
            }

            RenderNow();
        }
        catch (Exception ex) { Log.Error("OnAnim failed", ex); }
    }

    // ------------------------------------------------------------------
    // 诗词
    // ------------------------------------------------------------------
    private async void InitialLoadAsync()
    {
        try { _current = await _poemService.FetchAsync(); }
        catch { }
        _alpha = 1;
        _targetAlpha = 1;
        Log.Info("poem(initial): " + _current.Text + "  [" + _current.Attribution + "] offline=" + _current.IsOffline);
        RenderNow();
    }

    private void RotatePoem(bool animate)
    {
        _ = LoadThenFadeAsync(animate);
    }

    private async Task LoadThenFadeAsync(bool animate)
    {
        Poem next;
        try { next = await _poemService.FetchAsync(); }
        catch { next = Poem.Offline(DateTime.Now); }

        _pending = next;
        Log.Info("poem(next): " + next.Text + "  [" + next.Attribution + "] offline=" + next.IsOffline);

        if (animate)
        {
            _targetAlpha = 0;
            if (!_anim.Enabled) _anim.Start();
        }
        else
        {
            _current = next;
            _pending = null;
            _alpha = 1;
            _targetAlpha = 1;
            RenderNow();
        }
    }

    // ------------------------------------------------------------------
    // 绘制
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

                double a = Math.Max(0, Math.Min(1, _alpha));
                int poemA = (int)Math.Round(238 * a);
                int attrA = (int)Math.Round(190 * a);

                DrawCentered(g, time, _timeFont, Brushes.White, YTime * _scale, HTime * _scale);
                DrawCentered(g, date, _dateFont, new SolidBrush(Color.FromArgb(238, 255, 255, 255)), YDate * _scale, HDate * _scale);

                if (poemA > 0 && !string.IsNullOrEmpty(poem))
                    DrawCentered(g, poem, _poemFont, new SolidBrush(Color.FromArgb(poemA, 255, 255, 255)), YPoem * _scale, HPoem * _scale);
                if (attrA > 0 && !string.IsNullOrEmpty(attr))
                    DrawCentered(g, attr, _attrFont, new SolidBrush(Color.FromArgb(attrA, 255, 255, 255)), YAttr * _scale, HAttr * _scale);
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
                    byte b = srow[i + 0], gg = srow[i + 1], r = srow[i + 2], al = srow[i + 3];
                    drow[i + 0] = (byte)(b * al / 255);
                    drow[i + 1] = (byte)(gg * al / 255);
                    drow[i + 2] = (byte)(r * al / 255);
                    drow[i + 3] = al;
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
        if (e.Button == MouseButtons.Right) _menu.Show(Cursor.Position);
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
                if (available.Contains(c)) return c;
        }
        catch { }
        return FontFamily.GenericSerif.Name;
    }
}
