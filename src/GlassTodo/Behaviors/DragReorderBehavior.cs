using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassTodo.ViewModels;

namespace GlassTodo.Behaviors;

/// <summary>
/// Hand-rolled vertical drag-reorder for the pending-task ItemsControl.
/// Lift the pressed row after a 6px threshold, shift siblings with short
/// translate animations, commit via ObservableCollection.Move + CommitCommand.
/// </summary>
public static class DragReorderBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DragReorderBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty CommitCommandProperty = DependencyProperty.RegisterAttached(
        "CommitCommand", typeof(ICommand), typeof(DragReorderBehavior), new PropertyMetadata(null));

    public static ICommand? GetCommitCommand(DependencyObject obj) => (ICommand?)obj.GetValue(CommitCommandProperty);
    public static void SetCommitCommand(DependencyObject obj, ICommand? value) => obj.SetValue(CommitCommandProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(DragState), typeof(DragReorderBehavior), new PropertyMetadata(null));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl ic) return;
        if ((bool)e.NewValue)
        {
            ic.PreviewMouseLeftButtonDown += OnMouseDown;
            ic.PreviewMouseMove += OnMouseMove;
            ic.PreviewMouseLeftButtonUp += OnMouseUp;
            ic.LostMouseCapture += OnLostCapture;
            ic.PreviewKeyDown += OnKeyDown;
        }
        else
        {
            ic.PreviewMouseLeftButtonDown -= OnMouseDown;
            ic.PreviewMouseMove -= OnMouseMove;
            ic.PreviewMouseLeftButtonUp -= OnMouseUp;
            ic.LostMouseCapture -= OnLostCapture;
            ic.PreviewKeyDown -= OnKeyDown;
            Cancel(ic, commit: false);
        }
    }

    private sealed class DragState
    {
        public bool Armed;
        public bool Dragging;
        public Point StartPoint;
        public int OriginIndex;
        public FrameworkElement? Container;
        public double[] BaseMids = Array.Empty<double>();
        public double RowHeight;
        public int TargetIndex;
    }

    private static DragState GetState(ItemsControl ic)
    {
        if (ic.GetValue(StateProperty) is not DragState s)
        {
            s = new DragState();
            ic.SetValue(StateProperty, s);
        }
        return s;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var ic = (ItemsControl)sender;
        var state = GetState(ic);
        state.Armed = false;

        // never hijack interactive elements inside the row
        if (HasInteractiveAncestor(e.OriginalSource as DependencyObject, ic)) return;

        var container = FindContainer(ic, e.OriginalSource as DependencyObject);
        if (container == null) return;

        int index = ic.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0) return;

        state.Armed = true;
        state.Dragging = false;
        state.StartPoint = e.GetPosition(ic);
        state.OriginIndex = index;
        state.Container = container;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var ic = (ItemsControl)sender;
        var state = GetState(ic);
        if (!state.Armed || state.Container == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            state.Armed = false;
            return;
        }

        Point pos = e.GetPosition(ic);

        if (!state.Dragging)
        {
            if (Math.Abs(pos.Y - state.StartPoint.Y) < 6 && Math.Abs(pos.X - state.StartPoint.X) < 6) return;
            BeginDrag(ic, state);
        }

        // move the lifted row with the cursor
        if (state.Container.RenderTransform is TransformGroup tg && tg.Children[1] is TranslateTransform tt)
            tt.Y = pos.Y - state.StartPoint.Y;

        UpdateTarget(ic, state, pos.Y);
    }

    private static void BeginDrag(ItemsControl ic, DragState state)
    {
        state.Dragging = true;
        ic.CaptureMouse();

        int count = ic.Items.Count;
        state.BaseMids = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (ic.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe)
                state.BaseMids[i] = fe.TranslatePoint(new Point(0, fe.ActualHeight / 2), ic).Y;
        }
        state.RowHeight = state.Container!.ActualHeight;
        state.TargetIndex = state.OriginIndex;

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1.02, 1.02));
        group.Children.Add(new TranslateTransform());
        state.Container.RenderTransformOrigin = new Point(0.5, 0.5);
        state.Container.RenderTransform = group;
        state.Container.Opacity = 0.88;
        Panel.SetZIndex(state.Container, 99);
    }

    private static void UpdateTarget(ItemsControl ic, DragState state, double cursorY)
    {
        int count = ic.Items.Count;
        int target = 0;
        for (int i = 0; i < count; i++)
        {
            if (i == state.OriginIndex) continue;
            if (state.BaseMids[i] < cursorY) target++;
        }
        state.TargetIndex = Math.Clamp(target, 0, count - 1);

        for (int i = 0; i < count; i++)
        {
            if (i == state.OriginIndex) continue;
            if (ic.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement fe) continue;

            double offset = 0;
            if (i > state.OriginIndex && state.BaseMids[i] < cursorY) offset = -state.RowHeight;
            else if (i < state.OriginIndex && state.BaseMids[i] > cursorY) offset = state.RowHeight;

            AnimateShift(fe, offset);
        }
    }

    private static void AnimateShift(FrameworkElement fe, double to)
    {
        if (fe.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            fe.RenderTransform = tt;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        tt.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var ic = (ItemsControl)sender;
        var state = GetState(ic);
        if (state.Dragging) Cancel(ic, commit: true);
        state.Armed = false;
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        var ic = (ItemsControl)sender;
        var state = GetState(ic);
        if (state.Dragging) Cancel(ic, commit: false);
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        var ic = (ItemsControl)sender;
        var state = GetState(ic);
        if (state.Dragging)
        {
            Cancel(ic, commit: false);
            e.Handled = true;
        }
    }

    private static void Cancel(ItemsControl ic, bool commit)
    {
        var state = GetState(ic);
        if (!state.Dragging)
        {
            state.Armed = false;
            return;
        }
        state.Dragging = false;
        state.Armed = false;

        if (ic.IsMouseCaptured) ic.ReleaseMouseCapture();

        // strip all transforms
        for (int i = 0; i < ic.Items.Count; i++)
        {
            if (ic.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement fe) continue;
            if (fe.RenderTransform is TranslateTransform tt) tt.BeginAnimation(TranslateTransform.YProperty, null);
            fe.RenderTransform = Transform.Identity;
            fe.Opacity = 1;
            Panel.SetZIndex(fe, 0);
        }

        if (commit && state.TargetIndex != state.OriginIndex
            && ic.ItemsSource is System.Collections.ObjectModel.ObservableCollection<TaskItemViewModel> col
            && state.OriginIndex < col.Count && state.TargetIndex < col.Count)
        {
            col.Move(state.OriginIndex, state.TargetIndex);
            GetCommitCommand(ic)?.Execute(null);
        }

        state.Container = null;
    }

    private static bool HasInteractiveAncestor(DependencyObject? source, DependencyObject stopAt)
    {
        while (source != null && source != stopAt)
        {
            if (source is ButtonBase or ToggleButton or TextBox or Thumb) return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private static FrameworkElement? FindContainer(ItemsControl ic, DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement fe && fe.Parent == null
                && ItemsControl.ItemsControlFromItemContainer(fe) == ic)
                return fe;
            if (source is ContentPresenter cp && ItemsControl.ItemsControlFromItemContainer(cp) == ic)
                return cp;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
