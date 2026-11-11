# MAJ-Logiciels agent: runs every few minutes (scheduled task on the VM, deployment account).
#  - Schedules: deploy a fixed selection or "all computers to do" at a given time (once or recurring)
#  - Queue ("deploy when available"): deploy queued computers as soon as they answer on the network
# Settings: <MajBase>\agent-settings.json  - Queue: <MajBase>\agent-queue.json  (MajBase: config.json, default C:\MAJ-Logiciels)
# Log: <MajBase>\Rapports\Agent\agent-YYYY-MM.log  - Status for the panel: <MajBase>\agent-status.json
# -Force: treat every enabled schedule as due now (for tests). DryRun in the settings: log what would be done, launch nothing.
param([switch]$Force)

. (Join-Path $PSScriptRoot "config.ps1")   # site settings: $deployRoot, $majBase, $deployAccount
$base         = $majBase
$settingsFile = "$base\agent-settings.json"
$queueFile    = "$base\agent-queue.json"
$statusFile   = "$base\agent-status.json"
$stateFile    = "$base\agent-state.json"
$logDir       = "$base\Rapports\Agent"
$logShare     = "$deployRoot\Winget-upgrade-logs"
$excludeFile  = "$logShare\exclude.txt"
$nightFile    = "$logShare\night-only.txt"
$versionFile  = "$logShare\last-check.json"
$credFile     = Join-Path $env:LOCALAPPDATA "MAJ-Logiciels\state.bin"
$launcher     = "$deployRoot\Winget-upgrade\winget-upgrade.ps1"
$macCacheFile = "$base\mac-cache.json"   # written by the panel at each network check

# Wake-on-LAN magic packet to the general broadcast and to the computer's last known subnet (.255)
function Send-WakeOnLan([string[]]$Names) {
    $cache = Read-Json $macCacheFile $null
    $udp = New-Object Net.Sockets.UdpClient; $udp.EnableBroadcast = $true
    foreach ($n in $Names) {
        $e = if ($cache) { $cache.$n } else { $null }
        if (-not $e -or -not @($e.Macs).Count) { "$n : MAC unknown"; continue }
        foreach ($mac in @($e.Macs)) {
            $b = [byte[]]($mac -split '[:-]' | ForEach-Object { [Convert]::ToByte($_, 16) })
            $pkt = [byte[]]((,[byte]255 * 6) + ($b * 16))
            $dest = @('255.255.255.255') + $(if ($e.Ip -match '^(\d+\.\d+\.\d+)\.\d+$') { "$($Matches[1]).255" })
            foreach ($d in ($dest | Select-Object -Unique)) { [void]$udp.Send($pkt, $pkt.Length, $d, 9) }
        }
        "$n : packet sent"
    }
    $udp.Close()
}

