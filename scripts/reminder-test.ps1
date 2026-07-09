# Injects a test task (reminder fires ~40s from now) into data.json, launches the
# published exe, waits for the 30s reminder scan, captures a screenshot, reports RAM.
$ErrorActionPreference = "Stop"
$dataPath = Join-Path $env:APPDATA "GlassTodo\data.json"
$json = Get-Content $dataPath -Raw -Encoding UTF8 | ConvertFrom-Json

$listId = $json.Lists[0].Id
$now = Get-Date
$task = [pscustomobject]@{
    Id               = [guid]::NewGuid().ToString()
    ListId           = $listId
    Title            = "提醒验证：这条会弹出玻璃通知"
    Notes            = $null
    IsDone           = $false
    Priority         = 3
    DueAt            = $now.Date.ToString("yyyy-MM-ddTHH:mm:ss")
    RemindAt         = $now.AddSeconds(40).ToString("yyyy-MM-ddTHH:mm:ss")
    ReminderFiredAt  = $null
    SortOrder        = -99000
    CreatedAt        = $now.ToString("yyyy-MM-ddTHH:mm:ss")
    CompletedAt      = $null
}
$json.Tasks = @($json.Tasks) + $task
$json | ConvertTo-Json -Depth 6 | Set-Content $dataPath -Encoding UTF8
Write-Output "task injected, remind at $($task.RemindAt)"

Start-Process "F:\TODO\src\GlassTodo\bin\Release\net8.0-windows\win-x64\publish\GlassTodo.exe"
Write-Output "app launched, waiting 80s for reminder scan..."
Start-Sleep -Seconds 80

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;using System.Runtime.InteropServices;
public static class W2 {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}
"@
[W2]::SetProcessDPIAware() | Out-Null
$w = [W2]::GetSystemMetrics(0); $h = [W2]::GetSystemMetrics(1)
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
# crop bottom-right quadrant where the toast should be
$crop = New-Object System.Drawing.Bitmap([int]($w/2), [int]($h/2))
$g2 = [System.Drawing.Graphics]::FromImage($crop)
$g2.DrawImage($bmp, 0, 0, (New-Object System.Drawing.Rectangle([int]($w/2), [int]($h/2), [int]($w/2), [int]($h/2))), [System.Drawing.GraphicsUnit]::Pixel)
$crop.Save("F:\TODO\.verify-toast.png")
$g.Dispose(); $bmp.Dispose(); $g2.Dispose(); $crop.Dispose()

$p = Get-Process GlassTodo -ErrorAction SilentlyContinue
if ($p) { Write-Output ("RAM=" + [math]::Round($p.WorkingSet64/1MB,1) + "MB PID=" + $p.Id) } else { Write-Output "PROCESS NOT RUNNING!" }
