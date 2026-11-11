# Which computers are done and which are still to do (package in <DeployRoot>\Winget-upgrade, run by PDQ or by the panel/agent).
# No computer is contacted: everything is read from deploy$.
#  - Deployments: <DeployRoot>\Winget-upgrade-logs\<COMPUTER>.log (written by the launcher at each run)
#  - Newest version dates: last-check.json (winget packages, from check-updates.ps1),
#    newest installers of the enabled modules (Adobe patch, 1Password MSI) downloaded to deploy$
# A computer is DONE when its last successful run is more recent than the newest version.
# The list of computers still to do is copied to the clipboard, ready to paste in PDQ.
param([string[]]$ComputerName)   # default: enabled Windows 10/11 computers in AD

. (Join-Path $PSScriptRoot "config.ps1")   # site settings: $deployRoot, $majBase, $deployAccount
$logShare    = "$deployRoot\Winget-upgrade-logs"
$stateFile   = "$logShare\last-check.json"
$excludeFile = "$logShare\exclude.txt"

# --- Computers ---
if (-not $ComputerName) {
    $searcher = [adsisearcher]$adFilter
    $searcher.PageSize = 1000
    [void]$searcher.PropertiesToLoad.Add('name')
    $ComputerName = @($searcher.FindAll() | ForEach-Object { [string]$_.Properties['name'][0] } | Sort-Object)
}

# --- Exclusions (one name per line, wildcards allowed, # = comment) ---
if (-not (Test-Path $excludeFile)) {
    try {
        "# Computers never deployed on purpose - one name per line, wildcards allowed (e.g. LAB-*)" | Set-Content -Path $excludeFile -Encoding ASCII
    } catch { }
}
$excludes = @(Get-Content $excludeFile -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
$excluded = @($ComputerName | Where-Object { $c = $_; $excludes | Where-Object { $c -like $_ } })
$computers = @($ComputerName | Where-Object { $excluded -notcontains $_ })

# --- Newest version date (threshold) and what sets it ---
$marks = @()
if (Test-Path $stateFile) {
    try {
        (Get-Content $stateFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object {
            if ($_.Value -isnot [string] -and $_.Value.FirstSeen) {
                $marks += [pscustomobject]@{ What = "$($_.Name) $($_.Value.Version)"; When = [datetime]$_.Value.FirstSeen }
            }
        }
    } catch { }
}
$marks += @(Get-ModuleMarks)
$newest = $marks | Sort-Object When -Descending | Select-Object -First 1

# --- Status per computer ---
$rows = foreach ($c in $computers) {
    $f = Join-Path $logShare "$c.log"
    if (-not (Test-Path $f)) {
        [pscustomobject]@{ Computer = $c; Status = "NEVER DONE"; LastRun = "-"; Detail = "" }
        continue
    }
    $item  = Get-Item $f
    $lines = @(Get-Content $f -Encoding UTF8)
    $final = $lines | Where-Object { $_ -match 'Launcher: ' } | Select-Object -Last 1
    $exit  = if ($final -match 'exit (\d+)') { [int]$Matches[1] } else { -1 }
    $summary = ($lines | Where-Object { $_ -match 'Summary:' } | Select-Object -Last 1) -replace '^\S+ \S+ ', ''
    $status = if ($exit -lt 0) { "INCOMPLETE" }
              elseif ($exit -gt 0) { "FAILED" }
              elseif ($newest -and $item.LastWriteTime -lt $newest.When) { "TO DO" }
              else { "DONE" }
    $detail = switch ($status) {
        "TO DO"      { "run before $($newest.What) ($($newest.When.ToString('yyyy-MM-dd HH:mm')))" }
        "FAILED"     { "exit $exit - $summary" }
        "INCOMPLETE" { "no final launcher line (running, interrupted or timed out)" }
        default      { $summary }
    }
    [pscustomobject]@{ Computer = $c; Status = $status; LastRun = $item.LastWriteTime.ToString('yyyy-MM-dd HH:mm'); Detail = $detail }
}

# --- Output ---
$order  = @{ 'FAILED' = 0; 'INCOMPLETE' = 1; 'TO DO' = 2; 'NEVER DONE' = 3; 'DONE' = 4 }
$colors = @{ 'FAILED' = 'Red'; 'INCOMPLETE' = 'Cyan'; 'TO DO' = 'Yellow'; 'NEVER DONE' = 'DarkGray'; 'DONE' = 'Green' }
$rows = @($rows | Sort-Object @{ e = { $order[$_.Status] } }, Computer)

Write-Host "Deployment status - read from $logShare (no computer contacted)" -ForegroundColor Cyan
if ($newest) {
    Write-Host ("Newest version: {0}, seen {1} - a computer is DONE if its last successful run is after that" -f $newest.What, $newest.When.ToString('yyyy-MM-dd HH:mm')) -ForegroundColor DarkGray
} else {
    Write-Host "No version date known yet: run 'Nouvelles versions sur le web' once" -ForegroundColor DarkGray
}
Write-Host ""
foreach ($r in $rows) {
    Write-Host ("{0,-18} {1,-11} {2,-17} {3}" -f $r.Computer, $r.Status, $r.LastRun, $r.Detail) -ForegroundColor $colors[$r.Status]
}
Write-Host ""
$rows | Group-Object Status | Sort-Object { $order[$_.Name] } | ForEach-Object { Write-Host ("{0,-11} {1}" -f $_.Name, $_.Count) -ForegroundColor $colors[$_.Name] }
if ($excluded.Count -gt 0) { Write-Host ("Excluded ({0}): {1}" -f $excluded.Count, ($excluded -join ', ')) -ForegroundColor DarkGray }
Write-Host ("Exclusion list: {0}" -f $excludeFile) -ForegroundColor DarkGray

# --- Computers still to do: clipboard + text file, ready for PDQ ---
$todo = @($rows | Where-Object { $_.Status -ne 'DONE' } | ForEach-Object { $_.Computer })
$desktop = "$majBase\PDQ"
$stamp   = Get-Date -Format 'yyyyMMdd-HHmm'
$rows | Export-Csv -Path (Join-Path $desktop "deploy-status-$stamp.csv") -NoTypeInformation -Encoding UTF8 -Delimiter ';'
if ($todo.Count -gt 0) {
    $todoFile = Join-Path $desktop "deploy-todo-$stamp.txt"
    $todo | Set-Content -Path $todoFile -Encoding ASCII
    try { $todo -join "`r`n" | Set-Clipboard } catch { }
    Write-Host ""
    Write-Host ("{0} computer(s) still to do - list copied to the clipboard and saved to {1}" -f $todo.Count, $todoFile) -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "Every computer is done." -ForegroundColor Green
}