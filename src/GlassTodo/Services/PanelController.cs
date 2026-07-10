using System.Windows.Media.Animation;
using GlassTodo.Interop;
using GlassTodo.Models;
using GlassTodo.Views;

namespace GlassTodo.Services;

public enum PanelState
{
    Hidden,
    Showing,
    Shown,
    Hiding,
}

/// <summary>
/// Owns the panel show/hide state machine. All placement runs in physical pixels via
/// SetWindowPos so the card lands correctly on any monitor regardless of DPI.
/// </summary>
public sealed class PanelController
{
    // 分层窗口四周留 22 DIP 透明边距承载投影：窗口 = 卡片 380 + 44；
    // 负间距让透明边越过屏幕边缘，卡片可视间隙仍为 12 DIP
    private const double PanelWidthDip = 424;
    private const double DockGapDip = -10;

    private readonly MainWindow _win;
    private readonly Func<AppSettings> _settings;
    private readonly JsonStore<AppSettings> _settingsStore;
    private readonly HashSet<string> _locks = new();
    private int _animToken;

    public PanelState State { get; private set; } = PanelState.Hidden;
    public MonitorInfoPx Monitor { get; private set; }
    public RectPx DockRect { get; private set; }

    /// <summary>Raised when a hide animation fully completes — the edge trigger disarms on this.</summary>
    public event Action? HiddenCompleted;

    /// <summary>True while editing, a popup/menu is open, or the mouse is captured (drag in progress).</summary>
    public bool InteractionLocked => _locks.Count > 0 || System.Windows.Input.Mouse.Captured != null;

    /// <summary>分层窗口四周的透明投影边距（DIP），与 MainWindow 根 Grid 的 Margin 保持一致。</summary>
    public const double ShadowMarginDip = 22;

    /// <summary>玻璃卡片可见左缘（物理像素），供 Toast 等外部窗口避让。</summary>
    public int CardLeftPx => DockRect.Left + (int)Math.Round(ShadowMarginDip * Monitor.Scale);

    /// <summary>窗口被外部最小化（如 Win+D）时同步状态机，否则边缘触发永远打不开。</summary>
    public void NotifyExternalHide()
    {
        if (State == PanelState.Hidden) return;
        _animToken++; // 使未完成动画的回调失效
        State = PanelState.Hidden;
        _win.Hide();
        HideFrostWindows();
        HiddenCompleted?.Invoke();
    }

    public bool Pinned
    {
        get => _settings().Pinned;
        set
        {
            if (_settings().Pinned == value) return;
            _settings().Pinned = value;
            _settingsStore.RequestSave();
        }
    }

    public PanelController(MainWindow win, Func<AppSettings> settings, JsonStore<AppSettings> settingsStore)
    {
        _win = win;
        _settings = settings;
        _settingsStore = settingsStore;
        Monitor = ScreenInterop.GetRightmostMonitor();
        _win.SlideProgressChanged += ApplyProgress;
    }

    public void AddLock(string key) => _locks.Add(key);
    public void RemoveLock(string key) => _locks.Remove(key);

    /// <summary>Recomputes the docking rectangle on the rightmost monitor.</summary>
    public void ReDock()
    {
        Monitor = ScreenInterop.GetRightmostMonitor();
        var wa = Monitor.Work;
        double sc = Monitor.Scale;
        int w = (int)Math.Round(PanelWidthDip * sc);
        int h = (int)Math.Round(wa.Height * Math.Clamp(_settings().PanelHeightRatio, 0.40, 0.95));
        int gap = (int)Math.Round(DockGapDip * sc);
        int left = wa.Right - gap - w;
        int top = wa.Top + (wa.Height - h) / 2;
        DockRect = new RectPx(left, top, w, h);
        ApplyProgress(State is PanelState.Shown ? 1.0 : _win.SlideProgress);
    }

    /// <summary>磨砂是否生效（液态风格 + 系统透明效果开启），由主题服务驱动。</summary>
    public bool FrostEnabled { get; set; }

    /// <summary>磨砂着色提供者（AABBGGRR），由 App 注入主题服务的当前值。</summary>
    public Func<uint>? FrostTintProvider { get; set; }

    // 每个任务卡对应一个小磨砂窗（系统模糊按整窗矩形合成、不吃区域裁剪，
    // 所以"只在卡片上起雾"必须用窗口池逐卡贴合）
    private readonly List<Views.BackdropWindow> _frostPool = new();
    private System.Windows.Rect[] _frostRects = Array.Empty<System.Windows.Rect>();
    private int _lastMainX;

