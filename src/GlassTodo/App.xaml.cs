using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using GlassTodo.Models;
using GlassTodo.Serialization;
using GlassTodo.Services;
using GlassTodo.ViewModels;
using GlassTodo.Views;

namespace GlassTodo;

public partial class App : Application
{
    private JsonStore<AppData>? _dataStore;
    private JsonStore<AppSettings>? _settingsStore;
    private MainViewModel? _vm;
    private MainWindow? _mainWindow;
    private BackdropWindow? _backdropWin;
    private PanelController? _panel;
    private EdgeTriggerService? _edge;
    private ThemeService? _theme;
    private HotkeyService? _hotkey;
    private SingleInstanceService? _single;
    private ReminderService? _reminders;
    private TaskbarIcon? _tray;
    private MenuItem? _trayPinItem;
    private MenuItem? _trayAutoStartItem;
    private DispatcherTimer? _settingChangeDebounce;

    private static string? _dataDir;

    public static string DefaultDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassTodo");

    /// <summary>数据目录：默认 %APPDATA%\GlassTodo，可被其中的 datapath.txt 指针重定向。</summary>
    public static string DataDir => _dataDir ??= ResolveDataDir();

    private static string ResolveDataDir()
    {
        try
        {
            string pointer = Path.Combine(DefaultDataDir, "datapath.txt");
            if (File.Exists(pointer))
            {
                string dir = File.ReadAllText(pointer).Trim();
                if (dir.Length > 0 && Directory.Exists(dir)) return dir;
            }
        }
        catch
        {
            // 指针不可读则退回默认位置
        }
        return DefaultDataDir;
    }

