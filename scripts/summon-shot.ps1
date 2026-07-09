Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WT {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@
[WT]::SetProcessDPIAware() | Out-Null
$w = [WT]::GetSystemMetrics(0)
$h = [WT]::GetSystemMetrics(1)

# summon via single-instance signal (no cursor interference)
Start-Process "F:\TODO\src\GlassTodo\bin\Release\net8.0-windows\win-x64\publish\GlassTodo.exe"
Start-Sleep -Milliseconds 850

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$bmp.Save("F:\TODO\.verify-trans.png")
$g.Dispose(); $bmp.Dispose()
Write-Output "saved ${w}x${h}"
