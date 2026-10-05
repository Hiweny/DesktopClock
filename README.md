# DesktopClock · 桌面古诗词时钟

一个贴在**桌面最底层**的极简透明时钟（Windows）。白色衬线字体，纯文字、无背板，可拖动，不遮挡其它窗口。

> 时间 → 日期 → 古诗词一句 → 出处/作者，居中排布，直接"浮现"在壁纸之上。

## 下载

- `DesktopClock.exe` —— 单文件版，双击即用（自包含，不需要装 .NET）。
- `DesktopClock-folder.zip` —— 免自解压版。**如果单文件版双击没反应，请用这个**（解压后运行里面的 `DesktopClock.exe`）。

## 特性

- **真透明**：逐像素 Alpha 分层窗口，只有文字，没有半透明色块。
- **贴桌面 / 不挡窗口**：无边框、无任务栏图标、不抢焦点，始终位于所有窗口的**最底层**——只在桌面上显示，不会遮挡任何程序。
- **可拖动**：按住文字拖动到任意位置；右键菜单里有"回到屏幕中央"。
- **衬线字体**：时间用 Georgia / Times New Roman，中文自动挑选本机衬线族（思源宋体 / 宋体 / 楷体等）。
- **古诗词一言**：联网时自动获取诗句（优先「一言 Hitokoto·诗词」，其次「今日诗词」）；**断网时**切换为本地"按时段变化的问候语 + 内置古诗词"。
- **开机自启动**：首次运行自动登记（写入 `HKCU\...\Run`，无需管理员权限），可在右键菜单中开关。
- **自适应 DPI**：高分屏自动缩放。

## 使用

1. 运行 `DesktopClock.exe`（若弹"未知发布者"，点「更多信息 → 仍要运行」）。
2. 按住文字可拖动；右键菜单：换一句诗词 / 回到屏幕中央 / 开机自启动 / 退出。
3. 透明区域鼠标会自动穿透，不影响点击桌面图标。

## 看不到时钟？请这样做

程序会把运行日志写到：

```
%LOCALAPPDATA%\DesktopClock\DesktopClock.log
```

（在资源管理器地址栏粘贴 `%LOCALAPPDATA%\DesktopClock` 回车即可打开）

请把这份日志发来，就能精确定位问题。常见情况：

- **杀毒软件拦截**：单文件版首次运行会自解压，360 / 火绒等可能拦截。改用 `DesktopClock-folder.zip` 解压版，或把程序加入白名单。
- **桌面被其它窗口占满**：时钟是最底层窗口，只在能看见桌面时显示。

## 接口来源

- 一言（Hitokoto）：`https://v1.hitokoto.cn/?c=i`（`i` = 诗词分类）
- 今日诗词：`https://v2.jinrishici.com/one.json`
- 断网兜底：内置古诗词库 + 按时段问候。

## 从源码构建

```bash
dotnet publish src/DesktopClock/DesktopClock.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 目录结构

```
src/DesktopClock/
  Program.cs        入口 + 全局异常兜底
  ClockForm.cs      时钟窗口：逐像素透明 / 绘制 / 拖动 / 右键
  PoemService.cs    一言接口 + 本地兜底
  Autostart.cs      开机自启动（注册表）
  Native.cs         Win32 互操作
  Log.cs            日志
```
