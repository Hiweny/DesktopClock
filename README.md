# DesktopClock · 桌面古诗词时钟

一个贴在**桌面最底层**的极简透明时钟（Windows）。白色衬线字体，纯文字、无背板，可拖动，不遮挡其它窗口。

> 时间 → 日期 → 古诗词一句 → 出处/作者，居中排布，直接"浮现"在壁纸之上。默认位于屏幕**中间偏上**。

## 下载

三个产物任选其一：

- `DesktopClock-exe.zip` —— **推荐**。里面就是单文件 `DesktopClock.exe`，解压即用（自包含，不用装 .NET）。
  用 zip 套一层是为了绕开浏览器 / 杀毒软件对 `.exe` 直接下载的拦截。
- `DesktopClock-folder.zip` —— 免自解压版（文件夹形式）。**如果单文件版双击没反应（被杀软拦），用这个**。
- `DesktopClock.exe` —— 未打包的单文件版；直接下载可能被浏览器或杀软拦截。

## 特性

- **真透明**：逐像素 Alpha 分层窗口，只有文字，没有半透明色块。
- **贴桌面 / 不挡窗口**：无边框、无任务栏图标、不抢焦点，始终位于所有窗口的**最底层**。
- **默认位置中间偏上**，可自由拖动；右键菜单有"回到默认位置"。
- **系统托盘常驻**：托盘图标可随时**显示/隐藏**时钟、换诗、退出。
- **诗词轮换 + 淡入淡出**：默认每 1 分钟换一句，带淡入淡出；间隔可选 30 秒 / 1 分钟 / 5 分钟 / 10 分钟。
- **衬线字体**：时间用 Georgia / Times New Roman，中文自动挑选本机衬线族。
- **古诗词一言**：联网时获取诗句（一言 Hitokoto·诗词 → 今日诗词）；**断网时**切换为本地"按时段问候 + 内置古诗词"。
- **开机自启动**：见下。
- **自适应 DPI**。

## 开机自启动

- 首次运行会自动登记到注册表
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`（无需管理员权限）。
- **每次启动都会把路径校正为当前 exe 的真实位置** —— 换过下载位置、移动过文件、用解压版都不会失效。
- 菜单（时钟上右键 / 托盘上右键）里的这一项会**直接写明状态**：
  - `开机自启动：已开启（点击关闭）` → 点一下即关闭
  - `开机自启动：已关闭（点击开启）` → 点一下即开启
- 如果重启后仍没自启：打开「任务管理器 → 启动应用」，看 `DesktopClock` 是否被禁用了。

## 使用

1. 解压后运行 `DesktopClock.exe`（若弹"未知发布者"，点「更多信息 → 仍要运行」）。
2. 按住文字可拖动；时钟上右键 = 全部选项。
3. **系统托盘**图标左键单击 = 显示/隐藏时钟，右键 = 菜单。
4. 透明区域鼠标自动穿透，不影响点击桌面图标。
5. 要看到时钟需**先显示桌面**（Win+D 或最小化其它窗口）。

## 看不到时钟？请这样做

日志位置：

```
%LOCALAPPDATA%\DesktopClock\DesktopClock.log
```

把这份日志发来就能精确定位。常见情况：

- **杀毒软件拦截**：单文件版首次运行会自解压，360 / 火绒等可能拦截。改用 `DesktopClock-folder.zip`，或加入白名单。
- **有窗口盖住桌面**：时钟是最底层窗口，请先 Win+D 或最小化其它窗口。
- **被自己隐藏了**：点托盘图标左键即可恢复显示。

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
  ClockForm.cs      时钟窗口：逐像素透明 / 绘制 / 拖动 / 托盘 / 轮换动画
  PoemService.cs    一言接口 + 本地兜底
  Autostart.cs      开机自启动（注册表，路径自动校正）
  Native.cs         Win32 互操作
  Log.cs            日志
  app.ico           应用 / 托盘图标
```
