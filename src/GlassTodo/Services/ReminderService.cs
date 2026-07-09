using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using GlassTodo.Interop;
using GlassTodo.Models;
using GlassTodo.ViewModels;
using GlassTodo.Views;

namespace GlassTodo.Services;

/// <summary>
/// Scans for due reminders every 30s (plus immediate rescans on schedule changes and
/// resume-from-sleep), shows glass toasts one at a time, and aggregates missed reminders.
/// </summary>
public sealed class ReminderService : IDisposable
{
    private sealed record ToastRequest(TodoTask? Task, int AggregateCount, bool Missed, string? AggregatePreview);

    private readonly MainViewModel _vm;
    private readonly PanelController _panel;
    private readonly ThemeService _theme;
    private readonly Func<AppData> _data;
    private readonly Action _save;
    private readonly DispatcherTimer _timer;
    private readonly Queue<ToastRequest> _queue = new();
    private ToastWindow? _current;

    public ReminderService(MainViewModel vm, PanelController panel, ThemeService theme,
        Func<AppData> data, Action save)
    {
        _vm = vm;
        _panel = panel;
        _theme = theme;
        _data = data;
        _save = save;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _timer.Tick += (_, _) => Scan(missed: false);
    }

    public void Start()
    {
        ScanMissedOnStartup();
        _timer.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Rescan() => Scan(missed: false);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Application.Current?.Dispatcher.BeginInvoke(() => Scan(missed: false));
    }

    private List<TodoTask> DueTasks() =>
        _data().Tasks
            .Where(t => !t.IsDone && t.RemindAt != null && t.RemindAt <= DateTime.Now && t.ReminderFiredAt == null)
            .OrderBy(t => t.RemindAt)
            .ToList();

    private void ScanMissedOnStartup()
    {
        var due = DueTasks();
        if (due.Count == 0) return;
        var now = DateTime.Now;
        foreach (var t in due) t.ReminderFiredAt = now;
        _save();

        if (due.Count == 1)
            _queue.Enqueue(new ToastRequest(due[0], 0, Missed: true, null));
        else
            _queue.Enqueue(new ToastRequest(null, due.Count, Missed: true, PreviewOf(due)));
        ShowNext();
    }

    private void Scan(bool missed)
    {
        // don't pop toasts over fullscreen games/presentations; retry next tick
        if (ScreenInterop.IsFullscreenAppActive()) return;

        var due = DueTasks();
        if (due.Count == 0) return;
        var now = DateTime.Now;
        foreach (var t in due) t.ReminderFiredAt = now;
        _save();

        if (due.Count > 3)
        {
            _queue.Enqueue(new ToastRequest(null, due.Count, missed, PreviewOf(due)));
        }
        else
        {
            foreach (var t in due) _queue.Enqueue(new ToastRequest(t, 0, missed, null));
        }
        ShowNext();
    }

    private static string PreviewOf(List<TodoTask> tasks) =>
        string.Join("、", tasks.Take(2).Select(t => t.Title)) + (tasks.Count > 2 ? "…" : "");

    private void ShowNext()
    {
        if (_current != null || _queue.Count == 0) return;
        var req = _queue.Dequeue();

        string title;
        string? subtitle;
        Brush? bar = null;
        bool actions;
        if (req.Task is { } task)
        {
            title = task.Title;
            subtitle = (req.Missed ? "错过的提醒" : "已到提醒时间") + " · " + _vm.GetListName(task.ListId);
            actions = true;
            bar = task.Priority switch
            {
                Priority.High => Application.Current.TryFindResource("PriorityHighBrush") as Brush,
                Priority.Medium => Application.Current.TryFindResource("PriorityMediumBrush") as Brush,
                Priority.Low => Application.Current.TryFindResource("PriorityLowBrush") as Brush,
                _ => Application.Current.TryFindResource("AccentBrush") as Brush,
            };
        }
        else
        {
            title = req.Missed ? $"你错过了 {req.AggregateCount} 条提醒" : $"{req.AggregateCount} 条任务提醒";
            subtitle = req.AggregatePreview;
            actions = false;
        }

        var toast = new ToastWindow(title, subtitle, bar, actions, _theme.IsDark, _theme.TransparencyEnabled);
        toast.Finished += result => OnToastFinished(req, result);
        toast.Closed += (_, _) =>
        {
            _current = null;
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, ShowNext);
        };

        _current = toast;
        // 先建句柄、定好位再显示，避免窗口先在系统默认位置闪现一帧
        new WindowInteropHelper(toast).EnsureHandle();
        PositionToast(toast);
        toast.Show();
        toast.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PositionToast(toast));
        toast.StartCountdown(TimeSpan.FromSeconds(12));
        try { System.Media.SystemSounds.Exclamation.Play(); }
        catch { /* no audio device — fine */ }
    }

    private void PositionToast(ToastWindow toast)
    {
        var mon = ScreenInterop.GetRightmostMonitor();
        // declared DIP size × monitor scale — Actual* may not be laid out yet
        int w = (int)Math.Round(toast.Width * mon.Scale);
        int h = (int)Math.Round(toast.Height * mon.Scale);
        int gap = (int)Math.Round(12 * mon.Scale);

        // 默认贴工作区右下角；面板展开时仅避让玻璃卡片的可见部分
        //（DockRect 是整窗矩形，含 22 DIP 透明投影边距，直接用会偏移过头）
        int x = mon.Work.Right - gap - w;
        if (_panel.State is PanelState.Shown or PanelState.Showing)
            x = Math.Min(x, _panel.CardLeftPx - gap - w);
        int y = mon.Work.Bottom - gap - h;

        var hwnd = new WindowInteropHelper(toast).Handle;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    private void OnToastFinished(ToastRequest req, ToastResult result)
    {
        if (req.Task is { } task)
        {
            switch (result)
            {
                case ToastResult.Done:
                    _vm.CompleteFromExternal(task);
                    break;
                case ToastResult.Snoozed:
                    task.RemindAt = DateTime.Now.AddMinutes(10);
                    task.ReminderFiredAt = null;
                    _save();
                    break;
                case ToastResult.BodyClicked:
                    _vm.ShowTaskList(task);
                    _panel.ShowPanel(activate: true);
                    break;
            }
        }
        else if (result == ToastResult.BodyClicked)
        {
            _vm.ShowTodayView();
            _panel.ShowPanel(activate: true);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
