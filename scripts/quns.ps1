Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Q {
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int s);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out P p);
    public struct P { public int X; public int Y; }
}
"@
$s = 0
[Q]::SHQueryUserNotificationState([ref]$s) | Out-Null
Write-Output ("QUNS=" + $s + "  (2=BUSY 3=D3D_FULL 4=PRESENTATION 5=NORMAL 6=QUIET 7=APP)")
$p = New-Object Q+P
[Q]::GetCursorPos([ref]$p) | Out-Null
Write-Output ("cursor=" + $p.X + "," + $p.Y)
