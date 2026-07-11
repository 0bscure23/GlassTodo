using System.Runtime.InteropServices;

namespace GlassTodo.Interop;

internal enum GlassBackdrop
{
    /// <summary>Win11 系统亚克力（DWMSBT_TRANSIENTWINDOW）——跟随窗口移动最顺滑，但浅色下偏奶白。</summary>
    SystemAcrylic,
    /// <summary>旧式 SetWindowCompositionAttribute 亚克力——透明度自控，通透感强（液态玻璃模式）。</summary>
    LegacyAcrylic,
    /// <summary>无模糊（系统关闭透明效果时的降级）。</summary>
    None,
}

internal static class WindowEffects
{
    /// <summary>
    /// Applies the glass recipe: sheet-of-glass frame, round corners, dark mode and the
    /// requested backdrop. Returns true when a blur backdrop is actually active.
    /// </summary>
    internal static bool ApplyGlass(IntPtr hwnd, bool dark, GlassBackdrop mode)
    {
        if (hwnd == IntPtr.Zero) return false;

        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        SetDarkMode(hwnd, dark);

        if (mode == GlassBackdrop.LegacyAcrylic)
        {
            SetSystemBackdrop(hwnd, NativeMethods.DWMSBT_NONE);
            // 清玻璃：轻高斯模糊，背后窗口内容保持可读；着色交给 XAML 层（此状态下 GradientColor 不参与混色）
            if (SetAccent(hwnd, NativeMethods.ACCENT_ENABLE_BLURBEHIND, 0x00000000)) return true;
            // 老接口不可用则退回系统亚克力
            mode = GlassBackdrop.SystemAcrylic;
        }

        if (mode == GlassBackdrop.SystemAcrylic)
        {
            SetAccent(hwnd, NativeMethods.ACCENT_DISABLED, 0);
            return SetSystemBackdrop(hwnd, NativeMethods.DWMSBT_TRANSIENTWINDOW) == 0;
        }

        SetAccent(hwnd, NativeMethods.ACCENT_DISABLED, 0);
        SetSystemBackdrop(hwnd, NativeMethods.DWMSBT_NONE);
        return false;
    }

    private static int SetSystemBackdrop(IntPtr hwnd, int backdrop) =>
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

    private static bool SetAccent(IntPtr hwnd, int state, uint gradientColor)
    {
        var policy = new NativeMethods.ACCENT_POLICY
        {
            AccentState = state,
            AccentFlags = 0,
            GradientColor = gradientColor,
            AnimationId = 0,
        };
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.ACCENT_POLICY>());
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = NativeMethods.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = Marshal.SizeOf<NativeMethods.ACCENT_POLICY>(),
            };
            return NativeMethods.SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    internal static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
    }

    internal static void AddExStyle(IntPtr hwnd, long styles)
    {
        long cur = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)(cur | styles));
    }
}
