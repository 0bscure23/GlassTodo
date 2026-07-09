# Removes test-injected reminder tasks from data.json (run with app stopped).
$dataPath = Join-Path $env:APPDATA "GlassTodo\data.json"
$j = Get-Content $dataPath -Raw -Encoding UTF8 | ConvertFrom-Json
$before = $j.Tasks.Count
$j.Tasks = @($j.Tasks | Where-Object {
    $_.Title -notlike "*Reminder test*" -and $_.Title -notlike "*玻璃通知*"
})
$j | ConvertTo-Json -Depth 6 | Set-Content $dataPath -Encoding UTF8
Write-Output ("tasks: $before -> " + $j.Tasks.Count)
