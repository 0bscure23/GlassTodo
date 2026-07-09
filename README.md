# 琉璃清单 (GlassTodo)

Windows 桌面液态玻璃风格待办清单。原生 WPF (.NET 8) 单进程实现，逐像素透明**清玻璃**面板——背后窗口内容清晰可见——鼠标靠近屏幕右缘即滑出，移开自动收起。

![tech](https://img.shields.io/badge/.NET-8.0_WPF-5B9DFF) ![size](https://img.shields.io/badge/绿色版exe-~1MB-5BB98B) ![ram](https://img.shields.io/badge/常驻内存-~25MB-5BB98B) ![license](https://img.shields.io/badge/license-MIT-B98CFF)

## 功能

- **右缘呼出**：鼠标停靠屏幕右缘约 300ms（可调）滑出面板，不抢当前窗口焦点；移开 500ms 后自动收起；`Win+D` 显示桌面后依然可正常召唤
- **全局热键**：默认 `Alt+Q`（被占用时自动换备选并气泡提示），呼出并直接聚焦输入框；设置页可录制改键
- **任务管理**：回车快速添加、圆形勾选完成动画、双击行内编辑、悬停删除、拖拽排序（列表视图）
- **优先级与日期**：旗标三级优先级（左侧色条）；迷你月历弹层（快捷片 + 翻月任选日期）+ 任意提醒时间（支持 `8:30`、`2145` 简写输入）；过期红色高亮；跨午夜自动刷新
- **提醒通知**：到点弹出右下角玻璃 Toast（不抢焦点），支持「完成 / 稍后10分钟」；错过的提醒下次启动聚合补报；睡眠唤醒后重扫
- **多清单**：胶囊 chips 切换，「今天 / 全部」智能视图 + 自定义清单（右键重命名/换色/删除，任务并入默认清单）
- **液态玻璃外观**：逐像素透明清玻璃 + 鼠标光斑跟随 + 果冻弹性动画 + 呼出光带扫过 + 渐变棱边；**玻璃底浓度**与**任务卡浓度**双滑杆自由定制；也可切换「经典」近实色风格
- **系统集成**：托盘图标（显示/固定/自启/设置/退出）、开机自启、单实例（二次启动唤起面板）、数据文件夹可自选位置（自动迁移并重启）
- **主题**：深/浅色实时跟随系统（也可手动指定）
- **占用低**：单进程，常驻隐藏时工作集约 20~30MB，空闲 CPU ≈ 0%（10Hz 光标轮询）

## 下载

前往 [Releases](../../releases)：

| 文件 | 说明 |
|---|---|
| `GlassTodo-Setup-x.y.z.exe` | 安装包，自包含 .NET 运行时，开箱即用（推荐） |
| `GlassTodo-x.y.z-win-x64.zip` | 绿色版单 exe（约 1MB），需已安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

系统要求：Windows 11（Windows 10 可运行，圆角/投影观感略有差异）。

## 使用

- 鼠标移到**屏幕最右缘停留**呼出；输入中、弹层打开、拖拽中不会自动隐藏；`Esc` 收起
- 右上角图钉**固定面板**；齿轮进设置：外观风格与浓度、热键、呼出/隐藏延迟、面板高度、自启、数据位置
- 数据与设置：`%APPDATA%\GlassTodo\`（`data.json` / `settings.json`，原子写入 + `.bak` 备份），可在设置中迁移到任意文件夹
- 全屏游戏/演示模式下边缘触发与提醒自动抑制

## 从源码构建

```powershell
# 依赖：.NET 8 SDK
dotnet run --project src/GlassTodo        # 开发运行

# 绿色版单文件（约 1MB，需目标机有 .NET 8 桌面运行时）
dotnet publish src/GlassTodo/GlassTodo.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true

# 自包含版（无需运行时，供安装包使用）
dotnet publish src/GlassTodo/GlassTodo.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o src/GlassTodo/bin/publish-sc

# 安装包（需 Inno Setup 6）
iscc scripts/installer.iss                # 产物在 dist/
```

## 技术要点

- **清玻璃**：不走 DWM 模糊材质（Win11 系统模糊均为重度磨砂且强度不可调），用 WPF 分层窗口逐像素透明 + 自绘着色/镜面光/棱边/投影实现，背后内容 100% 清晰；圆角 18px 不受 DWM 限制，且不受系统「透明效果」开关影响
- **滑入滑出**：自定义 `SlideProgress` 依赖属性按**物理像素**每帧 `SetWindowPos`，跨 DPI 显示器不错位；液态模式带回弹缓动
- **呼出状态机**：Hidden/Showing/Shown/Hiding + 固定 + 交互锁（输入中/弹层开/拖拽中不自动隐藏）+ 收起后解除武装防弹回
- **存储**：System.Text.Json 源生成，防抖原子写 + `.bak` 三级加载链
- 仅两个依赖：CommunityToolkit.Mvvm、Hardcodet.NotifyIcon.Wpf

## 目录

```
src/GlassTodo/
├─ Interop/      # 全部 Win32 P/Invoke（DWM、光标、显示器、热键…）
├─ Services/     # 面板状态机、边缘触发、提醒、主题、存储、托盘周边
├─ ViewModels/   # MVVM 层（主视图/任务/清单/日期选择/设置）
├─ Views/        # 主玻璃卡片、设置页、提醒 Toast
├─ Behaviors/    # 拖拽排序、聚焦行为
└─ Themes/       # 深浅调色板 + 全套控件模板
scripts/         # 安装包脚本、图标生成与验证脚本
```

## License

[MIT](LICENSE)
