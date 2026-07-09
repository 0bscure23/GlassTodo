param(
    [int]$DwellWaitMs = 1600,
    [int]$HideWaitMs = 1400
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class W {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@

[W]::SetProcessDPIAware() | Out-Null
$w = [W]::GetSystemMetrics(0)
$h = [W]::GetSystemMetrics(1)
Write-Output "physical screen: ${w}x${h}"

function Save-Shot([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path)
    $g.Dispose(); $bmp.Dispose()
}

# 1) park cursor at the right edge, wait for dwell + slide-in
[W]::SetCursorPos($w - 1, [int]($h / 2)) | Out-Null
Start-Sleep -Milliseconds $DwellWaitMs
Save-Shot "F:\TODO\.verify-shown.png"
Write-Output "captured .verify-shown.png (panel should be visible)"

# 2) move cursor to screen center, wait for grace + slide-out
[W]::SetCursorPos([int]($w / 2), [int]($h / 2)) | Out-Null
Start-Sleep -Milliseconds $HideWaitMs
Save-Shot "F:\TODO\.verify-hidden.png"
Write-Output "captured .verify-hidden.png (panel should be gone)"
