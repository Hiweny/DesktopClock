# DesktopClock · 桌面古诗词时钟

一个贴在**桌面壁纸层**上的极简透明时钟小组件（Windows）。白色衬线字体，纯文字、无背板，可随意拖动，不遮挡任何窗口。

> 效果：时间 → 日期 → 古诗词一句 → 出处/作者，全部居中排布，直接"浮现"在壁纸之上。

## 特性

- **真透明**：逐像素 Alpha 分层窗口，只有文字，没有半透明色块。
- **贴壁纸层**：窗口寄宿在桌面 `WorkerW` 上——
  - 位于壁纸之上、所有普通窗口之下，不会影响其它程序；
  - 隐藏桌面图标、按下 `Win + D`（显示桌面）时依然可见；
  - 从不抢焦点、不出现在任务栏、不置顶。
- **可拖动**：按住文字区域即可拖动到任意位置。
- **衬线字体**：时间用 Georgia / Times New Roman，中文用宋体 / 思源宋体等衬线族（自动探测本机已安装字体）。
- **古诗词一言**：联网时自动获取诗句（优先「一言 Hitokoto·诗词」，其次「今日诗词」）；
  **断网时**自动切换为本地「随时间变化的问候语 + 内置古诗词」，例如"早上好 / 下午好 / 晚上好"。
- **开机自启动**：首次运行自动登记（写入 `HKCU\...\Run`，无需管理员权限），可在右键菜单中开关。
- **自适应 DPI**：高分屏下自动缩放。

## 使用

1. 下载 `DesktopClock.exe`，双击运行即可（`self-contained` 单文件，无需安装 .NET 运行时）。
2. 拖到你喜欢的位置。
3. 右键菜单：
   - **换一句诗词**：立即刷新一条诗句；
   - **开机自启动**：勾选/取消；
   - **退出**：关闭组件。

> 如果 Windows SmartScreen 提示"未知发布者"，点击"更多信息 → 仍要运行"即可（程序未做代码签名）。

## 接口来源

- 一言（Hitokoto）：`https://v1.hitokoto.cn/?c=i` （`i` = 诗词分类）
- 今日诗词：`https://v2.jinrishici.com/one.json`
- 断网兜底：内置古诗词库 + 按时段问候。

## 从源码构建

需要 .NET 8 SDK：

```bash
dotnet publish src/DesktopClock/DesktopClock.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

产物 `out/DesktopClock.exe` 为单文件绿色版。

## 已知说明

- 若「Windows 资源管理器（explorer.exe）」被重启，桌面会被重建，组件可能随之消失，重新运行一次即可。
- 组件只在主显示器居中显示；拖动位置仅在本次运行内有效，重启后回到屏幕居中。

## 目录结构

```
src/DesktopClock/
  Program.cs        入口，初始化 DPI 感知
  ClockForm.cs      时钟窗口：桌面层嵌入 / 逐像素透明 / 绘制 / 拖动
  PoemService.cs    一言接口 + 本地兜底
  Autostart.cs      开机自启动（注册表）
  Native.cs         Win32 互操作
```
