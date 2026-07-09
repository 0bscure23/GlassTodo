$p = "$env:APPDATA\GlassTodo\data.json"
$t = Get-Content $p -Raw -Encoding UTF8
# remove any task object whose title starts with TOAST (test pollution; objects have no nested braces)
$pattern = '\{[^{}]*"Title":\s*"TOAST[^{}]*\},\s*'
$before = ([regex]::Matches($t, $pattern)).Count
$t = [regex]::Replace($t, $pattern, '')
# also handle a trailing (last-in-array) variant without comma
$pattern2 = ',?\s*\{[^{}]*"Title":\s*"TOAST[^{}]*\}(?=\s*\])'
$before += ([regex]::Matches($t, $pattern2)).Count
$t = [regex]::Replace($t, $pattern2, '')
[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "removed $before test task(s)"
