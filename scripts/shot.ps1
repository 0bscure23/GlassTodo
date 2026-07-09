param([string]$Path = "F:\TODO\.verify-shot.png")

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public static class WS {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@
[WS]::SetProcessDPIAware() | Out-Null
$w = [WS]::GetSystemMetrics(0)
$h = [WS]::GetSystemMetrics(1)
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$bmp.Save($Path)
$g.Dispose(); $bmp.Dispose()
Write-Output "saved $Path (${w}x${h})"
