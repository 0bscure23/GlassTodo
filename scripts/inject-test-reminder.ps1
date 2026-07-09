param([int]$Seconds = -5)

$path = "$env:APPDATA\GlassTodo\data.json"
$doc = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json

$t = [PSCustomObject]@{
    Id              = [guid]::NewGuid().ToString()
    ListId          = $doc.Lists[0].Id
    Title           = "TEST reminder toast (auto-removed)"
    Notes           = $null
    IsDone          = $false
    Priority        = 3
    DueAt           = (Get-Date).ToString("yyyy-MM-ddT00:00:00")
    RemindAt        = (Get-Date).AddSeconds($Seconds).ToString("yyyy-MM-ddTHH:mm:ss")
    ReminderFiredAt = $null
    SortOrder       = -99000
    CreatedAt       = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss")
    CompletedAt     = $null
}

$doc.Tasks = @($doc.Tasks) + $t
$doc | ConvertTo-Json -Depth 6 | Set-Content $path -Encoding UTF8
Write-Output ("injected, remindAt=" + $t.RemindAt)
