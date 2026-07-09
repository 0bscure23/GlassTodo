$p = "$env:APPDATA\GlassTodo\data.json"
$t = Get-Content $p -Raw -Encoding UTF8
$listId = [regex]::Match($t, '"Id":\s*"([0-9a-fA-F-]{36})"').Groups[1].Value
$remind = (Get-Date).AddSeconds(50).ToString("yyyy-MM-ddTHH:mm:ss")
$today = (Get-Date).ToString("yyyy-MM-ddT00:00:00")
$guid = [guid]::NewGuid().ToString()
$obj = "{`n      `"Id`": `"$guid`",`n      `"ListId`": `"$listId`",`n      `"Title`": `"TOAST位置测试`",`n      `"Notes`": null,`n      `"IsDone`": false,`n      `"Priority`": 2,`n      `"DueAt`": `"$today`",`n      `"RemindAt`": `"$remind`",`n      `"ReminderFiredAt`": null,`n      `"SortOrder`": -99000,`n      `"CreatedAt`": `"$remind`",`n      `"CompletedAt`": null`n    },`n    "
$idx = $t.IndexOf('"Tasks": [')
if ($idx -lt 0) { Write-Error "Tasks array not found"; exit 1 }
$insertAt = $t.IndexOf('{', $idx)
$t = $t.Substring(0, $insertAt) + $obj + $t.Substring($insertAt)
Set-Content $p $t -Encoding UTF8
Write-Output "injected reminder at $remind (list $listId)"