    private void ApplyProgress(double p)
    {
        if (_win.Hwnd == IntPtr.Zero) return;
        int hiddenLeft = Monitor.Bounds.Right;
        int x = (int)Math.Round(hiddenLeft + (DockRect.Left - hiddenLeft) * p);
        NativeMethods.SetWindowPos(_win.Hwnd, IntPtr.Zero, x, DockRect.Top, DockRect.Width, DockRect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        _lastMainX = x;
        PositionFrostWindows();
    }

    /// <summary>主窗口布局变化时推送最新的任务卡矩形（窗口 DIP 坐标）。</summary>
    public void UpdateFrostRegion(System.Windows.Rect[] rects)
    {
        _frostRects = rects;
        EnsureFrostWindows();
    }

    private void EnsureFrostWindows()
    {
        if (!FrostEnabled || State is PanelState.Hidden or PanelState.Hiding)
        {
            HideFrostWindows();
            return;
        }
        while (_frostPool.Count < _frostRects.Length)
        {
            var w = new Views.BackdropWindow();
            w.EnsureHandle();
            WindowEffects.ApplyFrost(w.Hwnd, FrostTintProvider?.Invoke() ?? 0x30FFFFFFu);
            _frostPool.Add(w);
        }
        for (int i = 0; i < _frostPool.Count; i++)
        {
            if (i < _frostRects.Length)
            {
                if (!_frostPool[i].IsVisible) _frostPool[i].Show();
            }
            else if (_frostPool[i].IsVisible)
            {
                _frostPool[i].Hide();
            }
        }
        PositionFrostWindows();
    }

    /// <summary>把池中的磨砂窗逐卡贴到屏幕坐标（跟随滑动与布局变化）。</summary>
    private void PositionFrostWindows()
    {
        if (!FrostEnabled) return;
        double sc = Monitor.Scale;
        int n = Math.Min(_frostPool.Count, _frostRects.Length);
        for (int i = 0; i < n; i++)
        {
            var w = _frostPool[i];
            if (w.Hwnd == IntPtr.Zero || !w.IsVisible) continue;
            var r = _frostRects[i];
            NativeMethods.SetWindowPos(w.Hwnd, _win.Hwnd,
                _lastMainX + (int)Math.Round(r.X * sc),
                DockRect.Top + (int)Math.Round(r.Y * sc),
                (int)Math.Round(r.Width * sc),
                (int)Math.Round(r.Height * sc),
                NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void HideFrostWindows()
    {
        foreach (var w in _frostPool)
            if (w.IsVisible) w.Hide();
    }

    /// <summary>主题/浓度变化后重刷所有磨砂窗的着色与可见性。</summary>
    public void RefreshFrost()
    {
        if (FrostEnabled)
        {
            uint tint = FrostTintProvider?.Invoke() ?? 0x30FFFFFFu;
            foreach (var w in _frostPool)
                WindowEffects.ApplyFrost(w.Hwnd, tint);
            EnsureFrostWindows();
        }
        else
        {
            HideFrostWindows();
        }
    }

    public bool IsCursorInsidePanel(int x, int y)
    {
        int expand = (int)Math.Round(8 * Monitor.Scale);
        // The gap strip between the card and the screen edge counts as "inside",
        // otherwise parking the mouse at the edge would immediately retract the panel.
        return x >= DockRect.Left - expand && x <= Monitor.Bounds.Right
            && y >= DockRect.Top - expand && y <= DockRect.Bottom + expand;
    }

    public bool IsCursorInTriggerZone(int x, int y)
    {
        var s = _settings();
        int band = (int)Math.Round(60 * Monitor.Scale);
        return x >= Monitor.Bounds.Right - Math.Max(1, s.EdgeZonePx)
            && y >= DockRect.Top - band
            && y <= DockRect.Bottom + band;
    }

    public void ShowPanel(bool activate)
    {
        if (State == PanelState.Shown)
        {
            if (activate) ActivateAndFocus();
            return;
        }

        ReDock();
        if (State == PanelState.Hidden)
        {
            // Win+D「显示桌面」会最小化工具窗口，Show() 恢复不了，必须显式还原
            if (_win.WindowState != System.Windows.WindowState.Normal)
                _win.WindowState = System.Windows.WindowState.Normal;
            ApplyProgress(0);
            _win.Show(); // ShowActivated=false → never steals focus
            if (FrostEnabled) EnsureFrostWindows();
        }

        State = PanelState.Showing;
        bool liquid = _settings().VisualStyle == 1;
        IEasingFunction showEase = liquid
            ? new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut } // 液态：轻微回弹落位
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        Animate(1.0, liquid ? 320 : 240, showEase, () => State = PanelState.Shown);
        if (liquid) _win.OnLiquidShow();
        if (activate) ActivateAndFocus();
    }

    public void HidePanel()
    {
        if (State is PanelState.Hidden or PanelState.Hiding) return;
        State = PanelState.Hiding;
        Animate(0.0, 180, new QuadraticEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            State = PanelState.Hidden;
            _win.Hide();
            HideFrostWindows();
            HiddenCompleted?.Invoke();
        });
    }

    public void ToggleFromHotkey()
    {
        if (State is PanelState.Shown or PanelState.Showing)
        {
            if (_win.IsActive) HidePanel();
            else ActivateAndFocus();
        }
        else
        {
            ShowPanel(activate: true);
        }
    }

    private void ActivateAndFocus()
    {
        _win.ForceForeground();
        _win.FocusQuickAdd();
    }

    private void Animate(double to, int ms, IEasingFunction ease, Action completed)
    {
        int token = ++_animToken;
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        anim.Completed += (_, _) =>
        {
            if (token == _animToken) completed();
        };
        _win.BeginAnimation(MainWindow.SlideProgressProperty, anim);
    }
}
