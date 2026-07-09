using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GlassTodo.Behaviors;
using GlassTodo.Interop;
using GlassTodo.Services;
using GlassTodo.ViewModels;

namespace GlassTodo.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public static readonly DependencyProperty SlideProgressProperty = DependencyProperty.Register(
        nameof(SlideProgress), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, e) => ((MainWindow)d).SlideProgressChanged?.Invoke((double)e.NewValue)));

    public double SlideProgress
    {
        get => (double)GetValue(SlideProgressProperty);
        set => SetValue(SlideProgressProperty, value);
    }

    public event Action<double>? SlideProgressChanged;
    public event Action? SystemSettingChanged;
    public event Action? DisplayChanged;
    public event Action<int>? HotkeyPressed;

    public IntPtr Hwnd { get; private set; }
    public PanelController? Controller { get; set; }

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        GotKeyboardFocus += (_, _) => RecomputeEditLock();
        LostKeyboardFocus += (_, _) => RecomputeEditLock();
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => RecomputeEditLock()), true);

        // Win+D「显示桌面」会把本工具窗最小化：立即还原窗口并把状态机置为 Hidden，
        // 否则之后鼠标靠边永远召唤不出面板
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) return;
            WindowState = WindowState.Normal;
            Controller?.NotifyExternalHide();
        };

        // when activation lands slightly after the summon, re-apply the pending quick-add focus
        Activated += (_, _) =>
        {
            if (!_focusQuickAddOnActivate) return;
            _focusQuickAddOnActivate = false;
            ApplyQuickAddFocus();
        };

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.IsSettingsOpen)) return;
            if (_vm.IsSettingsOpen) Controller?.AddLock("settings");
            else Controller?.RemoveLock("settings");
        };
    }

    /// <summary>Creates the HWND early (without showing) so DWM effects and positioning can be applied.</summary>
    public void EnsureHandle()
    {
        Hwnd = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(Hwnd)?.AddHook(WndProc);
        WindowEffects.AddExStyle(Hwnd, NativeMethods.WS_EX_TOOLWINDOW);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_HOTKEY:
                HotkeyPressed?.Invoke(wParam.ToInt32());
                handled = true;
                break;
            case NativeMethods.WM_SETTINGCHANGE:
                SystemSettingChanged?.Invoke();
                break;
            case NativeMethods.WM_DISPLAYCHANGE:
            case NativeMethods.WM_DPICHANGED:
                DisplayChanged?.Invoke();
                break;
        }
        return IntPtr.Zero;
    }

    private bool _focusQuickAddOnActivate;

    public void FocusQuickAdd()
    {
        _focusQuickAddOnActivate = !IsActive;
        ApplyQuickAddFocus();
    }

    private void ApplyQuickAddFocus()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            QuickAddBox.Focus();
            Keyboard.Focus(QuickAddBox);
            QuickAddBox.CaretIndex = QuickAddBox.Text.Length;
        });
    }

    /// <summary>
    /// Robust foreground grab. A plain SetForegroundWindow from a background process is
    /// refused by the foreground lock; attaching to the current foreground thread's input
    /// queue makes the switch stick (the standard launcher-style workaround).
    /// </summary>
    public void ForceForeground()
    {
        if (Hwnd == IntPtr.Zero) return;
        try { Activate(); } catch { /* window may be mid-show */ }
        if (NativeMethods.GetForegroundWindow() == Hwnd) return;

        IntPtr fg = NativeMethods.GetForegroundWindow();
        uint cur = NativeMethods.GetCurrentThreadId();
        uint fgThread = fg != IntPtr.Zero ? NativeMethods.GetWindowThreadProcessId(fg, out _) : 0;
        bool attached = false;
        try
        {
            if (fgThread != 0 && fgThread != cur)
                attached = NativeMethods.AttachThreadInput(cur, fgThread, true);
            NativeMethods.BringWindowToTop(Hwnd);
            NativeMethods.SetForegroundWindow(Hwnd);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(cur, fgThread, false);
        }
    }

    // ------------------------------------------------------------------
    //  interaction lock: keep the panel open while the user is typing
    // ------------------------------------------------------------------

    private void RecomputeEditLock()
    {
        bool locked = Keyboard.FocusedElement is TextBox tb
                      && ReferenceEquals(Window.GetWindow(tb), this)
                      && (tb.Text.Length > 0 || ViewBehaviors.GetIsEditBox(tb));
        if (locked) Controller?.AddLock("edit");
        else Controller?.RemoveLock("edit");
    }

    // ------------------------------------------------------------------
    //  keyboard
    // ------------------------------------------------------------------

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_vm.DuePicker.IsOpen)
        {
            _vm.DuePicker.Close();
            e.Handled = true;
            return;
        }
        if (_vm.IsSettingsOpen)
        {
            _vm.IsSettingsOpen = false;
            e.Handled = true;
            return;
        }
        Controller?.HidePanel();
        e.Handled = true;
    }

    private void QuickAdd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.AddTaskCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && QuickAddBox.Text.Length > 0)
        {
            _vm.QuickAddText = "";
            e.Handled = true;
        }
    }

    private void RowEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskItemViewModel item }) return;
        if (e.Key == Key.Enter)
        {
            item.CommitEditCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void RowEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItemViewModel item })
            item.CommitEditCommand.Execute(null);
    }

    private void Row_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: TaskItemViewModel item } && !item.IsDone)
        {
            item.BeginEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------
    //  chips
    // ------------------------------------------------------------------

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChipViewModel chip })
            _vm.SelectChipCommand.Execute(chip);
    }

    private void ChipEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChipViewModel chip }) return;
        if (e.Key == Key.Enter)
        {
            CommitChipEdit(chip);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.CancelChipEditCommand.Execute(chip);
            e.Handled = true;
        }
    }

    private void ChipEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChipViewModel chip } && chip.IsEditing)
            CommitChipEdit(chip);
    }

    private void CommitChipEdit(ChipViewModel chip)
    {
        if (chip.IsAddChip) _vm.CommitAddListCommand.Execute(chip);
        else _vm.CommitRenameListCommand.Execute(chip);
    }

    private void Chip_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChipViewModel chip } el) return;
        if (!chip.IsUserList) return;
        e.Handled = true;

        var menu = new ContextMenu { PlacementTarget = el, Placement = PlacementMode.Bottom };

        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) => _vm.BeginRenameListCommand.Execute(chip);
        menu.Items.Add(rename);

        // color swatches
        var swatchPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        string[] colors = { "#5B9DFF", "#5BB98B", "#FFC53D", "#FF6B6B", "#B98CFF", "#56C2E6", "#FF9F5B", "#F272B6" };
        foreach (string hex in colors)
        {
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            string captured = hex;
            dot.MouseLeftButtonUp += (_, args) =>
            {
                _vm.SetListColor(chip, captured);
                menu.IsOpen = false;
                args.Handled = true;
            };
            swatchPanel.Children.Add(dot);
        }
        var colorItem = new MenuItem { Header = swatchPanel, StaysOpenOnClick = true };
        menu.Items.Add(colorItem);

        if (!chip.IsDefaultList)
        {
            menu.Items.Add(new Separator());
            var delete = new MenuItem { Header = "删除清单（任务并入默认清单）" };
            delete.Click += (_, _) => _vm.DeleteListCommand.Execute(chip);
            menu.Items.Add(delete);
        }

        menu.Opened += (_, _) => Controller?.AddLock("menu");
        menu.Closed += (_, _) => Controller?.RemoveLock("menu");
        menu.IsOpen = true;
    }

    private void ChipsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ChipsScroll.ScrollToHorizontalOffset(ChipsScroll.HorizontalOffset - e.Delta * 0.4);
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    //  due picker popup
    // ------------------------------------------------------------------

    private void RowDue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItemViewModel item } el)
            OpenDuePicker(el, item);
    }

    private void ComposeDue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el)
            OpenDuePicker(el, _vm.ComposeTarget);
    }

    private void OpenDuePicker(FrameworkElement target, IDueTarget dueTarget)
    {
        DuePopup.PlacementTarget = target;
        _vm.DuePicker.Open(dueTarget);
    }

    private void DuePopup_Opened(object sender, EventArgs e)
    {
        Controller?.AddLock("popup");
        if (_liquid)
        {
            var spring = new System.Windows.Media.Animation.BackEase
            {
                Amplitude = 0.55,
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            };
            DuePopupScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = spring });
            DuePopupScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = spring });
        }
    }

    private void DuePopup_Closed(object sender, EventArgs e)
    {
        Controller?.RemoveLock("popup");
        if (_vm.DuePicker.IsOpen) _vm.DuePicker.Close();
    }

    private void TimeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm.DuePicker.CommitTimeText();
        e.Handled = true;
    }

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e) => _vm.DuePicker.CommitTimeText();

    // ------------------------------------------------------------------
    //  liquid glass mode: pointer-tracked specular light, rim, sheen sweep
    // ------------------------------------------------------------------

    private bool _liquid;

    public void UpdateLiquidMode(bool liquid)
    {
        _liquid = liquid;
        var vis = liquid ? Visibility.Visible : Visibility.Collapsed;
        LiquidLight.Visibility = vis;
        LiquidGlint.Visibility = vis;
        LiquidRim.Visibility = vis;
        if (!liquid) SheenSweep.Opacity = 0;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_liquid) return;
        var p = e.GetPosition(this);
        LiquidLightBrush.Center = LiquidLightBrush.GradientOrigin = p;
        var glint = new Point(p.X - 9, p.Y - 12);
        LiquidGlintBrush.Center = LiquidGlintBrush.GradientOrigin = glint;
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_liquid) return;
        FadeTo(LiquidLight, 1, 200);
        FadeTo(LiquidGlint, 1, 200);
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        FadeTo(LiquidLight, 0, 350);
        FadeTo(LiquidGlint, 0, 350);
    }

    private static void FadeTo(UIElement el, double opacity, int ms) =>
        el.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms)));

    /// <summary>Plays the light band sweeping across the glass as the panel slides in.</summary>
    public void OnLiquidShow()
    {
        if (!_liquid) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            double travel = Math.Max(ActualWidth, 380) + 160;
            var opacity = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
            opacity.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.Zero));
            opacity.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.6, TimeSpan.FromMilliseconds(140)));
            opacity.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.45, TimeSpan.FromMilliseconds(420)));
            opacity.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(680)));
            SheenSweep.BeginAnimation(OpacityProperty, opacity);

            var sweep = new System.Windows.Media.Animation.DoubleAnimation(-160, travel, TimeSpan.FromMilliseconds(680))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            };
            SheenT.BeginAnimation(TranslateTransform.XProperty, sweep);
        });
    }

    // ------------------------------------------------------------------
    //  settings (M6 完善)
    // ------------------------------------------------------------------

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsSettingsOpen = !_vm.IsSettingsOpen;
    }
}