New-Item -Path $logDir -ItemType Directory -Force | Out-Null
$log = Join-Path $logDir ("agent-{0}.log" -f (Get-Date -Format 'yyyy-MM'))
function Write-AgentLog([string]$Message) { "{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message | Add-Content -Path $log -Encoding UTF8 }
function Read-Json([string]$Path, $Default) {
    if (-not (Test-Path $Path)) { return $Default }
    try { return (Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { Write-AgentLog "WARNING: could not read $Path ($($_.Exception.Message))"; return $Default }
}
function Save-Json([string]$Path, $Object) { ConvertTo-Json -InputObject $Object -Depth 6 | Set-Content -Path $Path -Encoding UTF8 }

# --- Settings ---
$settings = Read-Json $settingsFile $null
if (-not $settings) { Write-AgentLog "No settings file ($settingsFile): nothing done"; exit 0 }
$dry = [bool]$settings.DryRun
$tag = if ($dry) { "[DRY RUN] " } else { "" }
# Password needed only when the package is started as the deployment account (LaunchAs = DeployAccount)
$passwordMissing = ($launchAs -eq 'DeployAccount') -and -not (Test-Path $credFile)
if ($passwordMissing -and $settings.Enabled) { Write-AgentLog "WARNING: no saved deployment password ($credFile) - in real mode nothing could be launched. Run one 'Deployer maintenant' from the panel." }
if (-not $settings.Enabled) {
    Save-Json $statusFile @{ LastPass = (Get-Date).ToString('s'); Message = "Agent désactivé"; Launched = @(); Queued = 0; DryRun = $dry }
    exit 0
}
$now = Get-Date
$bh  = $settings.BusinessHours
$businessHours = (@($bh.Days) -contains $now.DayOfWeek.ToString()) -and ($now.Hour -ge [int]$bh.Start) -and ($now.Hour -lt [int]$bh.End)

# --- Once a day (WebCheckTime, default 17:00): look for new versions on the web, so new versions make computers "to do" ---
$webTime = if ($settings.WebCheckTime) { [string]$settings.WebCheckTime } else { "17:00" }
$webFile = "$base\agent-webcheck.txt"
$lastWeb = [datetime]::MinValue
try { $lastWeb = [datetime]((Get-Content $webFile -Raw -ErrorAction Stop).Trim()) } catch { }
$webAt = [datetime]::Today.Add([TimeSpan]::Parse($webTime))
if ($settings.WebCheck -ne $false -and $now -ge $webAt -and $lastWeb -lt $webAt) {
    $cu = Join-Path $PSScriptRoot "check-updates.ps1"
    if (Test-Path $cu) {
        $cuOut = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cu 2>&1 | ForEach-Object { "$_" })
        $news = @($cuOut | Where-Object { $_ -match '\s(NEW|PENDING)\s' } | ForEach-Object { ($_ -replace '\s{2,}', ' ').Trim() })
        $bad  = @($cuOut | Where-Object { $_ -match 'NOT FOUND|Could not save' } | ForEach-Object { ($_ -replace '\s{2,}', ' ').Trim() })
        Write-AgentLog ("Web check: " + $(if ($news) { $news -join ' / ' } else { "nothing new" }))
        if ($bad) { Write-AgentLog ("WARNING web check: " + ($bad -join ' / ')) }
        $now.ToString('s') | Set-Content $webFile
    } else { Write-AgentLog "WARNING: check-updates.ps1 not found ($cu)" }
}

# --- Status of the computers (same rules as the panel) ---
function Get-ListFile([string]$Path) { @(Get-Content $Path -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') }) }
$searcher = [adsisearcher]$adFilter
$searcher.PageSize = 1000
[void]$searcher.PropertiesToLoad.Add('name')
$adNames  = @($searcher.FindAll() | ForEach-Object { [string]$_.Properties['name'][0] } | Sort-Object)
$excludes = Get-ListFile $excludeFile
$names    = @($adNames | Where-Object { $n = $_; -not ($excludes | Where-Object { $n -like $_ }) })
$night    = @(Get-ListFile $nightFile)

$marks = @()
try {
    (Get-Content $versionFile -Raw -ErrorAction Stop | ConvertFrom-Json).PSObject.Properties | ForEach-Object {
        if ($_.Value -isnot [string] -and $_.Value.FirstSeen) { $marks += [datetime]$_.Value.FirstSeen }
    }
} catch { }
$marks += @(Get-ModuleMarks | ForEach-Object { $_.When })   # newest installers of the enabled modules
$newest = $marks | Sort-Object -Descending | Select-Object -First 1

$upToDate = @{}   # written by the program: computer -> time its tracked software was read up to date
$utd = Read-Json "$base\uptodate.json" $null
if ($utd) { foreach ($p in $utd.PSObject.Properties) { $upToDate[$p.Name] = [datetime]$p.Value } }
$status = @{}; $lastRun = @{}
foreach ($c in $names) {
    $f = Join-Path $logShare "$c.log"
    if (-not (Test-Path $f)) { $status[$c] = 'NEVER'; continue }
    $item  = Get-Item $f
    $lines = @(Get-Content $f -Encoding UTF8)
    $final = $lines | Where-Object { $_ -match 'Launcher: ' } | Select-Object -Last 1
    $exit  = if ($final -match 'exit (\d+)') { [int]$Matches[1] } else { -1 }
    $sum   = $lines | Where-Object { $_ -match 'Summary:' } | Select-Object -Last 1
    $status[$c] = if ($exit -gt 0) { 'FAILED' } elseif ($exit -lt 0 -or -not $sum) { 'INCOMPLETE' }
                  elseif ($newest -and $item.LastWriteTime -lt $newest) { 'TODO' } else { 'DONE' }
    if ($status[$c] -eq 'TODO' -and $upToDate[$c] -and (-not $newest -or $upToDate[$c] -gt $newest)) { $status[$c] = 'DONE' }   # checked up to date on the computer
    $lastRun[$c] = $item.LastWriteTime
}

# --- Agent state: computers launched recently are not launched again for an hour ---
$state = Read-Json $stateFile ([pscustomobject]@{ Launched = [pscustomobject]@{} })
$recent = @{}
foreach ($p in $state.Launched.PSObject.Properties) { if (([datetime]$p.Value) -gt $now.AddMinutes(-60)) { $recent[$p.Name] = [datetime]$p.Value } }

# --- Candidates: due schedules, then the queue ---
$candidates = [ordered]@{}   # computer -> reason
$toQueue    = @()            # computers from a schedule that must wait (offline or outside business hours only)
$schedules  = @($settings.Schedules)
foreach ($s in $schedules) {
    if (-not $s.Enabled) { continue }
    $due = $false
    if ($s.Once) {
        $due = $Force -or (-not $s.LastRun -and $now -ge [datetime]$s.Once)
    } else {
        $at = [datetime]::Today.Add([TimeSpan]::Parse($s.Time))
        $due = $Force -or ((@($s.Days) -contains $now.DayOfWeek.ToString()) -and $now -ge $at -and (-not $s.LastRun -or [datetime]$s.LastRun -lt $at))
    }
    if (-not $due) { continue }
    $wanted = if ($s.Targets -eq 'Selected') {
        @($s.Computers | Where-Object { $status.ContainsKey($_) -and $status[$_] -ne 'DONE' })
    } else {
        $kinds = @('TODO', 'FAILED', 'INCOMPLETE') + $(if ($s.IncludeNever) { 'NEVER' } else { @() })
        @($status.Keys | Where-Object { $kinds -contains $status[$_] } | Sort-Object)
    }
    Write-AgentLog "${tag}Schedule '$($s.Name)' due: $($wanted.Count) computer(s) to do"
    foreach ($c in $wanted) { if (-not $candidates.Contains($c)) { $candidates[$c] = "schedule '$($s.Name)'" } }
    if (-not $dry -and -not $Force) {
        $s.LastRun = $now.ToString('s')
        if ($s.Once) { $s.Enabled = $false }
    }
}

$queue = @(Read-Json $queueFile @() | ForEach-Object { $_ } | Where-Object { $_ -and $_.Computer })
$keptQueue = @()
foreach ($q in $queue) {
    $added = [datetime]$q.Added
    if ($added -lt $now.AddDays(-[int]$settings.Queue.DropAfterDays)) { Write-AgentLog "Queue: $($q.Computer) dropped (waiting since $($added.ToString('yyyy-MM-dd')))"; continue }
    if (-not $status.ContainsKey($q.Computer)) { Write-AgentLog "Queue: $($q.Computer) dropped (not in AD or excluded)"; continue }
    if ($status[$q.Computer] -eq 'DONE' -and $lastRun[$q.Computer] -gt $added) { Write-AgentLog "Queue: $($q.Computer) done, removed"; continue }
    $keptQueue += $q
    $queueAllowed = $settings.Queue.Enabled -and ($settings.Queue.When -eq 'Anytime' -or -not $businessHours)
    if ($queueAllowed -and -not $candidates.Contains($q.Computer)) { $candidates[$q.Computer] = "queue" }
}

# --- Filters: outside-business-hours computers, recently launched, offline, max per pass ---
$final = [ordered]@{}
foreach ($c in @($candidates.Keys)) {
    if ($businessHours -and ($night -contains $c)) {
        if ($candidates[$c] -ne 'queue') { $toQueue += $c }
        Write-AgentLog "${tag}$c : outside business hours only, waiting"; continue
    }
    if ($recent.ContainsKey($c)) { Write-AgentLog "${tag}$c : launched at $($recent[$c].ToString('HH:mm')), skipped"; continue }
    $final[$c] = $candidates[$c]
}

function Test-Reachable([string[]]$Names) {
    $res = @{}; $ips = @{}
    foreach ($n in $Names) {
        $ip = $null
        try { $ip = [Net.Dns]::GetHostAddresses($n) | Where-Object { $_.AddressFamily -eq 'InterNetwork' } | Select-Object -First 1 } catch { }
        if ($ip) { $ips[$n] = $ip } else { $res[$n] = "no DNS" }
    }
    $ips.GetEnumerator() | Group-Object { $_.Value.ToString() } | Where-Object Count -gt 1 | ForEach-Object {
        foreach ($n in @($_.Group | ForEach-Object Key)) { $res[$n] = "duplicate DNS ($($_.Name))"; [void]$ips.Remove($n) }
    }
    $probes = foreach ($kv in @($ips.GetEnumerator())) {
        $cl = New-Object Net.Sockets.TcpClient
        [pscustomobject]@{ Name = $kv.Key; Client = $cl; Task = $cl.ConnectAsync($kv.Value, 445) }
    }
    if ($probes) { Start-Sleep -Milliseconds 2500 }
    foreach ($pr in $probes) {
        $res[$pr.Name] = if ($pr.Task.IsCompleted -and -not $pr.Task.IsFaulted -and $pr.Client.Connected) { "OK" } else { "no answer" }
        $pr.Client.Close()
    }
    $res
}

$launchList = @()
if ($final.Count -gt 0) {
    $reach = Test-Reachable @($final.Keys)
    foreach ($c in @($final.Keys)) {
        if ($reach[$c] -eq 'OK') { $launchList += $c; continue }
        Write-AgentLog "${tag}$c : $($reach[$c]) ($($final[$c]))"
        if ($final[$c] -ne 'queue') { $toQueue += $c }
    }
    # Schedule targets that do not answer: wake them up, the queue picks them up at a next pass
    $toWake = @($final.Keys | Where-Object { $reach[$_] -ne 'OK' -and $final[$_] -ne 'queue' })
    if ($toWake.Count -gt 0 -and $settings.WakeOnLan -ne $false) {
        if ($dry) { Write-AgentLog "[DRY RUN] would send Wake-on-LAN to: $($toWake -join ', ')" }
        else { foreach ($l in @(Send-WakeOnLan $toWake)) { Write-AgentLog "Wake-on-LAN: $l" } }
    }
}
$max = [Math]::Max(1, [int]$settings.MaxPerRun)
if ($launchList.Count -gt $max) {
    Write-AgentLog "${tag}$($launchList.Count) computers ready, limited to $max this pass"
    $launchList = @($launchList | Select-Object -First $max)
}

# --- Schedule targets that must wait go to the queue (when the schedule asks for it) ---
foreach ($c in ($toQueue | Sort-Object -Unique)) {
    $reason = $candidates[$c]
    $src = $schedules | Where-Object { "schedule '$($_.Name)'" -eq $reason } | Select-Object -First 1
    if ($src -and -not $src.QueueIfUnavailable) { continue }
    if (-not ($keptQueue | Where-Object { $_.Computer -eq $c })) {
        $keptQueue += [pscustomobject]@{ Computer = $c; Added = $now.ToString('s'); From = $candidates[$c] }
        Write-AgentLog "${tag}$c : added to the queue (will be deployed when available)"
    }
}

# --- Launch ---
$launched = @()
if ($launchList.Count -gt 0) {
    if ($dry) {
        foreach ($c in $launchList) { Write-AgentLog "[DRY RUN] would launch $c ($($final[$c]))" }
    } elseif ($passwordMissing) {
        Write-AgentLog "ERROR: no saved deployment password ($credFile). Run one 'Deployer maintenant' from the panel first."
    } else {
        $user = $null; $pw = $null
        if ($launchAs -eq 'DeployAccount') {
            $cred = Import-Clixml $credFile
            $user = $cred.UserName; $pw = $cred.GetNetworkCredential().Password
        }
        $opt  = New-PSSessionOption -OpenTimeout 10000 -OperationTimeout 60000
        $res  = @(Invoke-Command -ComputerName $launchList -SessionOption $opt -ThrottleLimit 30 -ErrorAction SilentlyContinue -ErrorVariable remoteErrors -ArgumentList $user, $pw, $launcher -ScriptBlock {
            param($user, $pw, $launcher)
            try {
                $action   = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$launcher`""
                $trigger  = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(20)
                $trigger.EndBoundary = (Get-Date).AddHours(1).ToString('s')
                $settings = New-ScheduledTaskSettingsSet -DeleteExpiredTaskAfter (New-TimeSpan -Minutes 30) -ExecutionTimeLimit (New-TimeSpan -Hours 2) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
                if ($user) {
                    Register-ScheduledTask -TaskName "MAJ-Logiciels-Launch" -Action $action -Trigger $trigger -Settings $settings -User $user -Password $pw -RunLevel Highest -Force -ErrorAction Stop | Out-Null
                } else {
                    # SYSTEM: the computer account reads the share, no password on the workstation
                    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
                    Register-ScheduledTask -TaskName "MAJ-Logiciels-Launch" -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force -ErrorAction Stop | Out-Null
                }
                "OK"
            } catch { "ERROR: $($_.Exception.Message)" }
        })
        $pw = $null
        foreach ($r in $res) {
            if ($r -eq "OK") { $launched += $r.PSComputerName; Write-AgentLog "Launched $($r.PSComputerName) ($($final[$r.PSComputerName]))" }
            else { Write-AgentLog "$($r.PSComputerName) : launch failed - $r" }
        }
        foreach ($e in $remoteErrors) { Write-AgentLog "$($e.TargetObject) : connection failed - $($e.Exception.Message)" }
    }
}

# --- Save: queue (launched computers leave it), state, schedules, status for the panel ---
$keptQueue = @($keptQueue | Where-Object { $launched -notcontains $_.Computer })
if (-not $dry) {
    Save-Json $queueFile $keptQueue
    foreach ($c in $launched) { $recent[$c] = $now }
    Save-Json $stateFile @{ Launched = $recent }
    if (-not $Force) { Save-Json $settingsFile $settings }
}
$msg = if ($dry) { "Mode test : $($launchList.Count) poste(s) seraient lancés" } else { "$($launched.Count) poste(s) lancé(s)" }
Save-Json $statusFile @{ LastPass = $now.ToString('s'); Message = $msg; Launched = @($launched); Ready = @($launchList); Queued = $keptQueue.Count; DryRun = $dry; PasswordMissing = $passwordMissing }
Write-AgentLog "${tag}Pass done: $msg, $($keptQueue.Count) in queue, business hours: $businessHours"