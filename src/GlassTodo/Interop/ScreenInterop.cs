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

    internal static bool IsFullscreenAppActive()
    {
        if (NativeMethods.SHQueryUserNotificationState(out int state) != 0) return false;
        return state is NativeMethods.QUNS_BUSY
            or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
            or NativeMethods.QUNS_PRESENTATION_MODE
            or NativeMethods.QUNS_APP;
    }

    internal static bool IsAnyMouseButtonDown() =>
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0 ||
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
}
