# Post-deployment check for the "Mise a jour logiciels tiers" PDQ package.
# Run on the VM with the deployment account, once PDQ has finished.
# For each workstation: last run of the package, its summary, and the real installed versions
# compared with the latest versions (winget, Adobe cache and 1Password MSI on deploy$).
param(
    [string[]]$ComputerName,          # default: all enabled Windows 10/11 computers in AD (LDAP)
    [int]$ThrottleLimit = 30
)

. (Join-Path $PSScriptRoot "config.ps1")   # site settings: $deployRoot, $majBase, $deployAccount

# --- Targets ---
if (-not $ComputerName) {
    # Enabled Windows 10/11 computers from AD (LDAP query, no RSAT needed)
    $searcher = [adsisearcher]$adFilter
    $searcher.PageSize = 1000
    [void]$searcher.PropertiesToLoad.Add('name')
    $ComputerName = @($searcher.FindAll() | ForEach-Object { [string]$_.Properties['name'][0] } | Sort-Object)
}
Write-Host "Checking $($ComputerName.Count) computer(s)..." -ForegroundColor Cyan

# --- Latest versions (reference) ---
function Get-WingetLatest([string]$Id) {
    # winget writes UTF-8 and French output uses a no-break space before ":" (read as garbage by PS 5.1 otherwise)
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $out = winget show --id $Id -e --source winget --accept-source-agreements --disable-interactivity 2>$null | Out-String
    if ($out -match '(?m)^\s*Version[^:\r\n]*:\s*(\S+)') { return $Matches[1] }
    return $null
}

function Get-MsiProperty([string]$Path, [string]$Property) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db   = $installer.GetType().InvokeMember("OpenDatabase", "InvokeMethod", $null, $installer, @($Path, 0))
    $view = $db.GetType().InvokeMember("OpenView", "InvokeMethod", $null, $db, @("SELECT Value FROM Property WHERE Property='$Property'"))
    [void]$view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null)
    $rec  = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
    $value = if ($rec) { $rec.GetType().InvokeMember("StringData", "GetProperty", $null, $rec, 1) } else { $null }
    [void]$view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null)
    foreach ($o in $rec, $view, $db, $installer) { if ($o) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($o) } }
    return [string]$value
}

# Tracked software (packages.txt + modules of package.json); Adobe is compared per flavor further down
$tracked = @(Get-TrackedSoftware | Where-Object { $_.Kind -ne 'adobe' })
$latest = [ordered]@{}
foreach ($t in $tracked) {
    $latest[$t.Label] = if ($t.Kind -eq 'winget') { Get-WingetLatest $t.Id } elseif ($t.Kind -eq '1password' -and (Test-Path $opMsiFile)) { Get-MsiProperty $opMsiFile 'ProductVersion' } else { $null }
}
# Adobe module: build numbers per flavor, as cached by the launcher (e.g. 2600322075)
$adobeLatest = @{}
if ($useAdobe) {
    try { (Get-Content $adobeCacheFile -Raw -ErrorAction Stop | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $adobeLatest[$_.Name] = [long]$_.Value } }
    catch { Write-Host "WARNING: no Adobe cache ($adobeCacheFile)" -ForegroundColor Yellow }
}

