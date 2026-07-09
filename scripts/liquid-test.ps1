Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WL {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@

[WL]::SetProcessDPIAware() | Out-Null
$w = [WL]::GetSystemMetrics(0)
$h = [WL]::GetSystemMetrics(1)

function Save-Shot([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path)
    $g.Dispose(); $bmp.Dispose()
}

# 1) trigger at the right edge; capture mid sheen-sweep
[WL]::SetCursorPos($w - 1, [int]($h / 2)) | Out-Null
Start-Sleep -Milliseconds 900
Save-Shot "F:\TODO\.verify-sheen.png"

# 2) move onto the card; capture the pointer-tracking glow
[WL]::SetCursorPos($w - 360, [int]($h / 2) - 120) | Out-Null
Start-Sleep -Milliseconds 900
Save-Shot "F:\TODO\.verify-glow.png"

Write-Output "done"
