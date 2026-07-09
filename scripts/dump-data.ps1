$j = Get-Content (Join-Path $env:APPDATA "GlassTodo\data.json") -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Output ("Lists: " + $j.Lists.Count + "  Tasks: " + $j.Tasks.Count)
foreach ($t in $j.Tasks) {
    Write-Output ("- [{0}] done={1} remind={2} fired={3}" -f $t.Title, $t.IsDone, $t.RemindAt, $t.ReminderFiredAt)
}
