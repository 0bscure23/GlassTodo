using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassTodo.Interop;

namespace GlassTodo.Views;

public enum ToastResult
{
    Timeout,
    Done,
    Snoozed,
    BodyClicked,
    Dismissed,
}

public partial class ToastWindow : Window
{
    private readonly bool _dark;
    private readonly bool _acrylic;
    private Storyboard? _countdown;
    private bool _resultRaised;

    /// <summary>Raised exactly once with how the toast ended, right before it closes.</summary>
    public event Action<ToastResult>? Finished;

    public ToastWindow(string title, string? subtitle, Brush? priorityBrush, bool showActions, bool dark, bool acrylic)
    {
        _dark = dark;
        _acrylic = acrylic;
        InitializeComponent();

        TitleText.Text = title;
        if (string.IsNullOrEmpty(subtitle)) SubtitleText.Visibility = Visibility.Collapsed;
        else SubtitleText.Text = subtitle;
        if (priorityBrush != null) PriorityBar.Background = priorityBrush;
        if (!showActions) ActionsRow.Visibility = Visibility.Collapsed;
        // fixed height sidesteps the WPF SizeToContent + WindowChrome clipping bug
        Height = showActions ? 118 : 74;

        MouseEnter += (_, _) => _countdown?.Pause(this);
        MouseLeave += (_, _) => _countdown?.Resume(this);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.ApplyGlass(hwnd, _dark, _acrylic ? GlassBackdrop.SystemAcrylic : GlassBackdrop.None);
        // never steal focus — the toast is purely mouse-interactive
        WindowEffects.AddExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    public void StartCountdown(TimeSpan duration)
    {
        var anim = new DoubleAnimation(1, 0, duration);
        anim.Completed += (_, _) => Finish(ToastResult.Timeout);
        _countdown = new Storyboard();
        _countdown.Children.Add(anim);
        Storyboard.SetTarget(anim, Progress);
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        _countdown.Begin(this, isControllable: true);
    }

    private void Finish(ToastResult result)
    {
        if (_resultRaised) return;
        _resultRaised = true;
        Finished?.Invoke(result);
        Close();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Finish(ToastResult.Done);
    private void Snooze_Click(object sender, RoutedEventArgs e) => Finish(ToastResult.Snoozed);
    private void Close_Click(object sender, RoutedEventArgs e) => Finish(ToastResult.Dismissed);
    private void Body_Click(object sender, MouseButtonEventArgs e) => Finish(ToastResult.BodyClicked);
}
