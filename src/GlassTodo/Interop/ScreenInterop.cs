using System.Runtime.InteropServices;

namespace GlassTodo.Interop;

/// <summary>Physical-pixel rectangle used for all window placement math.</summary>
public readonly record struct RectPx(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool Contains(int x, int y, int expand = 0) =>
        x >= Left - expand && x <= Right + expand && y >= Top - expand && y <= Bottom + expand;
}

public sealed record MonitorInfoPx(RectPx Bounds, RectPx Work, double Scale);

internal static class ScreenInterop
{
    /// <summary>The monitor whose right edge is the rightmost point of the virtual desktop.</summary>
    internal static MonitorInfoPx GetRightmostMonitor()
    {
        IntPtr best = IntPtr.Zero;
        int bestRight = int.MinValue;
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr mon, IntPtr _, ref NativeMethods.RECT r, IntPtr _) =>
            {
                if (r.Right > bestRight)
                {
                    bestRight = r.Right;
                    best = mon;
                }
                return true;
            }, IntPtr.Zero);
        return FromHandle(best);
    }

    internal static MonitorInfoPx FromHandle(IntPtr hMonitor)
    {
        var mi = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        double scale = 1.0;
        if (hMonitor != IntPtr.Zero && NativeMethods.GetMonitorInfoW(hMonitor, ref mi))
        {
            if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dx, out _) == 0)
                scale = dx / 96.0;
            return new MonitorInfoPx(ToRectPx(mi.Monitor), ToRectPx(mi.Work), scale);
        }
        // Extremely defensive fallback: primary screen via SystemParameters would need DIP math; report a sane default.
        return new MonitorInfoPx(new RectPx(0, 0, 1920, 1080), new RectPx(0, 0, 1920, 1032), 1.0);
    }

    private static RectPx ToRectPx(NativeMethods.RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    /// <summary>
    /// 几何判定全屏应用：前台窗口存在、不是壳层窗口（桌面/任务栏）、没有标题栏、
    /// 且完整覆盖其所在显示器。不再依赖 SHQueryUserNotificationState——它会把
    /// “桌面处于前台”（Progman/WorkerW 本身是全屏窗口）误报为全屏应用。
    /// </summary>
    internal static bool IsFullscreenAppActive()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false; // 没有前台窗口（如 Win+D 之后）＝ 桌面

        var sb = new System.Text.StringBuilder(64);
        if (NativeMethods.GetClassNameW(fg, sb, sb.Capacity) > 0)
        {
            string cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false; // 壳层桌面 / 任务栏
        }

        // 普通最大化窗口保留 WS_CAPTION；无边框全屏（游戏/F11/放映）才没有
        long style = (long)NativeMethods.GetWindowLongPtr(fg, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION) return false;

        if (!NativeMethods.GetWindowRect(fg, out var r)) return false;
        var mon = FromHandle(NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 },
            NativeMethods.MONITOR_DEFAULTTONEAREST));
        var b = mon.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    internal static bool IsAnyMouseButtonDown() =>
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0 ||
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
}
