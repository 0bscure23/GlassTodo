param(
    [string]$Keys = "^%q",
    [switch]$TypeTask
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public static class WK {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@
[WK]::SetProcessDPIAware() | Out-Null
$w = [WK]::GetSystemMetrics(0)
$h = [WK]::GetSystemMetrics(1)

function Save-Shot([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path)
    $g.Dispose(); $bmp.Dispose()
}

# park cursor INSIDE the future panel area (not the 2px trigger strip) so the
# panel stays open once summoned
[WK]::SetCursorPos($w - 300, [int]($h / 2)) | Out-Null
Start-Sleep -Milliseconds 200

$sh = New-Object -ComObject WScript.Shell
$sh.SendKeys($Keys)
Start-Sleep -Milliseconds 800
Save-Shot "F:\TODO\.verify-hotkey.png"

if ($TypeTask) {
    $sh.SendKeys("Buy milk at 6pm")
    Start-Sleep -Milliseconds 400
    $sh.SendKeys("{ENTER}")
    Start-Sleep -Milliseconds 900
    Save-Shot "F:\TODO\.verify-added.png"
}
Write-Output "done"