    /// <summary>迁移数据文件到新目录、写入指针文件并重启应用（设置页调用）。</summary>
    public void ChangeDataDirAndRestart(string newDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newDir)) return;
            newDir = Path.GetFullPath(newDir);
            if (string.Equals(newDir, DataDir, StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(newDir);

            FlushAll();
            // 目标已有数据则直接采用（不覆盖），否则把当前数据带过去
            foreach (string name in new[] { "data.json", "data.json.bak", "settings.json", "settings.json.bak" })
            {
                string src = Path.Combine(DataDir, name);
                string dst = Path.Combine(newDir, name);
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
            }

            Directory.CreateDirectory(DefaultDataDir);
            File.WriteAllText(Path.Combine(DefaultDataDir, "datapath.txt"),
                string.Equals(newDir, DefaultDataDir, StringComparison.OrdinalIgnoreCase) ? "" : newDir);

            // 先释放单实例互斥体再拉起新进程，否则新进程会被当作二次启动直接退出
            _hotkey?.Dispose();
            _tray?.Dispose();
            _tray = null;
            _single?.Dispose();
            _single = null;
            if (Environment.ProcessPath is { } exe)
                System.Diagnostics.Process.Start(exe);
            Shutdown();
        }
        catch
        {
            _tray?.ShowBalloonTip("琉璃清单", "更改数据文件夹失败，请确认目标位置可写。", BalloonIcon.Warning);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = new SingleInstanceService();
        if (!_single.TryAcquire())
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _tray?.Dispose();

        _settingsStore = new JsonStore<AppSettings>(Path.Combine(DataDir, "settings.json"), AppJsonContext.Default.AppSettings);
        _dataStore = new JsonStore<AppData>(Path.Combine(DataDir, "data.json"), AppJsonContext.Default.AppData);
        bool firstRun = !_dataStore.LoadedFromDisk;

        _vm = new MainViewModel(_dataStore, _settingsStore, firstRun);
        _theme = new ThemeService(() => _settingsStore.Data);
        _mainWindow = new MainWindow(_vm);
        _panel = new PanelController(_mainWindow, () => _settingsStore.Data, _settingsStore);
        _mainWindow.Controller = _panel;
        _edge = new EdgeTriggerService(_panel, () => _settingsStore.Data);

        _mainWindow.EnsureHandle();

        // 磨砂背景板：液态模式下贴在卡片后方提供真实背景模糊
        _backdropWin = new BackdropWindow();
        _backdropWin.EnsureHandle();
        _theme.RegisterFrostBackdrop(_backdropWin.Hwnd);
        _panel.Backdrop = _backdropWin;
        _theme.ThemeChanged += () =>
        {
            _panel.FrostEnabled = _theme.FrostActive;
            _panel.RefreshFrost();
        };

        _theme.Register(_mainWindow);
        _theme.Refresh();
        _panel.ReDock();

        _single.SummonRequested += () => Dispatcher.BeginInvoke(() => _panel?.ShowPanel(activate: true));

        _settingChangeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _settingChangeDebounce.Tick += (_, _) =>
        {
            _settingChangeDebounce.Stop();
            _theme.Refresh();
        };
        _mainWindow.SystemSettingChanged += () =>
        {
            _settingChangeDebounce.Stop();
            _settingChangeDebounce.Start();
        };
        _mainWindow.DisplayChanged += () => _panel.ReDock();

        _hotkey = new HotkeyService(_mainWindow);
        _hotkey.Pressed += () => _panel.ToggleFromHotkey();
        bool hotkeyOk = _hotkey.Register(_settingsStore.Data.HotKey);
        string? hotkeyFallback = null;
        if (!hotkeyOk && _settingsStore.Data.HotKey == "Alt+Q")
        {
            // the factory default is taken on this machine — pick a working alternative
            foreach (string alt in new[] { "Ctrl+Alt+Q", "Ctrl+Alt+Space", "Ctrl+Shift+Space" })
            {
                if (!_hotkey.Register(alt)) continue;
                hotkeyOk = true;
                hotkeyFallback = alt;
                _settingsStore.Data.HotKey = alt;
                _settingsStore.RequestSave();
                break;
            }
        }

        _vm.SettingsVM = new SettingsViewModel(_settingsStore,
            applyPanelLayout: () => _panel.ReDock(),
            applyHotkey: chord => _hotkey.Register(chord),
            applyTheme: () => _theme.Refresh(),
            applyStyle: ApplyVisualStyle);
        ApplyVisualStyle();

        InitTray();
        if (!hotkeyOk)
            _tray?.ShowBalloonTip("琉璃清单", $"全局热键注册失败：{_hotkey.LastError}，可在设置中更换。", BalloonIcon.Warning);
        else if (hotkeyFallback != null)
            _tray?.ShowBalloonTip("琉璃清单", $"默认热键 Alt+Q 已被占用，已自动改用 {hotkeyFallback}。", BalloonIcon.Info);

        _reminders = new ReminderService(_vm, _panel, _theme,
            data: () => _dataStore.Data,
            save: () => _dataStore.RequestSave());
        _vm.ScheduleChanged += () => _reminders.Rescan();
        _reminders.Start();

        // 睡眠唤醒后核对是否跨天（午夜定时器可能漂移）
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, args) =>
        {
            if (args.Mode == Microsoft.Win32.PowerModes.Resume)
                Dispatcher.BeginInvoke(() => _vm?.CheckDayRollover());
        };

        // keep the resident footprint low: once the panel is fully hidden, compact and trim
        _panel.HiddenCompleted += () => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, TrimWorkingSet);

        _edge.Start();

        if (firstRun) _panel.ShowPanel(activate: true);
        else Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, TrimWorkingSet); // silent tray start
    }

    private static void TrimWorkingSet()
    {
        GC.Collect(2, GCCollectionMode.Optimized);
        Interop.NativeMethods.SetProcessWorkingSetSize(new IntPtr(-1), new IntPtr(-1), new IntPtr(-1));
    }

    private void ApplyVisualStyle()
    {
        if (_settingsStore is null || _vm is null || _mainWindow is null) return;
        bool liquid = _settingsStore.Data.VisualStyle == 1;
        _vm.IsLiquidStyle = liquid;
        _mainWindow.UpdateLiquidMode(liquid);
        _theme?.Refresh(); // backdrop type follows the visual style
    }

    // ------------------------------------------------------------------
    //  tray icon
    // ------------------------------------------------------------------

    private void InitTray()
    {
        if (_panel is null || _vm is null) return;

        var menu = new ContextMenu();

        var show = new MenuItem { Header = "显示面板" };
        show.Click += (_, _) => _panel.ShowPanel(activate: true);
        menu.Items.Add(show);

        _trayPinItem = new MenuItem { Header = "固定面板", IsCheckable = true, IsChecked = _vm.IsPinned };
        _trayPinItem.Click += (_, _) => _vm.IsPinned = _trayPinItem.IsChecked;
        menu.Items.Add(_trayPinItem);

        _trayAutoStartItem = new MenuItem { Header = "开机自启", IsCheckable = true, IsChecked = AutoStartService.IsEnabled() };
        _trayAutoStartItem.Click += (_, _) =>
        {
            AutoStartService.SetEnabled(_trayAutoStartItem.IsChecked);
            if (_settingsStore != null)
            {
                _settingsStore.Data.AutoStart = _trayAutoStartItem.IsChecked;
                _settingsStore.RequestSave();
            }
        };
        menu.Items.Add(_trayAutoStartItem);

        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) =>
        {
            _panel.ShowPanel(activate: true);
            _vm.IsSettingsOpen = true;
        };
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        menu.Opened += (_, _) =>
        {
            if (_trayPinItem != null) _trayPinItem.IsChecked = _vm.IsPinned;
            if (_trayAutoStartItem != null) _trayAutoStartItem.IsChecked = AutoStartService.IsEnabled();
        };

        _tray = new TaskbarIcon
        {
            ToolTipText = "琉璃清单 — 鼠标靠右缘或 Alt+Q 呼出",
            IconSource = CreateTrayIconImage(),
            ContextMenu = menu,
        };
        _tray.TrayLeftMouseUp += (_, _) => _panel.ShowPanel(activate: true);

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsPinned) && _trayPinItem != null)
                _trayPinItem.IsChecked = _vm.IsPinned;
        };
    }

    private static ImageSource CreateTrayIconImage()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var fill = new SolidColorBrush(Color.FromRgb(0x5B, 0x9D, 0xFF));
            dc.DrawRoundedRectangle(fill, null, new Rect(2, 2, 28, 28), 8, 8);
            var pen = new Pen(Brushes.White, 3.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(9, 16.5), false, false);
                ctx.LineTo(new Point(14, 21.5), true, true);
                ctx.LineTo(new Point(23.5, 10.5), true, true);
            }
            dc.DrawGeometry(null, pen, geo);
        }
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    // ------------------------------------------------------------------
    //  lifecycle
    // ------------------------------------------------------------------

    protected override void OnExit(ExitEventArgs e)
    {
        FlushAll();
        _reminders?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        FlushAll();
        base.OnSessionEnding(e);
    }

    private void FlushAll()
    {
        _dataStore?.Flush();
        _settingsStore?.Flush();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch
        {
            // logging must never crash the crash handler
        }
        FlushAll();
        e.Handled = true;
    }
}
