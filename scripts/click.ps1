param([int]$X, [int]$Y, [string]$Shot = "")

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public static class WC {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, System.UIntPtr extra);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@
[WC]::SetProcessDPIAware() | Out-Null

[WC]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 150
[WC]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # left down
Start-Sleep -Milliseconds 60
[WC]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # left up
Start-Sleep -Milliseconds 700

if ($Shot -ne "") {
    $w = [WC]::GetSystemMetrics(0)
    $h = [WC]::GetSystemMetrics(1)
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($Shot)
    $g.Dispose(); $bmp.Dispose()
}
Write-Output "clicked $X,$Y"
