$path = "$env:APPDATA\GlassTodo\data.json"
$doc = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
$before = @($doc.Tasks).Count
$doc.Tasks = @($doc.Tasks | Where-Object { $_.SortOrder -ne -99000 })
$doc | ConvertTo-Json -Depth 6 | Set-Content $path -Encoding UTF8
Write-Output ("removed " + ($before - @($doc.Tasks).Count) + " test task(s)")