$missingRef = @($latest.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object { $_.Key })
if ($missingRef.Count -gt 0) { Write-Host "WARNING: no reference version for $($missingRef -join ', ') - these are NOT compared" -ForegroundColor Yellow }
Write-Host "Reference versions:" -ForegroundColor Cyan
$latest.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-10} {1}" -f $_.Key, $_.Value) }
if ($useAdobe) { Write-Host ("  {0,-10} {1}" -f 'Adobe', (($adobeLatest.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', ')) }

# --- Remote inventory ---
$remote = Invoke-Command -ComputerName $ComputerName -ThrottleLimit $ThrottleLimit -ErrorAction SilentlyContinue -ErrorVariable remoteErrors -ScriptBlock {
    # Protected work folder of the package (ProgramData); Windows\Temp for computers not run since
    $log   = Join-Path $env:ProgramData "MAJ-Logiciels\winget-upgrade\winget-upgrade.log"
    if (-not (Test-Path $log)) { $log = "C:\Windows\Temp\winget-upgrade\winget-upgrade.log" }
    $lines = @(Get-Content $log -Encoding UTF8 -ErrorAction SilentlyContinue)
    $apps  = Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
                              "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayVersion } |
        Select-Object @{ n = 'Key'; e = { $_.PSChildName } }, DisplayName, DisplayVersion,
                      @{ n = 'Wow'; e = { $_.PSPath -like '*WOW6432Node*' } }
    [pscustomobject]@{
        LogTime = if (Test-Path $log) { (Get-Item $log).LastWriteTime } else { $null }
        Summary = ($lines | Where-Object { $_ -match 'Summary:' } | Select-Object -Last 1) -replace '^\S+ \S+ ', ''
        Issues  = @($lines | Where-Object { $_ -match 'failed -|could not|Unknown|forbidden|not applicable|msiexec (?!0 |3010 )\d+' } |
                    ForEach-Object { $_ -replace '^\S+ (\S+) ', '$1 ' })
        Apps    = @($apps)
    }
}

# --- Comparison helpers ---
function ConvertTo-Ver([string]$v) {
    if (-not $v) { return $null }
    $parts = @(($v -replace '[^\d\.]', '').Split('.') | Where-Object { $_ -ne '' } | Select-Object -First 4)
    while ($parts.Count -lt 2) { $parts += '0' }
    try { return [version]($parts -join '.') } catch { return $null }
}
function ConvertTo-AdobeBuild([string]$Version) {
    $p = $Version.Split('.')
    if ($p.Count -ne 3) { return $null }
    return [long]("{0}{1}{2}" -f $p[0], $p[1].PadLeft(3, '0'), $p[2].PadLeft(5, '0'))
}
$adobeFlavors = [ordered]@{
    'Reader32'     = '^\{AC76BA86-7AD7-\d{4}-7B44-AC0F074E4100\}$'
    'ReaderX64'    = '^\{AC76BA86-\d{4}-1033-7760-BC15014EA700\}$'
    'ReaderX64MUI' = '^\{AC76BA86-1033-FF00-7760-BC15014EA700\}$'
    'AcrobatX64'   = '^\{AC76BA86-1033-FFFF-7760-BC15014EA700\}$'
    'Acrobat32'    = '^\{AC76BA86-1033-FFFF-7760-0C0F074E4100\}$'
}

# --- Build report ---
$today  = (Get-Date).Date
$report = foreach ($r in $remote) {
    $outdated = @(); $notes = @(); $found = @()

    foreach ($c in $tracked) {
        if (-not $c.Detect) { continue }
        $app = $r.Apps | Where-Object { $_.DisplayName -match $c.Detect } | Sort-Object { ConvertTo-Ver $_.DisplayVersion } -Descending | Select-Object -First 1
        if (-not $app) { continue }
        $found += $c.Label
        $haveText = if ($app.DisplayName -match $c.Detect -and $Matches[1]) { [string]$Matches[1] } else { [string]$app.DisplayVersion }
        $have = ConvertTo-Ver $haveText
        $want = ConvertTo-Ver $latest[$c.Label]
        if ($have -and $want -and $have -lt $want) { $outdated += "$($c.Label) $haveText < $($latest[$c.Label])" }
    }

    foreach ($fl in @(if ($useAdobe) { $adobeFlavors.GetEnumerator() })) {
        foreach ($app in @($r.Apps | Where-Object { $_.Key -match $fl.Value })) {
            $found += "Adobe $($fl.Key)"
            $have = ConvertTo-AdobeBuild $app.DisplayVersion
            $want = $adobeLatest[$fl.Key]
            if ($have -and $want -and $have -lt $want) { $outdated += "Adobe $($fl.Key) $($app.DisplayVersion) < $want" }
        }
    }

    if ($r.Issues.Count -gt 0) { $notes += $r.Issues }

    $ranToday = $r.LogTime -and $r.LogTime -ge $today
    $status = if ($ranToday -and -not $r.Summary) { "RUNNING" }
              elseif ($outdated.Count -gt 0) { "OUTDATED" }
              elseif (-not $ranToday)    { "NOT RUN TODAY" }
              elseif ($r.Summary -notmatch ' 0 failed' -or $r.Issues.Count -gt 0) { "FAILED" }
              else                       { "OK" }

    [pscustomobject]@{
        Computer = $r.PSComputerName
        Status   = $status
        LastRun  = if ($r.LogTime) { $r.LogTime.ToString('yyyy-MM-dd HH:mm') } else { "-" }
        Summary  = $r.Summary
        Outdated = $outdated -join '; '
        Notes    = $notes -join '; '
        Software = ($found | Sort-Object -Unique) -join ', '
    }
}
foreach ($e in $remoteErrors) {
    $report += [pscustomobject]@{ Computer = [string]$e.TargetObject; Status = "UNREACHABLE"; LastRun = "-"; Summary = ""; Outdated = ""; Notes = ""; Software = "" }
}
$report = @($report | Sort-Object @{ e = { @{ 'RUNNING' = 0; 'OUTDATED' = 1; 'FAILED' = 2; 'NOT RUN TODAY' = 3; 'UNREACHABLE' = 4; 'OK' = 5 }[$_.Status] } }, Computer)

# --- Output ---
$colors = @{ 'RUNNING' = 'Cyan'; 'OK' = 'Green'; 'OUTDATED' = 'Red'; 'FAILED' = 'Red'; 'NOT RUN TODAY' = 'Yellow'; 'UNREACHABLE' = 'DarkGray' }
foreach ($row in $report) {
    $detail = @($row.Outdated, $row.Notes) | Where-Object { $_ }
    Write-Host ("{0,-18} {1,-14} {2,-17} {3}" -f $row.Computer, $row.Status, $row.LastRun, ($detail -join ' | ')) -ForegroundColor $colors[$row.Status]
}
Write-Host ""
$report | Group-Object Status | Sort-Object Name | ForEach-Object { Write-Host ("{0,-14} {1}" -f $_.Name, $_.Count) -ForegroundColor $colors[$_.Name] }

$csv = Join-Path "$majBase\Rapports\Verification" ("winget-upgrade-report-{0}.csv" -f (Get-Date -Format 'yyyyMMdd-HHmm'))
$report | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8 -Delimiter ';'
Write-Host "CSV: $csv" -ForegroundColor Cyan