using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GlassTodo.Behaviors;

public static class ViewBehaviors
{
    // ----- FocusWhenVisible: focus (and select) a TextBox the moment it becomes visible -----

    public static readonly DependencyProperty FocusWhenVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusWhenVisible", typeof(bool), typeof(ViewBehaviors),
        new PropertyMetadata(false, OnFocusWhenVisibleChanged));

    public static bool GetFocusWhenVisible(DependencyObject obj) => (bool)obj.GetValue(FocusWhenVisibleProperty);
    public static void SetFocusWhenVisible(DependencyObject obj, bool value) => obj.SetValue(FocusWhenVisibleProperty, value);

    private static void OnFocusWhenVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        if ((bool)e.NewValue)
        {
            el.IsVisibleChanged += OnVisibleChanged;
            if (el.IsVisible) Focus(el);
        }
        else
        {
            el.IsVisibleChanged -= OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is FrameworkElement el) Focus(el);
    }

    private static void Focus(FrameworkElement el)
    {
        el.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            el.Focus();
            if (el is TextBox tb) tb.SelectAll();
        });
    }

    // ----- IsEditBox: marks inline-edit boxes so an empty edit box still holds the interaction lock -----

    public static readonly DependencyProperty IsEditBoxProperty = DependencyProperty.RegisterAttached(
        "IsEditBox", typeof(bool), typeof(ViewBehaviors), new PropertyMetadata(false));

    public static bool GetIsEditBox(DependencyObject obj) => (bool)obj.GetValue(IsEditBoxProperty);
    public static void SetIsEditBox(DependencyObject obj, bool value) => obj.SetValue(IsEditBoxProperty, value);
}
