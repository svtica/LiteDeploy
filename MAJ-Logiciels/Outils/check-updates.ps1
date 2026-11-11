# Quick check: is there anything newer on the web than what we know? No computer is contacted.
#  - winget packages (packages.txt): latest online; a version seen after the last deployment stays PENDING
#    until a deployment runs (deployments are read from the log copies in Winget-upgrade-logs)
#  - Adobe Reader module (package.json): latest online vs build cached on deploy$ by the last deployment
#  - 1Password module (package.json): date of the MSI online vs MSI downloaded to deploy$ by the last deployment
. (Join-Path $PSScriptRoot "config.ps1")   # site settings: $deployRoot, $majBase, $deployAccount
$logShare   = "$deployRoot\Winget-upgrade-logs"
$stateFile  = "$logShare\last-check.json"

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-WingetLatest([string]$Id) {
    # French winget output uses a no-break space before ":", hence [^:]* after "Version"
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

# Adobe build number (2600322075) -> version (26.003.22075)
function ConvertTo-AdobeVersion([long]$Build) {
    $b = [string]$Build
    if ($b.Length -lt 9) { return $b }
    "{0}.{1}.{2}" -f $b.Substring(0, $b.Length - 8), $b.Substring($b.Length - 8, 3), $b.Substring($b.Length - 5)
}

# Previous state: Id -> { Version, FirstSeen } (older format: Id -> version string)
$previous = @{}
if (Test-Path $stateFile) {
    try {
        (Get-Content $stateFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object {
            if ($_.Value -is [string]) { $previous[$_.Name] = [pscustomobject]@{ Version = $_.Value; FirstSeen = (Get-Date).ToString('s') } }
            else { $previous[$_.Name] = [pscustomobject]@{ Version = [string]$_.Value.Version; FirstSeen = [string]$_.Value.FirstSeen } }
        }
    } catch { }
}
$current = [ordered]@{}
$rows = @()

# Deployments: one log per computer, rewritten at each run of the package
$deployLogs = @(Get-ChildItem $logShare -Filter "*.log" -File -ErrorAction SilentlyContinue)
$lastDeploy = ($deployLogs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime

Write-Host "Checking the web..." -ForegroundColor Cyan
if ($lastDeploy) { Write-Host ("Last deployment seen: {0} ({1} computer log(s) on deploy$)" -f $lastDeploy.ToString('yyyy-MM-dd HH:mm'), $deployLogs.Count) -ForegroundColor DarkGray }

# 1. winget packages
$ids = Get-Content $pkgFile | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { ($_ -split '\|')[0].Trim() }
foreach ($id in $ids) {
    $v    = Get-WingetLatest $id
    $prev = $previous[$id]
    if (-not $v) {
        if ($prev) { $current[$id] = $prev }
        $rows += [pscustomobject]@{ Software = $id; Online = '-'; Known = $(if ($prev) { $prev.Version } else { '-' }); Status = "NOT FOUND"; Compared = "" }
        continue
    }
    $isNew = (-not $prev) -or ($v -ne $prev.Version)
    $firstSeen = if ($isNew) { Get-Date } else { [datetime]$prev.FirstSeen }
    $current[$id] = [pscustomobject]@{ Version = $v; FirstSeen = $firstSeen.ToString('s') }
    # Computers deployed since this version was first seen
    $since = @($deployLogs | Where-Object { $_.LastWriteTime -gt $firstSeen }).Count
    $status = if ($isNew) { "NEW" } elseif ($since -eq 0) { "PENDING" } else { "deployed" }
    $note   = if ($since -gt 0) { "$since computer(s) deployed since $($firstSeen.ToString('yyyy-MM-dd'))" } else { "seen $($firstSeen.ToString('yyyy-MM-dd HH:mm')), not deployed yet" }
    $rows += [pscustomobject]@{ Software = $id; Online = $v; Known = $(if ($prev) { $prev.Version } else { '-' }); Status = $status; Compared = $note }
}

# 2. Adobe Reader module (one build number for all Reader/Acrobat flavors)
if ($useAdobe) {
$adobeId = 'Adobe.Acrobat.Reader.64-bit'
$av = Get-WingetLatest $adobeId
$onlineBuild = if ($av -match '^(\d+)\.(\d+)\.(\d+)') { [long]("{0}{1}{2}" -f $Matches[1], $Matches[2].PadLeft(3, '0'), $Matches[3].PadLeft(5, '0')) } else { $null }
$cached = $null
try { $cached = [long](Get-Content "$deployRoot\Adobe\adobe-latest.json" -Raw | ConvertFrom-Json).ReaderX64 } catch { }
$status = if (-not $onlineBuild) { "NOT FOUND" } elseif (-not $cached) { "NO CACHE" } elseif ($onlineBuild -gt $cached) { "NEW" } else { "same" }
$rows += [pscustomobject]@{ Software = "Adobe Reader / Acrobat"; Online = $(if ($onlineBuild) { ConvertTo-AdobeVersion $onlineBuild } else { '-' }); Known = $(if ($cached) { ConvertTo-AdobeVersion $cached } else { '-' }); Status = $status; Compared = "deploy$ cache" }
}

# 3. 1Password module (only the date of the MSI is published, not its version)
if ($useOnePassword) {
$online = $null
foreach ($attempt in 1..3) {
    try {
        $req = [Net.HttpWebRequest]::Create("https://downloads.1password.com/win/1PasswordSetup-latest.msi")
        $req.Method = "HEAD"; $req.Timeout = 15000
        $resp = $req.GetResponse(); $online = $resp.Headers["Last-Modified"]; $resp.Close()
        break
    } catch { Start-Sleep -Seconds 2 }
}
$stamp = (Get-Content "$deployRoot\1Password\last-modified.txt" -Raw -ErrorAction SilentlyContinue)
if ($stamp) { $stamp = $stamp.Trim() }
$opVersion = $null
try { $opVersion = Get-MsiProperty "$deployRoot\1Password\1PasswordSetup-latest.msi" 'ProductVersion' } catch { }
$shortDate = { param($d) try { ([datetime]::Parse($d)).ToString('yyyy-MM-dd') } catch { $d } }
$status = if (-not $online) { "NOT FOUND" } elseif (-not $stamp) { "NO CACHE" } elseif ($online -ne $stamp) { "NEW" } else { "same" }
# The version online is only known once a deployment downloads the new MSI: when NEW, only its date is shown
$onlineText = if (-not $online) { '-' } elseif ($status -eq 'same' -and $opVersion) { "$opVersion ($(& $shortDate $online))" } else { "? ($(& $shortDate $online))" }
$knownText  = if (-not $stamp) { '-' } elseif ($opVersion) { "$opVersion ($(& $shortDate $stamp))" } else { & $shortDate $stamp }
$rows += [pscustomobject]@{ Software = "1Password"; Online = $onlineText; Known = $knownText; Status = $status; Compared = "deploy$ MSI" }
}

# --- Output ---
$colors = @{ 'NEW' = 'Yellow'; 'PENDING' = 'Yellow'; 'deployed' = 'Green'; 'same' = 'Green'; 'NOT FOUND' = 'Red'; 'NO CACHE' = 'DarkGray' }
Write-Host ""
Write-Host ("{0,-34} {1,-22} {2,-22} {3,-9} {4}" -f "Software", "Online", "Known", "Status", "Detail")
foreach ($r in $rows) {
    Write-Host ("{0,-34} {1,-22} {2,-22} {3,-9} {4}" -f $r.Software, $r.Online, $r.Known, $r.Status, $r.Compared) -ForegroundColor $colors[$r.Status]
}
Write-Host ""
$todo = @($rows | Where-Object { $_.Status -in 'NEW', 'PENDING' })
if ($todo.Count -gt 0) {
    Write-Host "$($todo.Count) version(s) not deployed yet: $(($todo.Software) -join ', ')" -ForegroundColor Yellow
} else {
    Write-Host "Nothing waiting: every version seen has been deployed since." -ForegroundColor Green
}

# Remember the winget versions seen and when they were first seen
try {
    New-Item -Path $logShare -ItemType Directory -Force | Out-Null
    $current | ConvertTo-Json | Set-Content -Path $stateFile -Encoding ASCII
} catch {
    Write-Host "Could not save $stateFile : $($_.Exception.Message)" -ForegroundColor DarkGray
}