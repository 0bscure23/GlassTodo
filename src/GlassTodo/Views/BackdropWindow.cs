using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using GlassTodo.Interop;

namespace GlassTodo.Views;

/// <summary>
/// 磨砂背景板：贴在玻璃卡片正后方的无交互窗口，由系统亚克力提供真实的背景模糊，
/// 使上层清玻璃"透光但模糊背后光线"。窗口区域被裁成与卡片一致的圆角矩形，
/// 位置由 PanelController 逐帧同步。
/// </summary>
public sealed class BackdropWindow : Window
{
    public IntPtr Hwnd { get; private set; }

    public BackdropWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(-1),
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });
    }

    public void EnsureHandle()
    {
        Hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowEffects.AddExStyle(Hwnd,
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT);
    }
}
