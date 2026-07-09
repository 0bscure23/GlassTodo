$ErrorActionPreference = "Stop"

# --- 1) inject an overdue-reminder task into data.json (app must be stopped) ---
$p = "$env:APPDATA\GlassTodo\data.json"
$j = Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json
# drop earlier test tasks first
$j.Tasks = @($j.Tasks | Where-Object { $_.Title -notmatch "^(Reminder test|milk test)" })
$defaultList = $j.Lists[0].Id
$t = [PSCustomObject]@{
    Id              = [guid]::NewGuid().ToString()
    ListId          = $defaultList
    Title           = "Reminder test - drink water"
    Notes           = $null
    IsDone          = $false
    Priority        = 3
    DueAt           = (Get-Date).Date.ToString("yyyy-MM-ddTHH:mm:ss")
    RemindAt        = (Get-Date).AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ss")
    ReminderFiredAt = $null
    SortOrder       = -99000
    CreatedAt       = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss")
    CompletedAt     = $null
}
$j.Tasks = @($j.Tasks) + $t
$j | ConvertTo-Json -Depth 6 | Set-Content $p -Encoding UTF8
Write-Output "injected overdue reminder task"

# --- 2) launch and wait for the missed-reminder toast ---
Add-Type -AssemblyName System.Drawing
$sig = '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);'
Add-Type -MemberDefinition $sig -Name U32 -Namespace Win
[Win.U32]::SetProcessDPIAware() | Out-Null
$w = [Win.U32]::GetSystemMetrics(0); $h = [Win.U32]::GetSystemMetrics(1)

function Save-Shot([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path)
    $g.Dispose(); $bmp.Dispose()
}

Start-Process "F:\TODO\src\GlassTodo\bin\Debug\net8.0-windows\GlassTodo.exe"
Start-Sleep -Milliseconds 9000
Save-Shot "F:\TODO\.verify-toast.png"
Write-Output "captured .verify-toast.png"

# --- 3) Ctrl+Alt+Q summon, type a task, Enter ---
$shell = New-Object -ComObject WScript.Shell
$shell.SendKeys("^%q")
Start-Sleep -Milliseconds 1200
$shell.SendKeys("milk test 123")
Start-Sleep -Milliseconds 400
$shell.SendKeys("{ENTER}")
Start-Sleep -Milliseconds 900
Save-Shot "F:\TODO\.verify-hotkey-add.png"
Write-Output "captured .verify-hotkey-add.png"

# --- 4) Esc hides ---
$shell.SendKeys("{ESC}")
Start-Sleep -Milliseconds 900
Save-Shot "F:\TODO\.verify-esc.png"
Write-Output "captured .verify-esc.png"

Get-Process GlassTodo -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ("RAM=" + [math]::Round($_.WorkingSet64 / 1MB, 1) + "MB")
}
