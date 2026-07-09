Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class W2 {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@
[W2]::SetProcessDPIAware() | Out-Null
$w = [W2]::GetSystemMetrics(0); $h = [W2]::GetSystemMetrics(1)

function Save-Shot([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path)
    $g.Dispose(); $bmp.Dispose()
}

$shell = New-Object -ComObject WScript.Shell

# 1) summon via global hotkey Ctrl+Alt+Q
$shell.SendKeys("^%q")
Start-Sleep -Milliseconds 900

# 2) type a task and press Enter (quick-add should be focused)
$shell.SendKeys("Buy milk before 6pm")
Start-Sleep -Milliseconds 400
$shell.SendKeys("{ENTER}")
Start-Sleep -Milliseconds 700
Save-Shot "F:\TODO\.verify-hotkey-add.png"
Write-Output "captured after hotkey + typed task"

# 3) Esc should hide the panel
$shell.SendKeys("{ESC}")
Start-Sleep -Milliseconds 700
Save-Shot "F:\TODO\.verify-esc-hide.png"
Write-Output "captured after Esc"
