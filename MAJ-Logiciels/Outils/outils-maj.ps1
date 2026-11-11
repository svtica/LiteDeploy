# Click panel for the software update tools (runs on the VM, deployment account).
# Shows every workstation with its deployment status, read from the log copies on deploy$ (no computer contacted).
# Expects the tool scripts in the same folder. Saved as UTF-8 with BOM for the French labels.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$scripts     = $PSScriptRoot
. (Join-Path $PSScriptRoot "config.ps1")   # site settings: $deployRoot, $majBase, $deployAccount
$logShare    = "$deployRoot\Winget-upgrade-logs"
$stateFile   = "$logShare\last-check.json"
$excludeFile = "$logShare\exclude.txt"
$nightFile   = "$logShare\night-only.txt"   # computers to deploy outside business hours only
$agentBase   = $majBase
$agentSettingsFile = "$agentBase\agent-settings.json"   # agent settings (also business hours for the whole program)
$agentQueueFile    = "$agentBase\agent-queue.json"      # "deploy when available" queue
$agentStatusFile   = "$agentBase\agent-status.json"     # last pass of the agent
$agentLogDir       = "$agentBase\Rapports\Agent"
$macCacheFile      = "$agentBase\mac-cache.json"       # MAC and last address of each computer, for Wake-on-LAN (also read by the agent)
$dayNames    = [ordered]@{ Monday = 'Lun'; Tuesday = 'Mar'; Wednesday = 'Mer'; Thursday = 'Jeu'; Friday = 'Ven'; Saturday = 'Sam'; Sunday = 'Dim' }
# $keepReports and $staleDays come from config.json (config.ps1)
$script:tracked = @(Get-TrackedSoftware)   # packages.txt + enabled modules

# Keep only the newest dated report files on the desktop (deploy-todo.txt and other files are never touched)
function Clear-OldReports {
    $desktop = "$agentBase\Rapports\Verification", "$agentBase\Rapports\Inventaire", "$agentBase\PDQ"
    foreach ($pattern in "winget-upgrade-report-*.csv", "deploy-status-*.csv", "deploy-todo-*.txt", "software-inventory-*.csv") {
        Get-ChildItem $desktop -Filter $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -Skip $keepReports |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
}
# --- Agent settings, queue and status (JSON files written by this program and by agent.ps1) ---
function Read-JsonFile([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    try { Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $null }
}
function Save-JsonFile([string]$Path, $Object) { ConvertTo-Json -InputObject $Object -Depth 6 | Set-Content -Path $Path -Encoding UTF8 }
function Get-AgentSettings {
    $s = Read-JsonFile $agentSettingsFile
    if (-not $s) {
        $week = @("Monday", "Tuesday", "Wednesday", "Thursday", "Friday")
        $s = [pscustomobject]@{ Enabled = $false; DryRun = $true; IntervalMinutes = 15; MaxPerRun = 30
             BusinessHours = [pscustomobject]@{ Days = $week; Start = 7; End = 17 }
             Queue = [pscustomobject]@{ Enabled = $true; When = "OutsideBusinessHours"; DropAfterDays = 14 }
             Schedules = @() }
    }
    $s.Schedules = @($s.Schedules | Where-Object { $_ })
    $s
}
function Get-AgentQueue { @(Read-JsonFile $agentQueueFile | ForEach-Object { $_ } | Where-Object { $_ -and $_.Computer }) }
function Test-BusinessHours {
    $bh = $script:agentSettings.BusinessHours
    $n = Get-Date
    (@($bh.Days) -contains $n.DayOfWeek.ToString()) -and ($n.Hour -ge [int]$bh.Start) -and ($n.Hour -lt [int]$bh.End)
}
# Next time a schedule runs, for display
function Get-NextRun($Sch) {
    if (-not $Sch.Enabled) { return $null }
    if ($Sch.Once) { if ($Sch.LastRun) { return $null } else { return [datetime]$Sch.Once } }
    $t = [TimeSpan]::Parse($Sch.Time)
    for ($i = 0; $i -le 7; $i++) {
        $d = (Get-Date).Date.AddDays($i).Add($t)
        if ((@($Sch.Days) -contains $d.DayOfWeek.ToString()) -and $d -gt (Get-Date) -and (-not $Sch.LastRun -or [datetime]$Sch.LastRun -lt $d)) { return $d }
    }
    $null
}
function Format-When($Sch) {
    if ($Sch.Once) { return "le " + ([datetime]$Sch.Once).ToString('yyyy-MM-dd HH:mm') }
    $days = @($dayNames.Keys | Where-Object { @($Sch.Days) -contains $_ } | ForEach-Object { $dayNames[$_] }) -join ' '
    "$days à $($Sch.Time)"
}
$script:agentSettings = Get-AgentSettings

$statusInfo = [ordered]@{
    'FAILED'     = @{ Label = 'Échec';       Order = 0; Color = [System.Drawing.Color]::FromArgb(253, 231, 233) }
    'INCOMPLETE' = @{ Label = 'Incomplet';   Order = 1; Color = [System.Drawing.Color]::FromArgb(229, 241, 251) }
    'TODO'       = @{ Label = 'À faire';     Order = 2; Color = [System.Drawing.Color]::FromArgb(255, 244, 206) }
    'NEVER'      = @{ Label = 'Jamais fait'; Order = 3; Color = [System.Drawing.Color]::FromArgb(240, 240, 240) }
    'DONE'       = @{ Label = 'Fait';        Order = 4; Color = [System.Drawing.Color]::FromArgb(223, 246, 221) }
}

# --- Status of every workstation, from the logs on deploy$ ---
function Get-DeployStatus {
    $searcher = [adsisearcher]$adFilter
    $searcher.PageSize = 1000
    [void]$searcher.PropertiesToLoad.Add('name')
    [void]$searcher.PropertiesToLoad.Add('lastlogontimestamp')
    $script:lastLogon = @{}
    $found = @($searcher.FindAll())
    foreach ($r in $found) {
        if ($r.Properties['lastlogontimestamp'].Count) { $script:lastLogon[[string]$r.Properties['name'][0]] = [datetime]::FromFileTime([long]$r.Properties['lastlogontimestamp'][0]) }
    }
    $names = @($found | ForEach-Object { [string]$_.Properties['name'][0] } | Sort-Object)
    # lastLogon is exact but kept by each domain controller separately: read them all, keep the most recent
    try {
        $nc  = ([adsi]"LDAP://RootDSE").defaultNamingContext
        $dcs = @([System.DirectoryServices.ActiveDirectory.Domain]::GetCurrentDomain().DomainControllers | ForEach-Object { $_.Name })
        foreach ($dc in $dcs) {
            try {
                $ds = New-Object System.DirectoryServices.DirectorySearcher([adsi]"LDAP://$dc/$nc", '(&(objectCategory=computer)(operatingSystem=Windows 1*))')
                $ds.PageSize = 1000; $ds.ClientTimeout = New-TimeSpan -Seconds 10
                [void]$ds.PropertiesToLoad.Add('name'); [void]$ds.PropertiesToLoad.Add('lastlogon')
                foreach ($r in $ds.FindAll()) {
                    if (-not $r.Properties['lastlogon'].Count) { continue }
                    $v = [long]$r.Properties['lastlogon'][0]
                    if ($v -le 0) { continue }
                    $n = [string]$r.Properties['name'][0]; $d = [datetime]::FromFileTime($v)
                    if (-not $script:lastLogon.ContainsKey($n) -or $d -gt $script:lastLogon[$n]) { $script:lastLogon[$n] = $d }
                }
            } catch { }
        }
    } catch { }

    $excludes = @(Get-Content $excludeFile -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    $script:adNames = $names
    $names = @($names | Where-Object { $n = $_; -not ($excludes | Where-Object { $n -like $_ }) })
    $script:excludedCount = $script:adNames.Count - $names.Count
    $script:nightOnly = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    Get-Content $nightFile -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { [void]$script:nightOnly.Add($_) }

    # Newest version date: winget packages (check-updates), newest installers of the enabled modules
    $marks = @()
    try {
        (Get-Content $stateFile -Raw -ErrorAction Stop | ConvertFrom-Json).PSObject.Properties | ForEach-Object {
            if ($_.Value -isnot [string] -and $_.Value.FirstSeen) { $marks += [pscustomobject]@{ What = "$($_.Name) $($_.Value.Version)"; When = [datetime]$_.Value.FirstSeen } }
        }
    } catch { }
    $marks += @(Get-ModuleMarks)
    $script:newest = $marks | Sort-Object When -Descending | Select-Object -First 1
    $script:latest = @{}
    $script:tracked = @(Get-TrackedSoftware)
    $idMap = @{}; foreach ($t in $script:tracked) { if ($t.Id) { $idMap[$t.Id] = $t.Label } }
    try { (Get-Content $stateFile -Raw -ErrorAction Stop | ConvertFrom-Json).PSObject.Properties | ForEach-Object { if ($idMap[$_.Name] -and $_.Value.Version) { $script:latest[$idMap[$_.Name]] = [string]$_.Value.Version } } } catch { }
    foreach ($kv in @((Get-ModuleLatest).GetEnumerator())) { $script:latest[$kv.Key] = $kv.Value }
    $script:agentSettings = Get-AgentSettings
    $script:queue = @{}
    foreach ($q in Get-AgentQueue) { $script:queue[$q.Computer] = $q }
    $script:planned = @{}
    foreach ($sch in @($script:agentSettings.Schedules | Where-Object { $_.Enabled -and $_.Targets -eq 'Selected' })) {
        $nr = Get-NextRun $sch
        foreach ($c in @($sch.Computers)) { if (-not $script:planned.ContainsKey($c)) { $script:planned[$c] = "Planifié : $($sch.Name)$(if ($nr) { ' (' + $nr.ToString('ddd HH:mm') + ')' })" } }
    }

    foreach ($c in $names) {
        $f = Join-Path $logShare "$c.log"
        if (-not (Test-Path $f)) { [pscustomobject]@{ Computer = $c; Status = 'NEVER'; LastRun = $null; Detail = '' }; continue }
        $item  = Get-Item $f
        $lines = @(Get-Content $f -Encoding UTF8)
        $final = $lines | Where-Object { $_ -match 'Launcher: ' } | Select-Object -Last 1
        $exit  = if ($final -match 'exit (\d+)') { [int]$Matches[1] } else { -1 }
        $sum   = ($lines | Where-Object { $_ -match 'Summary:' } | Select-Object -Last 1) -replace '^\S+ \S+ Summary: ', ''
        # A run without "Summary:" did nothing useful, even with exit 0 (older launcher)
        $status = if ($exit -gt 0) { 'FAILED' } elseif ($exit -lt 0 -or -not $sum) { 'INCOMPLETE' }
                  elseif ($script:newest -and $item.LastWriteTime -lt $script:newest.When) { 'TODO' } else { 'DONE' }
        $detail = switch ($status) {
            'TODO'       { "Passage antérieur à $($script:newest.What)" }
            'FAILED'     { "Code $exit - $(if ($sum) { $sum } else { ($lines | Where-Object { $_ -match 'Launcher: ' } | Select-Object -First 1) -replace '^\S+ \S+ Launcher: ', '' })" }
            'INCOMPLETE' { if ($exit -lt 0) { "Pas de ligne finale (en cours, interrompu ou délai dépassé)" } else { "Le travailleur n'a rien écrit (aucun résumé)" } }
            default      { $sum }
        }
        [pscustomobject]@{ Computer = $c; Status = $status; LastRun = $item.LastWriteTime; Detail = $detail }
    }
}

function Start-Tool([string]$Script, [string]$Arguments) {
    $path = Join-Path $scripts $Script
    if (-not (Test-Path $path)) { Show-Info "Script introuvable : $path" "Erreur"; return }
    Start-Process powershell.exe -ArgumentList "-NoExit -ExecutionPolicy Bypass -Command & '$path' $Arguments"
}
# Messages always shown over the active window of the program (never hidden behind a dialog)
function Get-MsgOwner { $a = [System.Windows.Forms.Form]::ActiveForm; if ($a) { $a } else { $form } }
function Show-Info([string]$Text, [string]$Title) { $o = Get-MsgOwner; if ($o) { [System.Windows.Forms.MessageBox]::Show($o, $Text, $Title) | Out-Null } else { [System.Windows.Forms.MessageBox]::Show($Text, $Title) | Out-Null } }

$checked = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$script:rows = @(); $script:newest = $null; $script:loading = $false
$script:sortCol = 1; $script:sortDesc = $false; $script:statusFilter = $null; $script:netFilter = $null
$script:net = @{}; $script:netTime = $null
$script:adNames = @(); $script:excludedCount = 0
$script:queue = @{}; $script:planned = @{}; $script:lastLogon = @{}
$script:idCache = @{}   # identities confirmed while the program is open: name -> address, user/restart info, time
$idCacheHours = 4       # an identity confirmed at the same address less than this many hours ago is not checked again
function Test-Stale([string]$Name) { -not $script:lastLogon.ContainsKey($Name) -or $script:lastLogon[$Name] -lt (Get-Date).AddDays(-$staleDays) }
# Logged-on user and last restart, from the last network check
function Get-HostInfo([string]$Name) { if ($script:net -and $script:net.ContainsKey($Name)) { $script:net[$Name].Info } }
function Format-User([string]$Name) { $i = Get-HostInfo $Name; if (-not $i) { "" } elseif ($i.User) { ($i.User -split '\\')[-1] } else { "personne" } }
# Installed version older than the latest known? ([version] when both parse, otherwise plain difference)
function Test-Older([string]$Installed, [string]$Latest) {
    if (-not $Installed -or -not $Latest) { return $false }
    try { [version]($Installed -replace '[^\d\.]', '') -lt [version]($Latest -replace '[^\d\.]', '') } catch { $Installed -ne $Latest }
}
# Tracked software to update on a computer (from its last identity session); $null when unknown (offline, not checked)
function Get-Outdated([string]$Name) {
    $i = Get-HostInfo $Name
    if (-not $i -or -not $i.Soft) { return $null }
    ,@(foreach ($k in @($i.Soft.Keys | Sort-Object)) { if ($script:latest -and (Test-Older ([string]$i.Soft[$k]) ([string]$script:latest[$k]))) { $k } })   # comma: an empty list must stay a list ("à jour"), not become $null
}
function Format-Outdated([string]$Name) { $o = Get-Outdated $Name; if ($null -eq $o) { "" } elseif ($o.Count) { $o -join ", " } else { "à jour" } }
function Format-Boot([string]$Name) {
    $i = Get-HostInfo $Name
    if (-not $i -or -not $i.Boot) { return "" }
    $h = ((Get-Date) - [datetime]$i.Boot).TotalHours
    $txt = if ($h -lt 1) { "il y a {0} min" -f [int]($h * 60) } elseif ($h -lt 48) { "il y a {0} h" -f [int]$h } else { "il y a {0} j" -f [int]($h / 24) }
    if ($i.Pending) { $txt += " · redémarrage en attente" }
    $txt
}
function Format-LastLogon([string]$Name) {
    if (-not $script:lastLogon.ContainsKey($Name)) { return "jamais" }
    $d = $script:lastLogon[$Name]; $days = ((Get-Date).Date - $d.Date).Days
    if ($days -le 0) { "Aujourd'hui " + $d.ToString('HH:mm') }
    elseif ($days -eq 1) { "Hier " + $d.ToString('HH:mm') }
    elseif ($days -lt 60) { "Il y a $days j" }
    else { "Il y a $([int][Math]::Floor($days / 30)) mois" }
}

# Network state of a computer, from the last network check (DNS, ping, port 445, identity)
function Get-NetLabel([string]$Name) {
    if (-not $script:netTime -or -not $script:net.ContainsKey($Name)) { return "" }
    switch ($script:net[$Name].State) {
        'OK'         { 'En ligne' }
        'UNVERIFIED' { 'En ligne' }
        'NOPING'     { 'Hors ligne' }
        'SMB'        { 'Partage bloqué' }
        'WRONG'      { 'Mauvais poste' }
        'NODNS'      { 'Pas de DNS' }
        default      { [string]$script:net[$Name].State }
    }
}
# Label plus the reason, for the tooltip
function Get-NetTip([string]$Name) {
    $l = Get-NetLabel $Name
    if (-not $l) { return "" }
    $d = $script:net[$Name].Detail
    if ($d) { "$l ($d)" } else { $l }
}

# Full network check, in parallel:
#  1. DNS asked directly to the DNS server (never the VM cache, always fresh)
#  2. ping and TCP 445 (file sharing, used by the deployments) at the same time
#  3. identity: the computer answering must be the right one (WinRM with Kerberos on the computer name)
function Test-NetworkFull([string[]]$Names, [switch]$Full) {
    $res = @{}; $ips = @{}
    $dnsServer = (Get-DnsClientServerAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.ServerAddresses } | Select-Object -First 1).ServerAddresses | Select-Object -First 1
    $done = 0
    foreach ($n in $Names) {
        $done++; if ($done % 5 -eq 0) { Set-LoadProgress ([int](30 * $done / [Math]::Max(1, $Names.Count))) "DNS ($done / $($Names.Count))" }
        # Full name: a DNS server asked directly does not add the domain suffix
        $fqdn = if ($n -notmatch '\.' -and $env:USERDNSDOMAIN) { "$n.$env:USERDNSDOMAIN" } else { $n }
        $ip = $null; $noSuchName = $false
        try {
            $p = @{ Name = $fqdn; Type = 'A'; DnsOnly = $true; QuickTimeout = $true; ErrorAction = 'Stop' }
            if ($dnsServer) { $p.Server = $dnsServer }
            $ip = Resolve-DnsName @p | Where-Object { $_.IPAddress } | Select-Object -First 1 -ExpandProperty IPAddress
        } catch { $noSuchName = $_.FullyQualifiedErrorId -match 'RCODE_NAME_ERROR' }
        # The DNS server said "no such name": a reliable answer, no slow fallback (LLMNR / NetBIOS)
        if (-not $ip -and -not $noSuchName) {
            # Fallback: the usual Windows resolver (may use the VM cache), never worse than before
            try { $ip = [Net.Dns]::GetHostAddresses($n) | Where-Object { $_.AddressFamily -eq 'InterNetwork' } | Select-Object -First 1 | ForEach-Object { $_.IPAddressToString } } catch { }
        }
        if ($ip) { $ips[$n] = $ip } else { $res[$n] = @{ State = 'NODNS'; Detail = "aucun enregistrement" } }
    }
    # Names sharing an address: kept for the tests, the identity check tells which one is really there
    $shared = @{}
    $ips.GetEnumerator() | Group-Object { $_.Value } | Where-Object Count -gt 1 | ForEach-Object {
        $all = @($_.Group | ForEach-Object Key)
        foreach ($n in $all) { $shared[$n] = "adresse $($_.Name) partagée avec " + (($all | Where-Object { $_ -ne $n }) -join ', ') }
    }
    $probes = foreach ($kv in @($ips.GetEnumerator())) {
        $tcp = New-Object Net.Sockets.TcpClient
        $png = New-Object System.Net.NetworkInformation.Ping
        [pscustomobject]@{ Name = $kv.Key; Ip = $kv.Value; Tcp = $tcp; TcpTask = $tcp.ConnectAsync($kv.Value, 445); PingTask = $png.SendPingAsync($kv.Value, 2000) }
    }
    if ($probes) { for ($k = 1; $k -le 5; $k++) { Start-Sleep -Milliseconds 500; Set-LoadProgress (30 + 4 * $k) "Ping et partage de fichiers" } }
    $smbOk = @()
    foreach ($pr in $probes) {
        $tcpOk  = $pr.TcpTask.IsCompleted -and -not $pr.TcpTask.IsFaulted -and $pr.Tcp.Connected
        $pingOk = $pr.PingTask.IsCompleted -and -not $pr.PingTask.IsFaulted -and $pr.PingTask.Result.Status -eq 'Success'
        $pr.Tcp.Close()
        if ($tcpOk) { $smbOk += $pr.Name }
        elseif ($pingOk) { $res[$pr.Name] = @{ State = 'SMB'; Detail = "répond au ping, pas au partage de fichiers" } }
        else { $res[$pr.Name] = @{ State = 'NOPING'; Detail = "$($pr.Ip) ne répond pas" + $(if ($shared[$pr.Name]) { ", $($shared[$pr.Name])" }) } }
    }
    # Identity: Kerberos is issued for the computer name, so another computer at that address is refused (0x80090322)
    # This VM itself: no need to prove who it is (and WinRM to itself needs an elevated session)
    if ($smbOk -contains $env:COMPUTERNAME) {
        $res[$env:COMPUTERNAME] = @{ State = 'OK'; Detail = "ce poste (la VM du programme)" }
        $smbOk = @($smbOk | Where-Object { $_ -ne $env:COMPUTERNAME })
    }
    if (-not $Full) {
        $fromCache = @()
        foreach ($n in $smbOk) {
            $c = $script:idCache[$n]
            if ($c -and $c.Ip -eq $ips[$n] -and $c.When -gt (Get-Date).AddHours(-$idCacheHours)) {
                $res[$n] = @{ State = 'OK'; Info = $c.Info; FromCache = $true; Detail = "identité confirmée à $($c.When.ToString('HH:mm'))" }
                $fromCache += $n
            }
        }
        $smbOk = @($smbOk | Where-Object { $fromCache -notcontains $_ })
    }
    if ($smbOk.Count -gt 0) {
        Get-Job -Name 'MAJ-identity' -ErrorAction SilentlyContinue | Where-Object { $_.State -ne 'Running' } | Remove-Job -Force
        $opt = New-PSSessionOption -OpenTimeout 8000 -OperationTimeout 12000
        # Identity session also reports: logged-on user, last restart, pending restart, physical MAC addresses (for Wake-on-LAN)
        $idRules = @($script:tracked | Where-Object { $_.Detect } | ForEach-Object { [pscustomobject]@{ Label = $_.Label; Detect = $_.Detect } })
        $idBlock = {
            param($rules)   # tracked software: Label + Detect (regex on DisplayName, group 1 = version)
            $cs = Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 5 -ErrorAction SilentlyContinue
            $os = Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5 -ErrorAction SilentlyContinue
            $pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
                       (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
            $macs = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.MacAddress } | ForEach-Object { $_.MacAddress })
            $soft = @{}
            $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
            foreach ($u in @(Get-ItemProperty $keys -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName })) {
                $n = [string]$u.DisplayName; $v = [string]$u.DisplayVersion; $k = $null
                foreach ($r in $rules) { if ($n -match $r.Detect) { $k = $r.Label; if ($Matches[1]) { $v = [string]$Matches[1] }; break } }
                if (-not $k -or -not $v) { continue }
                $keep = $true
                if ($soft[$k]) { try { $keep = [version]($v -replace '[^\d\.]', '') -gt [version]($soft[$k] -replace '[^\d\.]', '') } catch { $keep = $false } }
                if ($keep) { $soft[$k] = $v }
            }
            [pscustomobject]@{ Name = $env:COMPUTERNAME; User = [string]$cs.UserName; Boot = $os.LastBootUpTime; Pending = [bool]$pending; Macs = $macs; Soft = $soft }
        }
        $job = Invoke-Command -ComputerName $smbOk -SessionOption $opt -ThrottleLimit 50 -AsJob -JobName "MAJ-identity" -ScriptBlock $idBlock -ArgumentList (,$idRules)
        $total = @($job.ChildJobs).Count; $t0 = Get-Date
        while ($job.State -eq 'Running' -and ((Get-Date) - $t0).TotalSeconds -lt 30) {
            $finished = @($job.ChildJobs | Where-Object { $_.State -notin 'Running', 'NotStarted' }).Count
            Set-LoadProgress (50 + [int](40 * $finished / [Math]::Max(1, $total))) "Identité des postes ($finished / $total)"
            Start-Sleep -Milliseconds 300
        }
        $who = @(); $idErrors = @()
        foreach ($cj in $job.ChildJobs) {
            if ($cj.State -eq 'Completed') { $who += @(Receive-Job $cj -ErrorAction SilentlyContinue) }
            elseif ($cj.State -eq 'Failed') { $idErrors += [pscustomobject]@{ TargetObject = $cj.Location; Exception = $(if ($cj.JobStateInfo.Reason) { $cj.JobStateInfo.Reason } else { [pscustomobject]@{ Message = "échec" } }) } }
        }
        if ($job.State -ne 'Running') { Remove-Job $job -Force }   # still running after 30 s: left behind without waiting (triangle), cleaned later
        foreach ($w in $who) {
            $asked = [string]$w.PSComputerName
            if ([string]$w.Name -eq $asked) { $res[$asked] = @{ State = 'OK'; Info = $w; Detail = $(if ($shared[$asked]) { "identité confirmée, $($shared[$asked])" } else { "" }) } }
            else { $res[$asked] = @{ State = 'WRONG'; Detail = "l'adresse $($ips[$asked]) appartient à $($w.Name)" } }
        }
        foreach ($e in $idErrors) {
            $asked = [string]$e.TargetObject
            if (-not $asked -or $res.ContainsKey($asked)) { continue }
            if ($e.Exception.Message -match '0x80090322') { $res[$asked] = @{ State = 'WRONG'; Detail = "l'appareil à l'adresse $($ips[$asked]) n'est pas $asked" + $(if ($shared[$asked]) { " ($($shared[$asked]))" }) } }
            else {
                $msg = ($e.Exception.Message -replace '\s+', ' ').Trim()
                $why = if ($msg -match "ne peut pas terminer l.op|cannot complete the operation") { "WinRM n'a pas répondu à temps (poste lent, en veille ou pare-feu)" }
                       elseif ($msg -match "accès refusé|access is denied") { "WinRM : accès refusé" }
                       else { "WinRM : " + $msg.Substring(0, [Math]::Min(160, $msg.Length)) }
                $res[$asked] = @{ State = 'UNVERIFIED'; Detail = "joignable, identité non vérifiée - $why" }
            }
        }
        foreach ($n in $smbOk) { if (-not $res.ContainsKey($n)) { $res[$n] = @{ State = 'UNVERIFIED'; Detail = "joignable, identité non vérifiée - WinRM n'a rien répondu dans le délai" } } }
        # Second try, longer, only for the ones not verified (a laptop on Wi-Fi or waking up can be slow the first time)
        $retry = @($smbOk | Where-Object { $res[$_].State -eq 'UNVERIFIED' })
        if ($retry.Count -gt 0) {
            $opt2 = New-PSSessionOption -OpenTimeout 15000 -OperationTimeout 20000
            $job2 = Invoke-Command -ComputerName $retry -SessionOption $opt2 -ThrottleLimit 50 -AsJob -JobName "MAJ-identity" -ScriptBlock $idBlock -ArgumentList (,$idRules)
            $t1 = Get-Date
            while ($job2.State -eq 'Running' -and ((Get-Date) - $t1).TotalSeconds -lt 20) {
                $left = [int](20 - ((Get-Date) - $t1).TotalSeconds)
                Set-LoadProgress (92 + [int](7 * (20 - $left) / 20)) "2e essai pour $($retry.Count) poste(s) - $left s au plus"
                Start-Sleep -Milliseconds 300
            }
            $who2 = @($job2.ChildJobs | Where-Object { $_.State -eq 'Completed' } | ForEach-Object { Receive-Job $_ -ErrorAction SilentlyContinue })
            if ($job2.State -ne 'Running') { Remove-Job $job2 -Force }   # still running after 20 s: left behind without waiting (triangle), cleaned later
            foreach ($w in $who2) {
                $asked = [string]$w.PSComputerName
                if ([string]$w.Name -eq $asked) { $res[$asked] = @{ State = 'OK'; Info = $w; Detail = "identité confirmée au 2e essai" } }
                else { $res[$asked] = @{ State = 'WRONG'; Detail = "l'adresse $($ips[$asked]) appartient à $($w.Name)" } }
            }
        }
    }
    # This VM itself: same information (user, restart, software, MAC), read locally without a remote session
    if ($res[$env:COMPUTERNAME] -and $res[$env:COMPUTERNAME].Detail -eq "ce poste (la VM du programme)" -and $idBlock) {
        try { $res[$env:COMPUTERNAME].Info = & $idBlock $idRules } catch { }
    }
    foreach ($k in @($res.Keys)) {
        $res[$k].Ip = $ips[$k]
        if ($res[$k].State -eq 'OK' -and $res[$k].Info -and -not $res[$k].FromCache) { $script:idCache[$k] = @{ Ip = $ips[$k]; Info = $res[$k].Info; When = Get-Date } }
        elseif ($res[$k].State -ne 'OK') { [void]$script:idCache.Remove($k) }
    }
    $res
}

# --- Form ---
$form = New-Object System.Windows.Forms.Form
$form.Text            = "Mises à jour des logiciels" + $(if ($orgName) { " – $orgName" })
$form.ClientSize      = New-Object System.Drawing.Size(940, 680)
$form.StartPosition   = "CenterScreen"
$form.FormBorderStyle = "Sizable"
$form.MaximizeBox     = $true
$form.Font            = New-Object System.Drawing.Font("Segoe UI", 9.5)
$tips = New-Object System.Windows.Forms.ToolTip

# --- Top: summary per status ---
$summary = @{}
$x = 15
foreach ($k in $statusInfo.Keys) {
    $l = New-Object System.Windows.Forms.Label
    $l.Location = New-Object System.Drawing.Point($x, 12); $l.Size = New-Object System.Drawing.Size(110, 26)
    $l.TextAlign = "MiddleCenter"; $l.BackColor = $statusInfo[$k].Color; $l.BorderStyle = "FixedSingle"
    $form.Controls.Add($l); $summary[$k] = $l
    $l.Tag = $k; $l.Cursor = [System.Windows.Forms.Cursors]::Hand
    $l.Add_Click({ Set-StatusFilter $this.Tag })
    $tips.SetToolTip($l, "Cliquez pour n'afficher que cet état, cliquez de nouveau pour tout afficher")
    $x += 117
}
# Queue box: computers waiting in the "deploy when available" queue (click: show only them)
$lblQueue = New-Object System.Windows.Forms.Label
$lblQueue.Location = New-Object System.Drawing.Point($x, 12); $lblQueue.Size = New-Object System.Drawing.Size(105, 26)
$lblQueue.TextAlign = "MiddleCenter"; $lblQueue.BackColor = [System.Drawing.Color]::FromArgb(214, 240, 236); $lblQueue.BorderStyle = "FixedSingle"
$lblQueue.Text = "En file : 0"; $lblQueue.Tag = 'QUEUE'; $lblQueue.Cursor = [System.Windows.Forms.Cursors]::Hand
$form.Controls.Add($lblQueue)
$lblQueue.Add_Click({ Set-StatusFilter $this.Tag })
$tips.SetToolTip($lblQueue, "Postes en file d'attente « dès que disponible ». Cliquez pour n'afficher qu'eux, cliquez de nouveau pour tout afficher")
# Legend for computers to deploy outside business hours only
$nightFore = [System.Drawing.Color]::FromArgb(91, 44, 160)
$nightBack = [System.Drawing.Color]::FromArgb(230, 222, 250)
$nightFont = New-Object System.Drawing.Font("Segoe UI", 9.5, [System.Drawing.FontStyle]::Bold)
$lblNight = New-Object System.Windows.Forms.Label
$lblNight.Location = New-Object System.Drawing.Point(713, 12); $lblNight.Size = New-Object System.Drawing.Size(212, 26)
$lblNight.TextAlign = "MiddleCenter"; $lblNight.BackColor = $nightBack; $lblNight.ForeColor = $nightFore; $lblNight.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$lblNight.BorderStyle = "FixedSingle"; $lblNight.Text = "Violet : hors heures ouvrables"
$form.Controls.Add($lblNight)
$lblNight.Tag = 'NIGHT'; $lblNight.Cursor = [System.Windows.Forms.Cursors]::Hand
$lblNight.Add_Click({ Set-StatusFilter $this.Tag })
$tips.SetToolTip($lblNight, "Cliquez pour n'afficher que les postes hors heures ouvrables, cliquez de nouveau pour tout afficher")

$lblRef = New-Object System.Windows.Forms.Label
$lblRef.Location = New-Object System.Drawing.Point(15, 44); $lblRef.Size = New-Object System.Drawing.Size(560, 20); $lblRef.ForeColor = [System.Drawing.Color]::DimGray
$form.Controls.Add($lblRef)
$tips.SetToolTip($lblRef, "Un poste est « Fait » si son dernier passage réussi est plus récent que cette version.")
$lblAgent = New-Object System.Windows.Forms.Label
$lblAgent.Location = New-Object System.Drawing.Point(580, 44); $lblAgent.Size = New-Object System.Drawing.Size(345, 20); $lblAgent.TextAlign = "MiddleRight"
$lblAgent.Cursor = [System.Windows.Forms.Cursors]::Hand
$form.Controls.Add($lblAgent)
$tips.SetToolTip($lblAgent, "Cliquez pour ouvrir le journal de l'agent")
$lblAgent.Add_Click({
    $l = Join-Path $agentLogDir ("agent-{0}.log" -f (Get-Date -Format 'yyyy-MM'))
    if (Test-Path $l) { Start-Process notepad.exe -ArgumentList "`"$l`"" } else { Show-Info "Pas encore de journal de l'agent." "Agent" }
})
function Update-AgentStatus {
    $st = Read-JsonFile $agentStatusFile
    $s  = $script:agentSettings
    if (-not $s.Enabled) { $lblAgent.Text = "Agent : désactivé"; $lblAgent.ForeColor = [System.Drawing.Color]::DimGray; return }
    $mode = if ($s.DryRun) { "actif en mode test" } else { "actif" }
    $last = if ($st -and $st.LastPass) { " · passage " + ([datetime]$st.LastPass).ToString('HH:mm') } else { " · aucun passage" }
    $lblAgent.Text = "Agent : $mode$last · $($script:queue.Count) en file"
    $lblAgent.ForeColor = $(if ($s.DryRun) { [System.Drawing.Color]::DarkOrange } else { [System.Drawing.Color]::FromArgb(16, 124, 16) })
    if ($st -and $st.PasswordMissing) { $lblAgent.Text += " · MOT DE PASSE MANQUANT"; $lblAgent.ForeColor = [System.Drawing.Color]::FromArgb(196, 43, 28) }
}

# --- Left: computer table ---
$lblFilter = New-Object System.Windows.Forms.Label
$lblFilter.Text = "Filtrer :"; $lblFilter.Location = New-Object System.Drawing.Point(15, 74); $lblFilter.AutoSize = $true
$form.Controls.Add($lblFilter)
$txtFilter = New-Object System.Windows.Forms.TextBox
$txtFilter.Location = New-Object System.Drawing.Point(75, 71); $txtFilter.Size = New-Object System.Drawing.Size(200, 25)
$form.Controls.Add($txtFilter)

$lv = New-Object System.Windows.Forms.ListView
$lv.Location = New-Object System.Drawing.Point(15, 102); $lv.Size = New-Object System.Drawing.Size(580, 528)
$lv.View = "Details"; $lv.CheckBoxes = $true; $lv.FullRowSelect = $true; $lv.GridLines = $true; $lv.HideSelection = $false
# Green / red dots shown before the computer name
function New-Dot([System.Drawing.Color]$Color) {
    $bmp = New-Object System.Drawing.Bitmap 16, 16
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $Color), 3, 3, 10, 10)
    $g.Dispose()
    $bmp
}
$dots = New-Object System.Windows.Forms.ImageList
$dots.ImageSize = New-Object System.Drawing.Size(16, 16)
$dots.Images.Add((New-Dot ([System.Drawing.Color]::FromArgb(16, 160, 16))))   # 0 = online
$dots.Images.Add((New-Dot ([System.Drawing.Color]::FromArgb(210, 40, 30))))   # 1 = not reachable
# Yellow triangle with a dark outline: a different shape, readable without colors
function New-Warning {
    $bmp = New-Object System.Drawing.Bitmap 16, 16
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $pts = [System.Drawing.Point[]]@((New-Object System.Drawing.Point 8, 1), (New-Object System.Drawing.Point 15, 14), (New-Object System.Drawing.Point 1, 14))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 205, 0))), $pts)
    $g.DrawPolygon((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 70, 0)), 1), $pts)
    $g.Dispose()
    $bmp
}
$dots.Images.Add((New-Warning))   # 2 = reachable, identity not verified (yellow triangle)
$lv.SmallImageList   = $dots
$lv.ShowItemToolTips = $true
[void]$lv.Columns.Add("Poste", 175)
[void]$lv.Columns.Add("État", 95)
[void]$lv.Columns.Add("Dernière connexion", 125)
[void]$lv.Columns.Add("Dernier passage", 120)
[void]$lv.Columns.Add("Détail", 180)
[void]$lv.Columns.Add("Remarque", 200)
[void]$lv.Columns.Add("Usager", 120)
[void]$lv.Columns.Add("Redémarré", 170)
[void]$lv.Columns.Add("À mettre à jour", 170)
$lv.AllowColumnReorder = $true
$script:colDefaults = @($lv.Columns | ForEach-Object { $_.Width })
$script:hiddenCols = New-Object 'System.Collections.Generic.HashSet[int]'
$script:autoFill = $true   # "Remarque" fills the free width until the user has a saved layout
$form.Controls.Add($lv)
# "Loading" message shown over the table while the program reads the AD, the logs or the network
$lblLoading = New-Object System.Windows.Forms.Label
$lblLoading.Location = New-Object System.Drawing.Point(15, 102); $lblLoading.Size = New-Object System.Drawing.Size(580, 528)
$lblLoading.TextAlign = "MiddleCenter"; $lblLoading.BackColor = [System.Drawing.Color]::White; $lblLoading.ForeColor = [System.Drawing.Color]::DimGray
$lblLoading.Font = New-Object System.Drawing.Font("Segoe UI", 13); $lblLoading.BorderStyle = "FixedSingle"; $lblLoading.Visible = $false
$form.Controls.Add($lblLoading)
function Show-Loading([string]$Text) {
    $lblLoading.Text = $Text; $lblLoading.Visible = $true; $lblLoading.BringToFront()
    $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    $form.Refresh()
}
function Hide-Loading { $lblLoading.Visible = $false; $pbLoad.Visible = $false; $form.Cursor = [System.Windows.Forms.Cursors]::Default }
$pbLoad = New-Object System.Windows.Forms.ProgressBar
$pbLoad.Size = New-Object System.Drawing.Size(360, 20); $pbLoad.Visible = $false
$form.Controls.Add($pbLoad)
# Progress of the network check, with the step in progress
function Set-LoadProgress([int]$Pct, [string]$Step) {
    $Pct = [Math]::Max(0, [Math]::Min(100, $Pct))
    $lblLoading.Text = "Vérification du réseau... $Pct %`r`n`r`n$Step`r`n`r`n"
    $pbLoad.Location = New-Object System.Drawing.Point(($lblLoading.Left + [int](($lblLoading.Width - $pbLoad.Width) / 2)), ($lblLoading.Top + [int]($lblLoading.Height / 2) + 45))
    $pbLoad.Value = $Pct; $pbLoad.Visible = $true; $pbLoad.BringToFront()
    $form.Refresh()
}
$tips.SetToolTip($lv, "Cliquez sur un en-tête pour trier, double-cliquez sur un poste pour ouvrir sa fiche")

$lblCount = New-Object System.Windows.Forms.Label
$lblCount.Location = New-Object System.Drawing.Point(15, 636); $lblCount.Size = New-Object System.Drawing.Size(255, 40)
$form.Controls.Add($lblCount)

function Update-Count {
    $f = if (-not $script:statusFilter) { "" } elseif ($script:statusFilter -eq 'NIGHT') { " - filtre : hors heures ouvrables" } elseif ($script:statusFilter -eq 'QUEUE') { " - filtre : en file d'attente" } elseif ($script:statusFilter -eq 'STALE') { " - filtre : inactifs" } else { " - filtre : $($statusInfo[$script:statusFilter].Label)" }
    if ($script:netFilter) { $f += $(if ($script:netFilter -eq 'ONLINE') { " · en ligne" } else { " · hors ligne" }) }
    $lblCount.Text = "$($checked.Count) poste(s) coché(s)$f"
}

# Click on a summary box: show only that status (click again: show all)
function Set-StatusFilter([string]$Key) {
    $script:statusFilter = if ($script:statusFilter -eq $Key) { $null } else { $Key }
    foreach ($k in $summary.Keys) { $summary[$k].BorderStyle = $(if ($script:statusFilter -eq $k) { "Fixed3D" } else { "FixedSingle" }) }
    $lblNight.BorderStyle = $(if ($script:statusFilter -eq 'NIGHT') { "Fixed3D" } else { "FixedSingle" })
    $lblQueue.BorderStyle = $(if ($script:statusFilter -eq 'QUEUE') { "Fixed3D" } else { "FixedSingle" })
    $lblStale.BorderStyle = $(if ($script:statusFilter -eq 'STALE') { "Fixed3D" } else { "FixedSingle" })
    Update-List
}

function Update-List {
    $sortKey = switch ($script:sortCol) {
        0 { { $_.Computer } }
        1 { { $statusInfo[$_.Status].Order } }
        2 { { if ($script:lastLogon.ContainsKey($_.Computer)) { $script:lastLogon[$_.Computer] } else { [datetime]::MinValue } } }
        3 { { if ($_.LastRun) { $_.LastRun } else { [datetime]::MinValue } } }
        5 { { if ($script:nightOnly -and $script:nightOnly.Contains($_.Computer)) { 0 } else { 1 } } }
        6 { { Format-User $_.Computer } }
        7 { { $i = Get-HostInfo $_.Computer; if ($i -and $i.Boot) { [datetime]$i.Boot } else { [datetime]::MaxValue } } }
        8 { { $o = Get-Outdated $_.Computer; if ($null -eq $o) { -1 } else { $o.Count } } }
        default { { $_.Detail } }
    }
    $list = @($script:rows | Where-Object { $_.Computer -like "*$($txtFilter.Text)*" } |
              Where-Object { -not $script:statusFilter -or $_.Status -eq $script:statusFilter -or
                             ($script:statusFilter -eq 'NIGHT' -and $script:nightOnly -and $script:nightOnly.Contains($_.Computer)) -or
                             ($script:statusFilter -eq 'QUEUE' -and $script:queue.ContainsKey($_.Computer)) -or
                             ($script:statusFilter -eq 'STALE' -and (Test-Stale $_.Computer)) } |
              Where-Object { -not $script:netFilter -or
                             ($script:netFilter -eq 'ONLINE' -and (Get-NetLabel $_.Computer) -eq 'En ligne') -or
                             ($script:netFilter -eq 'OFFLINE' -and (Get-NetLabel $_.Computer) -and (Get-NetLabel $_.Computer) -ne 'En ligne') } |
              Sort-Object -Property @{ Expression = $sortKey; Descending = $script:sortDesc }, Computer)
    $script:loading = $true
    $lv.BeginUpdate(); $lv.Items.Clear()
    foreach ($r in $list) {
        $it = New-Object System.Windows.Forms.ListViewItem($r.Computer)
        [void]$it.SubItems.Add($statusInfo[$r.Status].Label)
        [void]$it.SubItems.Add((Format-LastLogon $r.Computer))
        [void]$it.SubItems.Add($(if ($r.LastRun) { $r.LastRun.ToString('yyyy-MM-dd HH:mm') } else { '-' }))
        [void]$it.SubItems.Add([string]$r.Detail)
        $notes = @()
        if ($script:nightOnly -and $script:nightOnly.Contains($r.Computer)) { $notes += "En dehors des heures ouvrables" }
        if ($script:queue.ContainsKey($r.Computer)) {
            $days = [int][Math]::Floor(((Get-Date) - [datetime]$script:queue[$r.Computer].Added).TotalDays)
            $left = [int]$script:agentSettings.Queue.DropAfterDays - $days
            $notes += $(if ($days -lt 1) { "En file depuis aujourd'hui" } else { "En file depuis $days j" }) + $(if ($left -le 2) { " (retrait dans $([Math]::Max(0, $left)) j)" })
        }
        if ($script:planned.ContainsKey($r.Computer)) { $notes += $script:planned[$r.Computer] }
        [void]$it.SubItems.Add($notes -join " · ")
        [void]$it.SubItems.Add((Format-User $r.Computer))
        [void]$it.SubItems.Add((Format-Boot $r.Computer))
        [void]$it.SubItems.Add((Format-Outdated $r.Computer))
        $it.BackColor = $statusInfo[$r.Status].Color
        # Network dot before the name: green = online, red = not reachable (reason in the tooltip)
        $netLabel = Get-NetLabel $r.Computer
        if ($netLabel) {
            $it.ImageIndex  = $(if ($netLabel -ne 'En ligne') { 1 } elseif ($script:net[$r.Computer].State -eq 'UNVERIFIED') { 2 } else { 0 })
            $it.ToolTipText = "$($r.Computer) : $(Get-NetTip $r.Computer)"
        }
        $it.UseItemStyleForSubItems = $false
        foreach ($sub in $it.SubItems) { $sub.BackColor = $statusInfo[$r.Status].Color }
        if (Test-Stale $r.Computer) { $it.SubItems[2].ForeColor = [System.Drawing.Color]::FromArgb(196, 43, 28) }
        $hi = Get-HostInfo $r.Computer
        if ($hi -and ($hi.Pending -or ($hi.Boot -and [datetime]$hi.Boot -lt (Get-Date).AddDays(-7)))) { $it.SubItems[7].ForeColor = [System.Drawing.Color]::FromArgb(196, 43, 28) }
        if ($script:lastLogon.ContainsKey($r.Computer)) { $it.ToolTipText = ("{0} : dernière connexion au domaine le {1}" -f $r.Computer, $script:lastLogon[$r.Computer].ToString('yyyy-MM-dd HH:mm')) + $(if ($netLabel) { " · $(Get-NetTip $r.Computer)" }) }
        if ($script:nightOnly -and $script:nightOnly.Contains($r.Computer)) {
            # Outside business hours only: name in violet bold, Remarque cell in lavender (row keeps its status color)
            $it.SubItems[0].ForeColor = $nightFore
            $it.SubItems[0].Font      = $nightFont
            $it.SubItems[5].BackColor = $nightBack
            $it.SubItems[5].ForeColor = $nightFore
        }
        $it.Checked = $checked.Contains($r.Computer)
        Set-RowProgress $it
        [void]$lv.Items.Add($it)
    }
    $lv.EndUpdate()
    $script:loading = $false
    Update-Count
}

function Update-Summary {
    foreach ($k in $statusInfo.Keys) {
        $n = @($script:rows | Where-Object Status -eq $k).Count
        $summary[$k].Text = "$($statusInfo[$k].Label) : $n"
    }
}
# "To do" only because of the date, but every tracked software read on the computer is up to date: "Fait".
# Also written to uptodate.json, so the agent does not launch the package there for nothing.
function Apply-SoftStatus {
    $now = Get-Date; $ok = @{}
    foreach ($r in $script:rows) {
        if (-not $r.PSObject.Properties['BaseStatus']) {
            $r | Add-Member -NotePropertyName BaseStatus -NotePropertyValue $r.Status
            $r | Add-Member -NotePropertyName BaseDetail -NotePropertyValue $r.Detail
        }
        $r.Status = $r.BaseStatus; $r.Detail = $r.BaseDetail
        if ($r.BaseStatus -ne 'TODO') { continue }
        $o = Get-Outdated $r.Computer
        if ($null -ne $o -and $o.Count -eq 0) {
            $when = if ($script:idCache -and $script:idCache[$r.Computer]) { [datetime]$script:idCache[$r.Computer].When } else { $now }
            $r.Status = 'DONE'; $r.Detail = "à jour (vérifié sur le poste à $($when.ToString('HH:mm')))"
            $ok[$r.Computer] = $when.ToString('s')
        }
    }
    if ($ok.Count) {
        $file = "$agentBase\uptodate.json"; $all = @{}
        $old = Read-JsonFile $file
        if ($old) { foreach ($p in $old.PSObject.Properties) { $all[$p.Name] = $p.Value } }
        foreach ($k in $ok.Keys) { $all[$k] = $ok[$k] }
        try { Save-JsonFile $file $all } catch { }
    }
    Update-Summary
}
function Update-Data {
    Show-Loading "Actualisation des postes en cours...`r`n`r`nLecture de l'Active Directory et des journaux de déploiement"
    try { $script:rows = @(Get-DeployStatus) } catch { Hide-Loading; Show-Info "Lecture impossible : $($_.Exception.Message)" "Erreur"; $script:rows = @() }
    Hide-Loading
    Apply-SoftStatus
    $lblRef.Text = if ($script:newest) {
        "Référence : $($script:newest.What) (vue le $($script:newest.When.ToString('yyyy-MM-dd HH:mm')))"
    } else { "Aucune version de référence : cliquez une fois sur « Nouvelles versions sur le web »." }
    if ($script:excludedCount) { $lblRef.Text += " · $($script:excludedCount) exclu(s)" }
    $lblQueue.Text = "En file : $($script:queue.Count)"
    $lblStale.Text = "Inactifs : " + @($script:rows | Where-Object { Test-Stale $_.Computer }).Count
    Update-AgentStatus
    Update-List
}

$lv.Add_ItemChecked({
    param($sender, $e)
    if ($script:loading) { return }
    if ($e.Item.Checked) { [void]$checked.Add($e.Item.Text) } else { [void]$checked.Remove($e.Item.Text) }
    Update-Count
})
$lv.Add_ColumnClick({
    param($sender, $e)
    if ($script:sortCol -eq $e.Column) { $script:sortDesc = -not $script:sortDesc } else { $script:sortCol = $e.Column; $script:sortDesc = $false }
    Update-List
})
$txtFilter.Add_TextChanged({ Update-List })
# Computer card: everything known about one computer, with its log
function Show-HostCard([string]$Name) {
    $r  = $script:rows | Where-Object Computer -eq $Name | Select-Object -First 1
    $n  = if ($script:net -and $script:net.ContainsKey($Name)) { $script:net[$Name] } else { $null }
    $hi = Get-HostInfo $Name
    $L  = New-Object System.Collections.Generic.List[string]
    $L.Add("POSTE : $Name"); $L.Add("")
    $L.Add("État du déploiement : $(if ($r) { $statusInfo[$r.Status].Label } else { '-' })")
    $L.Add("Détail              : $(if ($r) { $r.Detail })")
    $L.Add("Dernier passage     : $(if ($r -and $r.LastRun) { $r.LastRun.ToString('yyyy-MM-dd HH:mm') } else { '-' })")
    $L.Add("Dernière connexion  : $(if ($script:lastLogon.ContainsKey($Name)) { $script:lastLogon[$Name].ToString('yyyy-MM-dd HH:mm') } else { 'jamais' })")
    $L.Add("Réseau              : $(if ($n) { Get-NetTip $Name } else { 'pas encore vérifié' })")
    $L.Add("Adresse IP          : $(if ($n -and $n.Ip) { $n.Ip } else { '-' })")
    $L.Add("Usager connecté     : $(if ($hi) { if ($hi.User) { $hi.User } else { 'personne' } } else { '-' })")
    $L.Add("Dernier redémarrage : $(if ($hi -and $hi.Boot) { ([datetime]$hi.Boot).ToString('yyyy-MM-dd HH:mm') + ' (' + (Format-Boot $Name) + ')' } else { '-' })")
    $L.Add(""); $L.Add("LOGICIELS SUIVIS (installé -> dernière version connue)")
    if ($hi -and $hi.Soft) {
        foreach ($k in @($script:tracked | ForEach-Object { $_.Label })) {
            $iv = [string]$hi.Soft[$k]; $lv8 = if ($script:latest) { [string]$script:latest[$k] } else { "" }
            $state = if (-not $iv) { "non installé" } elseif (Test-Older $iv $lv8) { "À METTRE À JOUR" } elseif ($lv8) { "à jour" } else { "dernière version inconnue" }
            $L.Add(("  {0,-10} {1,-16} -> {2,-16} {3}" -f $k, $(if ($iv) { $iv } else { "-" }), $(if ($lv8) { $lv8 } else { "?" }), $state))
        }
    } else { $L.Add("  (pas d'information : poste hors ligne ou identité non vérifiée)") }
    $L.Add("")
    $mc = Read-JsonFile $macCacheFile
    $L.Add("Adresses MAC        : $(if ($mc -and $mc.$Name) { @($mc.$Name.Macs) -join ', ' } else { '-' })")
    $rem = @()
    if ($script:nightOnly -and $script:nightOnly.Contains($Name)) { $rem += "en dehors des heures ouvrables" }
    if ($script:queue.ContainsKey($Name)) { $rem += "en file d'attente depuis le $(([datetime]$script:queue[$Name].Added).ToString('yyyy-MM-dd HH:mm'))" }
    if ($script:planned.ContainsKey($Name)) { $rem += $script:planned[$Name] }
    $L.Add("Remarques           : $(if ($rem) { $rem -join ' · ' } else { '-' })")
    # Installed versions from the last fleet verification report, whatever its columns are
    $rep = Get-ChildItem "$agentBase\Rapports\Verification" -Filter "winget-upgrade-report-*.csv" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($rep) {
        $rows = @(Import-Csv $rep.FullName -ErrorAction SilentlyContinue | Where-Object { $_.PSObject.Properties.Value -contains $Name })
        $L.Add(""); $L.Add("DERNIÈRE VÉRIFICATION DES VERSIONS ($($rep.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))")
        if ($rows.Count) { foreach ($row in $rows) { $L.Add("  " + (($row.PSObject.Properties | Where-Object { $_.Value -ne $Name } | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' | ')) } }
        else { $L.Add("  (poste absent de ce rapport)") }
    }
    $log = Join-Path $logShare "$Name.log"
    $L.Add(""); $L.Add("FIN DU JOURNAL DE DÉPLOIEMENT")
    if (Test-Path $log) { Get-Content $log -Tail 15 -Encoding UTF8 | ForEach-Object { $L.Add("  $_") } } else { $L.Add("  (aucun journal : jamais déployé)") }

    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Fiche du poste - $Name"; $dlg.ClientSize = New-Object System.Drawing.Size(820, 560); $dlg.StartPosition = "CenterParent"; $dlg.Font = $form.Font
    $tb = New-Object System.Windows.Forms.TextBox
    $tb.Multiline = $true; $tb.ReadOnly = $true; $tb.ScrollBars = "Both"; $tb.WordWrap = $false
    $tb.Font = New-Object System.Drawing.Font("Consolas", 9.5); $tb.Location = New-Object System.Drawing.Point(12, 12); $tb.Size = New-Object System.Drawing.Size(796, 490)
    $tb.Anchor = "Top, Bottom, Left, Right"; $tb.Text = ($L -join "`r`n")
    $dlg.Controls.Add($tb)
    $bLog = New-Object System.Windows.Forms.Button; $bLog.Text = "Ouvrir le journal"; $bLog.Location = New-Object System.Drawing.Point(12, 515); $bLog.Size = New-Object System.Drawing.Size(160, 32); $bLog.Anchor = "Bottom, Left"
    $bLog.Add_Click({ if (Test-Path $log) { Start-Process notepad.exe -ArgumentList "`"$log`"" } else { Show-Info "Aucun journal pour $Name." "Journal" } })
    $bCopy = New-Object System.Windows.Forms.Button; $bCopy.Text = "Copier"; $bCopy.Location = New-Object System.Drawing.Point(182, 515); $bCopy.Size = New-Object System.Drawing.Size(100, 32); $bCopy.Anchor = "Bottom, Left"
    $bCopy.Add_Click({ [System.Windows.Forms.Clipboard]::SetText($tb.Text) })
    $bClose = New-Object System.Windows.Forms.Button; $bClose.Text = "Fermer"; $bClose.Location = New-Object System.Drawing.Point(708, 515); $bClose.Size = New-Object System.Drawing.Size(100, 32); $bClose.Anchor = "Bottom, Right"
    $bClose.Add_Click({ $dlg.Close() })
    $dlg.Controls.AddRange(@($bLog, $bCopy, $bClose))
    [void]$dlg.ShowDialog($form)
}
$lv.Add_DoubleClick({
    $it = $lv.SelectedItems | Select-Object -First 1
    if ($it) { Show-HostCard $it.Text }
})

function New-Button($Parent, [string]$Text, [int]$X, [int]$Y, [int]$W, [string]$Tip, [scriptblock]$Action) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $Text; $b.Location = New-Object System.Drawing.Point($X, $Y); $b.Size = New-Object System.Drawing.Size($W, 32)
    $b.Add_Click($Action); $Parent.Controls.Add($b); $tips.SetToolTip($b, $Tip)
}

New-Button $form "Actualiser" 285 68 240 "Relit l'AD et les journaux, puis vérifie le réseau : DNS, ping, partage de fichiers et identité de chaque poste. Maj + clic : revérifie aussi les identités déjà confirmées" { $full = ([System.Windows.Forms.Control]::ModifierKeys -band [System.Windows.Forms.Keys]::Shift) -ne 0; Update-Data; Update-Network -Full:$full }
function Update-Network([switch]$Full) {
    $lblNet.Text = "vérification..."
    Show-Loading "Vérification du réseau...`r`n`r`nDNS, ping, partage de fichiers et identité de chaque poste"
    $script:net = Test-NetworkFull @($script:rows | ForEach-Object Computer) -Full:$Full
    $script:netTime = Get-Date
    # Keep the MAC addresses seen, for Wake-on-LAN (a computer keeps its entry while it is off)
    $cache = @{}
    $old = Read-JsonFile $macCacheFile
    if ($old) { foreach ($p in $old.PSObject.Properties) { $cache[$p.Name] = $p.Value } }
    foreach ($n in @($script:net.Keys)) {
        $i = $script:net[$n].Info
        if ($i -and @($i.Macs).Count) { $cache[$n] = [pscustomobject]@{ Macs = @($i.Macs); Ip = $script:net[$n].Ip; Seen = (Get-Date).ToString('s') } }
    }
    try { Save-JsonFile $macCacheFile $cache } catch { }
    Hide-Loading
    Apply-SoftStatus
    Update-NetCounters
    Update-List
}
# "En ligne" and "Hors ligne" boxes, from the current network state (full or single-computer refresh)
function Update-NetCounters {
    $labels = @($script:rows | ForEach-Object { Get-NetLabel $_.Computer })
    $on = @($labels | Where-Object { $_ -eq 'En ligne' }).Count
    $notOn = @($labels | Where-Object { $_ -and $_ -ne 'En ligne' })
    $lblOnline.Text  = "$([char]0x25CF) En ligne : $on"
    $lblOffline.Text = "$([char]0x25CF) Hors ligne : $($notOn.Count)"
    $why = ($notOn | Group-Object | Sort-Object Count -Descending | ForEach-Object { "$($_.Count) $($_.Name.ToLower())" }) -join ', '
    $tips.SetToolTip($lblOffline, "$why. Cliquez pour n'afficher qu'eux")
    $lblNet.Text = "vérifié à " + $script:netTime.ToString('HH:mm')
    $unv = @($script:rows | Where-Object { $script:net.ContainsKey($_.Computer) -and $script:net[$_.Computer].State -eq 'UNVERIFIED' }).Count
    $tips.SetToolTip($lblOnline, "Vérifié à $($script:netTime.ToString('HH:mm')). $on en ligne, dont $unv sans identité confirmée (triangle jaune). Cliquez pour n'afficher que les postes en ligne (se combine avec les cases d'état)")
}

# Network boxes: click to show only the online (or not reachable) computers; combines with the status filter
$lblOnline = New-Object System.Windows.Forms.Label
$lblOnline.Location = New-Object System.Drawing.Point(565, 72); $lblOnline.Size = New-Object System.Drawing.Size(120, 24)
$lblOnline.TextAlign = "MiddleCenter"; $lblOnline.BorderStyle = "FixedSingle"; $lblOnline.Cursor = [System.Windows.Forms.Cursors]::Hand
$lblOnline.BackColor = [System.Drawing.Color]::FromArgb(223, 246, 221); $lblOnline.ForeColor = [System.Drawing.Color]::FromArgb(16, 124, 16)
$lblOnline.Text = "$([char]0x25CF) En ligne : -"; $lblOnline.Tag = 'ONLINE'
$form.Controls.Add($lblOnline)
$lblOnline.Add_Click({ Set-NetFilter $this.Tag })
$tips.SetToolTip($lblOnline, "Cliquez pour n'afficher que les postes en ligne (se combine avec les cases d'état)")
$lblOffline = New-Object System.Windows.Forms.Label
$lblOffline.Location = New-Object System.Drawing.Point(691, 72); $lblOffline.Size = New-Object System.Drawing.Size(130, 24)
$lblOffline.TextAlign = "MiddleCenter"; $lblOffline.BorderStyle = "FixedSingle"; $lblOffline.Cursor = [System.Windows.Forms.Cursors]::Hand
$lblOffline.BackColor = [System.Drawing.Color]::FromArgb(253, 231, 233); $lblOffline.ForeColor = [System.Drawing.Color]::FromArgb(196, 43, 28)
$lblOffline.Text = "$([char]0x25CF) Hors ligne : -"; $lblOffline.Tag = 'OFFLINE'
$form.Controls.Add($lblOffline)
$lblOffline.Add_Click({ Set-NetFilter $this.Tag })
$tips.SetToolTip($lblOffline, "Cliquez pour n'afficher que les postes qui ne répondent pas")
$lblNet = New-Object System.Windows.Forms.Label   # verification time, shown in the "En ligne" tooltip
$lblStale = New-Object System.Windows.Forms.Label
$lblStale.Location = New-Object System.Drawing.Point(826, 72); $lblStale.Size = New-Object System.Drawing.Size(99, 24)
$lblStale.TextAlign = "MiddleCenter"; $lblStale.BorderStyle = "FixedSingle"; $lblStale.Cursor = [System.Windows.Forms.Cursors]::Hand
$lblStale.BackColor = [System.Drawing.Color]::FromArgb(240, 240, 240); $lblStale.ForeColor = [System.Drawing.Color]::FromArgb(196, 43, 28)
$lblStale.Text = "Inactifs : -"; $lblStale.Tag = 'STALE'
$form.Controls.Add($lblStale)
$lblStale.Add_Click({ Set-StatusFilter $this.Tag })
$tips.SetToolTip($lblStale, "Postes sans connexion au domaine depuis plus de $staleDays jours : oubliés dans l'AD ? Cliquez pour n'afficher qu'eux")
function Set-NetFilter([string]$Key) {
    $script:netFilter = if ($script:netFilter -eq $Key) { $null } else { $Key }
    $lblOnline.BorderStyle  = $(if ($script:netFilter -eq 'ONLINE')  { "Fixed3D" } else { "FixedSingle" })
    $lblOffline.BorderStyle = $(if ($script:netFilter -eq 'OFFLINE') { "Fixed3D" } else { "FixedSingle" })
    Update-List
}
New-Button $form "Cocher les postes à faire" 275 636 190 "Coche les postes affichés qui ne sont pas « Fait » (les postes « En dehors des heures ouvrables » restent décochés pendant les heures ouvrables)" {
    $day = Test-BusinessHours
    $skipped = @()
    foreach ($it in $lv.Items) {
        if ($it.SubItems[1].Text -eq $statusInfo['DONE'].Label) { continue }
        if ($day -and $script:nightOnly.Contains($it.Text)) { $skipped += $it.Text; continue }
        $it.Checked = $true
    }
    if ($skipped.Count -gt 0) {
        Show-Info ("Heures ouvrables : {0} poste(s) « En dehors des heures ouvrables » non coché(s) :`r`n  {1}`r`n`r`nCochez-les à la main si vous voulez quand même les déployer maintenant." -f $skipped.Count, ($skipped -join "`r`n  ")) "En dehors des heures ouvrables"
    }
}
New-Button $form "Tout décocher" 475 636 120 "Vide toute la sélection" { $checked.Clear(); Update-List }

# --- exclude.txt / night-only.txt on the share: read, add, remove (comment lines are kept) ---
function Add-ToListFile([string]$Path, [string[]]$Names, [string]$Comment) {
    $existing = @(Get-Content $Path -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() })
    $new = @($Names | Where-Object { $n = $_; -not ($existing | Where-Object { $_ -eq $n }) } | Sort-Object -Unique)
    if ($new.Count -eq 0) { return 0 }
    $lines = @("# $Comment (added from the update panel on $(Get-Date -Format 'yyyy-MM-dd') by $env:USERNAME)") + $new
    Add-Content -Path $Path -Value $lines -Encoding ASCII
    $new.Count
}
function Remove-FromListFile([string]$Path, [string[]]$Entries) {
    $lines = @(Get-Content $Path -ErrorAction SilentlyContinue)
    $keep  = @($lines | Where-Object { $l = $_.Trim(); -not ($Entries | Where-Object { $_ -eq $l }) })
    Set-Content -Path $Path -Value $keep -Encoding ASCII
    $lines.Count - $keep.Count
}
# Entries of exclude.txt with their reason (the comment line above them) and the AD computers they match
function Get-ExcludeEntries {
    $reason = ""
    foreach ($line in @(Get-Content $excludeFile -ErrorAction SilentlyContinue)) {
        $l = $line.Trim()
        if (-not $l) { continue }
        if ($l.StartsWith('#')) { $reason = $l.TrimStart('#').Trim(); continue }
        $matched = @($script:adNames | Where-Object { $_ -like $l })
        [pscustomobject]@{ Entry = $l; Reason = $reason; Matches = $matched }
    }
}

# Computers targeted by a right-click: the selected rows, or the checked ones when none is selected
function Get-MenuTargets {
    $sel = @($lv.SelectedItems | ForEach-Object { $_.Text })
    if ($sel.Count -gt 0) { return $sel }
    @($checked | Sort-Object)
}
function Confirm-Action([string]$Question, [string[]]$Names) {
    [System.Windows.Forms.MessageBox]::Show((Get-MsgOwner), ("{0}`r`n`r`n  {1}" -f $Question, ($Names -join "`r`n  ")), "Confirmation", "YesNo", "Question") -eq "Yes"
}

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$miExclude = $menu.Items.Add("Exclure (ne plus jamais déployer)")
$miNight   = $menu.Items.Add("Marquer « en dehors des heures ouvrables »")
$miDay     = $menu.Items.Add("Retirer de « en dehors des heures ouvrables »")
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miQueue   = $menu.Items.Add("Déployer dès que disponible (file d'attente)")
$miUnqueue = $menu.Items.Add("Retirer de la file d'attente")
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miCopy    = $menu.Items.Add("Copier le détail (réseau, dernière connexion)")
$miRefOne  = $menu.Items.Add("Actualiser ce poste")
# Full network check (DNS, ping, file sharing, identity, user, restart) of the selected computers only, ignoring the 4-hour memory
$miRefOne.Add_Click({
    $t = @($lv.SelectedItems | ForEach-Object { $_.Text })
    if ($t.Count -eq 0) { Show-Info "Sélectionnez au moins un poste (clic droit sur sa ligne)." "Actualiser ce poste"; return }
    Show-Loading "Vérification de $($t -join ', ')..."
    $r = Test-NetworkFull $t -Full
    Hide-Loading
    if (-not $script:net) { $script:net = @{} }
    foreach ($k in @($r.Keys)) { $script:net[$k] = $r[$k] }
    if (-not $script:netTime) { $script:netTime = Get-Date }
    Apply-SoftStatus
    Update-NetCounters
    Update-List
})
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miRestart = $menu.Items.Add("Redémarrer...")
$miAbort   = $menu.Items.Add("Annuler un redémarrage prévu")
$miMsg     = $menu.Items.Add("Envoyer un message à l'usager...")
$miShare   = $menu.Items.Add("Ouvrir C$")
$miWake    = $menu.Items.Add("Réveiller (Wake-on-LAN)")
$miInstall = $menu.Items.Add("Installer un logiciel...")

# --- Software catalog (file on the share, created with these defaults the first time) ---
$catalogFile = "$deployRoot\Winget-upgrade\catalog.json"
$defaultCatalog = @(
    [pscustomobject]@{ Name = "Java 8 (64 bits)";               Type = "winget"; Id = "Oracle.JavaRuntimeEnvironment"; Arch = "x64"; Detect = "^Java 8 Update .*64-bit" }
    [pscustomobject]@{ Name = "7-Zip";                          Type = "winget"; Id = "7zip.7zip";                     Arch = "";    Detect = "^7-Zip" }
    [pscustomobject]@{ Name = "Notepad++";                      Type = "winget"; Id = "Notepad++.Notepad++";           Arch = "";    Detect = "^Notepad\+\+" }
    [pscustomobject]@{ Name = "VLC";                            Type = "winget"; Id = "VideoLAN.VLC";                  Arch = "";    Detect = "^VLC media player" }
    [pscustomobject]@{ Name = "Adobe Acrobat Reader (64 bits)"; Type = "winget"; Id = "Adobe.Acrobat.Reader.64-bit";   Arch = "";    Detect = "^Adobe Acrobat" }
)
if ($useOnePassword) { $defaultCatalog += [pscustomobject]@{ Name = "1Password"; Type = "msi"; Path = $opMsiFile; Args = "MANAGED_INSTALL=1 MANAGED_UPDATE=1 /qn /norestart"; Detect = "^1Password$"; Publisher = "O=AgileBits Inc\." } }
function Get-Catalog {
    $c = Read-JsonFile $catalogFile
    if (-not $c) { try { Save-JsonFile $catalogFile $defaultCatalog } catch { }; return $defaultCatalog }
    @($c | ForEach-Object { $_ } | Where-Object { $_.Name })
}

# Which catalog software is installed on each computer (uninstall entries of the registry)
function Get-InstalledMatrix([string[]]$Names, $Catalog) {
    $opt = New-PSSessionOption -OpenTimeout 8000 -OperationTimeout 20000
    $res = @{}
    $out = @(Invoke-Command -ComputerName $Names -SessionOption $opt -ErrorAction SilentlyContinue -ErrorVariable errs -ArgumentList (,@($Catalog | ForEach-Object { $_.Detect })) -ScriptBlock {
        param($patterns)
        $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
        $names = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName } | ForEach-Object { $_.DisplayName })
        [pscustomobject]@{ Found = @($patterns | ForEach-Object { $p = $_; [bool]($names | Where-Object { $_ -match $p }) }) }
    })
    foreach ($o in $out) { $res[[string]$o.PSComputerName] = @($o.Found) }
    foreach ($e in $errs) { $res[[string]$e.TargetObject] = $null }
    $res
}

# Install on each computer, as SYSTEM, through a one-time scheduled task (winget runs as SYSTEM, MSI copied first).
# The work folder is protected (maj-secure.ps1: SYSTEM and Administrators only) and the script SYSTEM runs is
# fixed: the catalog entries are passed as data (install.json), never pasted into code.
$installRelative = "MAJ-Logiciels\install"
$installScript = @'
$dir  = Split-Path $MyInvocation.MyCommand.Path -Parent
$log  = Join-Path $dir "install.log"
. (Join-Path $dir "maj-secure.ps1")
"START" | Set-Content $log
$wg = Resolve-Path "C:\Program Files\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe\winget.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1
$k = 0
foreach ($it in @(Get-Content (Join-Path $dir "install.json") -Raw -Encoding UTF8 | ConvertFrom-Json)) {
    $k++
    "STEP $($it.Name)" | Add-Content $log
    $c = $null
    try {
        if ($it.Type -eq 'msi') {
            $msi = Join-Path $dir ([IO.Path]::GetFileName([string]$it.File))
            if ($it.Publisher) { [void](Assert-Publisher $msi @($it.Publisher)) }
            $msiArgs = @('/i', "`"$msi`"") + @(if ($it.Args) { [string]$it.Args }) + @('/l*v', "`"$msi.log`"")
            $c = (Start-Process msiexec.exe -ArgumentList $msiArgs -Wait -PassThru).ExitCode
        } elseif ($wg) {
            $wgArgs = @('install', '--id', [string]$it.Id, '-e') + @(if ($it.Arch) { '--architecture', [string]$it.Arch }) +
                      @('--source', 'winget', '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity')
            & $wg.Path @wgArgs *>> (Join-Path $dir "winget-$k.log")
            $c = $LASTEXITCODE
        } else { $c = 'winget-missing' }
    } catch { $c = "error: $($_.Exception.Message)" }
    "RESULT $($it.Name)|$c" | Add-Content $log
}
"DONE" | Add-Content $log
Unregister-ScheduledTask -TaskName "MAJ-Logiciels-Install" -Confirm:$false -ErrorAction SilentlyContinue
'@
function Get-RemoteInstallDir([string]$Pc) { "\\$Pc\C$\ProgramData\$installRelative" }
function Start-Install([hashtable]$Plan) {   # computer -> list of catalog entries to install
    $script:installs = if ($script:installs) { $script:installs } else { @{} }
    $failed = @()
    $secureText = Get-Content (Join-Path $packageDir "maj-secure.ps1") -Raw -ErrorAction SilentlyContinue
    if (-not $secureText) { return @("maj-secure.ps1 introuvable sur le partage ($packageDir) : relancez Installer.ps1") }
    foreach ($pc in @($Plan.Keys)) {
        $items = @($Plan[$pc])
        try {
            # 1. Protected work folder, created on the computer itself (an untrusted one is moved aside)
            $sec = Invoke-Command -ComputerName $pc -ErrorAction Stop -ArgumentList $secureText, $installRelative -ScriptBlock {
                param($code, $rel)
                . ([scriptblock]::Create($code))
                Initialize-SecureFolder $env:ProgramData $rel
            }
            $dir = Get-RemoteInstallDir $pc
            Remove-Item "$dir\*" -Force -Recurse -ErrorAction SilentlyContinue
            # 2. Fixed script, its helpers, the installers and the list of what to install
            Set-Content -Path "$dir\install.ps1" -Value $installScript -Encoding UTF8 -ErrorAction Stop
            Set-Content -Path "$dir\maj-secure.ps1" -Value $secureText -Encoding ASCII -ErrorAction Stop
            $data = foreach ($it in $items) {
                if ($it.Type -eq 'msi') {
                    Copy-Item $it.Path $dir -Force -ErrorAction Stop
                    [pscustomobject]@{ Name = [string]$it.Name; Type = 'msi'; File = (Split-Path $it.Path -Leaf); Args = [string]$it.Args; Publisher = [string]$it.Publisher }
                } else {
                    [pscustomobject]@{ Name = [string]$it.Name; Type = 'winget'; Id = [string]$it.Id; Arch = [string]$it.Arch }
                }
            }
            ConvertTo-Json -InputObject @($data) -Depth 3 | Set-Content -Path "$dir\install.json" -Encoding UTF8 -ErrorAction Stop
            # 3. One-time task as SYSTEM
            Invoke-Command -ComputerName $pc -ErrorAction Stop -ArgumentList $installRelative -ScriptBlock {
                param($rel)
                $a = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $env:ProgramData $rel)\install.ps1`""
                Register-ScheduledTask -TaskName "MAJ-Logiciels-Install" -Action $a -User "SYSTEM" -RunLevel Highest -Force | Out-Null
                Start-ScheduledTask -TaskName "MAJ-Logiciels-Install"
            }
            $script:installs[$pc] = @{ Start = Get-Date; Count = $items.Count; Done = $false; Text = "Installation : démarrage" + $(if (@($sec.Messages).Count) { " (dossier non fiable mis de côté)" }) }
        } catch { $failed += "$pc : $($_.Exception.Message)" }
    }
    if ($script:installs.Count) { $lblHelp.Visible = $false; $timer.Start() }
    $failed
}
function Test-InstallsActive { [bool]($script:installs -and @($script:installs.Values | Where-Object { -not $_.Done }).Count) }
# Reads each computer's install log: current step, then the result of each software
function Update-InstallProgress {
    foreach ($pc in @($script:installs.Keys)) {
        $s = $script:installs[$pc]
        if ($s.Done) { continue }
        $lines = @(Get-Content "$(Get-RemoteInstallDir $pc)\install.log" -ErrorAction SilentlyContinue)
        $results = @($lines | Where-Object { $_ -like 'RESULT *' } | ForEach-Object {
            $n, $c = ($_.Substring(7)) -split '\|', 2
            $ok = $c -in '0', '3010', '-1978335189'   # 3010: restart needed, -1978335189: winget "already installed"
            "$n $(if ($ok) { 'OK' } else { "échec ($c)" })"
        })
        if ($lines -contains 'DONE') {
            $s.Done = $true; $s.Text = "Installé : " + ($results -join ', ')
            $s.Ok = -not ($results -match 'échec')
        } elseif (((Get-Date) - $s.Start).TotalMinutes -gt 30) {
            $s.Done = $true; $s.Ok = $false; $s.Text = "Délai dépassé (voir C:\ProgramData\MAJ-Logiciels\install sur le poste)"
        } else {
            $step = ($lines | Where-Object { $_ -like 'STEP *' } | Select-Object -Last 1)
            $k = $results.Count + 1
            $wl = @(Get-Content "$(Get-RemoteInstallDir $pc)\winget-$k.log" -ErrorAction SilentlyContinue)
            $phase = 0.05; $what = "démarrage"
            if (-not $wl -and $step) { $phase = 0.5; $what = "installation" }   # MSI: no steps written
            foreach ($l in $wl) {
                if ($l -match '^Found ')                          { $phase = 0.10; $what = "recherche" }
                elseif ($l -match 'Downloading')                  { $phase = 0.30; $what = "téléchargement" }
                elseif ($l -match 'verified installer hash')      { $phase = 0.60; $what = "vérification" }
                elseif ($l -match 'Starting package install')     { $phase = 0.70; $what = "installation" }
                elseif ($l -match 'Successfully installed')       { $phase = 0.95; $what = "finalisation" }
            }
            $pct = [int](100 * ($results.Count + $phase) / [Math]::Max(1, $s.Count))
            $s.Text = if ($step) { "$pct % - $($step.Substring(5)) ($k/$($s.Count)) : $what" } elseif ($lines) { "0 % - démarrage" } else { "0 % - en attente de la tâche" }
        }
    }
    if (-not (Test-InstallsActive) -and $script:installs.Count) {
        $summary = @($script:installs.GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key) : $($_.Value.Text)" })
        $script:installs = @{}
        if (-not ($script:deploy -and $script:deploy.Active)) { $timer.Stop() }
        Update-Data
        Show-Info ("Installation terminée.`r`n`r`n" + ($summary -join "`r`n")) "Installer un logiciel"
    }
}

# Window: catalog with what is missing on the selected computers, then install
$miInstall.Add_Click({
    $t = @($lv.SelectedItems | ForEach-Object { $_.Text })
    if ($t.Count -eq 0) { $t = @($checked | Sort-Object) }
    if ($t.Count -eq 0) { Show-Info "Sélectionnez ou cochez au moins un poste." "Installer un logiciel"; return }
    $cat = @(Get-Catalog)
    Show-Loading "Inventaire des logiciels sur $($t.Count) poste(s)..."
    $m = Get-InstalledMatrix $t $cat
    Hide-Loading
    $unreach = @($t | Where-Object { -not $m[$_] })
    $ok = @($t | Where-Object { $m[$_] })
    if ($ok.Count -eq 0) { Show-Info ("Aucun poste joignable :`r`n  " + ($unreach -join "`r`n  ")) "Installer un logiciel"; return }
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Installer un logiciel"; $dlg.ClientSize = New-Object System.Drawing.Size(520, 400); $dlg.StartPosition = "CenterParent"
    $dlg.FormBorderStyle = "FixedDialog"; $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false; $dlg.Font = $form.Font
    $lbl = New-Object System.Windows.Forms.Label; $lbl.Location = New-Object System.Drawing.Point(12, 10); $lbl.Size = New-Object System.Drawing.Size(496, 54)
    $lbl.Text = "Postes : " + ($ok -join ", ") + $(if ($unreach) { "`r`nInjoignables, écartés : " + ($unreach -join ", ") })
    $list = New-Object System.Windows.Forms.CheckedListBox; $list.Location = New-Object System.Drawing.Point(12, 70); $list.Size = New-Object System.Drawing.Size(496, 270); $list.CheckOnClick = $true
    for ($i = 0; $i -lt $cat.Count; $i++) {
        $missing = @($ok | Where-Object { -not $m[$_][$i] })
        $state = if ($missing.Count -eq 0) { "déjà installé partout" } elseif ($missing.Count -eq $ok.Count) { "absent" } else { "absent sur " + ($missing -join ", ") }
        [void]$list.Items.Add("$($cat[$i].Name)  -  $state")
    }
    $bGo = New-Object System.Windows.Forms.Button; $bGo.Text = "Installer"; $bGo.Location = New-Object System.Drawing.Point(300, 352); $bGo.Size = New-Object System.Drawing.Size(100, 34)
    $bNo = New-Object System.Windows.Forms.Button; $bNo.Text = "Annuler"; $bNo.Location = New-Object System.Drawing.Point(408, 352); $bNo.Size = New-Object System.Drawing.Size(100, 34)
    $bNo.Add_Click({ $dlg.Close() })
    $bGo.Add_Click({
        $plan = @{}
        foreach ($i in @($list.CheckedIndices)) { foreach ($pc in $ok) { if (-not $m[$pc][$i]) { if (-not $plan[$pc]) { $plan[$pc] = @() }; $plan[$pc] += $cat[$i] } } }
        if ($plan.Count -eq 0) { Show-Info "Rien à installer : les logiciels cochés sont déjà présents." "Installer un logiciel"; return }
        $desc = @($plan.GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key) : " + (@($_.Value | ForEach-Object Name) -join ", ") })
        $night = @($plan.Keys | Where-Object { $script:nightOnly.Contains($_) })
        $warn = if ($night -and (Test-BusinessHours)) { "`r`n`r`nAttention, heures ouvrables : " + ($night -join ", ") + " (en dehors des heures ouvrables)" } else { "" }
        if (-not (Confirm-Action ("Installer ?" + $warn) $desc)) { return }
        $script:installPlan = $plan; $dlg.Close()
    })
    $dlg.Controls.AddRange(@($lbl, $list, $bGo, $bNo))
    $script:installPlan = $null
    [void]$dlg.ShowDialog($form)
    if ($script:installPlan) {
        $f = @(Start-Install $script:installPlan)
        Update-List
        if ($f.Count) { Show-Info ("Non lancé sur :`r`n" + ($f -join "`r`n")) "Installer un logiciel" }
    }
})
# Magic packet to the general broadcast and to the computer's last known subnet (.255): crossing subnets needs the router to allow it
function Send-WakeOnLan([string[]]$Names) {
    $cache = Read-JsonFile $macCacheFile
    $udp = New-Object Net.Sockets.UdpClient; $udp.EnableBroadcast = $true
    $out = foreach ($n in $Names) {
        $e = if ($cache) { $cache.$n } else { $null }
        if (-not $e -or -not @($e.Macs).Count) { "$n : adresse MAC inconnue (le poste doit avoir été vu en ligne une fois)"; continue }
        foreach ($mac in @($e.Macs)) {
            $b = [byte[]]($mac -split '[:-]' | ForEach-Object { [Convert]::ToByte($_, 16) })
            $pkt = [byte[]]((,[byte]255 * 6) + ($b * 16))
            $dest = @('255.255.255.255') + $(if ($e.Ip -match '^(\d+\.\d+\.\d+)\.\d+$') { "$($Matches[1]).255" })
            foreach ($d in ($dest | Select-Object -Unique)) { [void]$udp.Send($pkt, $pkt.Length, $d, 9) }
        }
        "$n : paquet envoyé ($(@($e.Macs) -join ', '))"
    }
    $udp.Close()
    $out
}
$miWake.Add_Click({
    $t = @($lv.SelectedItems | ForEach-Object { $_.Text })
    if ($t.Count -eq 0) { $t = @($checked | Sort-Object) }
    if ($t.Count -eq 0) { Show-Info "Sélectionnez ou cochez au moins un poste." "Réveiller"; return }
    Show-Info ((@(Send-WakeOnLan $t) -join "`r`n") + "`r`n`r`nCliquez sur « Actualiser » dans une ou deux minutes pour voir s'ils répondent.") "Réveiller"
})
Add-Type -AssemblyName Microsoft.VisualBasic
# Runs a small command on the selected computers and shows one line per computer
function Invoke-OnTargets([string[]]$Names, [scriptblock]$Block, [object[]]$ArgList, [string]$Title) {
    $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    $opt = New-PSSessionOption -OpenTimeout 8000 -OperationTimeout 20000
    $out = @(Invoke-Command -ComputerName $Names -SessionOption $opt -ArgumentList $ArgList -ScriptBlock $Block -ErrorAction SilentlyContinue -ErrorVariable errs)
    $form.Cursor = [System.Windows.Forms.Cursors]::Default
    $lines = @($out | ForEach-Object { "$($_.PSComputerName) : $_" }) + @($errs | ForEach-Object { "$($_.TargetObject) : ÉCHEC - " + (($_.Exception.Message -replace '\s+', ' ').Substring(0, [Math]::Min(120, ($_.Exception.Message -replace '\s+', ' ').Length))) })
    Show-Info ($lines -join "`r`n") $Title
}
$miRestart.Add_Click({
    $t = @($lv.SelectedItems | ForEach-Object { $_.Text })
    if ($t.Count -eq 0) { Show-Info "Sélectionnez au moins un poste (clic droit sur sa ligne)." "Redémarrer"; return }
    $min = [Microsoft.VisualBasic.Interaction]::InputBox("Délai avant le redémarrage, en minutes (0 = immédiat).`r`nL'usager verra un avertissement avec ce délai.`r`n`r`n" + ($t -join ", "), "Redémarrer", "5")
    if ($min -eq "") { return }
    if ($min -notmatch '^\d+$') { Show-Info "Entrez un nombre de minutes." "Redémarrer"; return }
    if (-not (Confirm-Action "Redémarrer ces postes dans $min minute(s) ?" $t)) { return }
    Invoke-OnTargets $t { param($s, $who) shutdown.exe /r /t $s /c "Redémarrage demandé par $who. Enregistrez votre travail."; "redémarrage prévu dans $([int]($s / 60)) min (code $LASTEXITCODE)" } @(([int]$min * 60), $senderLower) "Redémarrer"
})
$miAbort.Add_Click({
    $t = @($lv.SelectedItems | ForEach-Object { $_.Text })
    if ($t.Count -eq 0) { Show-Info "Sélectionnez au moins un poste." "Annuler"; return }
    Invoke-OnTargets $t { shutdown.exe /a 2>$null; if ($LASTEXITCODE -eq 0) { "redémarrage annulé" } else { "aucun redémarrage prévu (code $LASTEXITCODE)" } } @() "Annuler un redémarrage"
})
$miMsg.Add_Click({
    $t = @($lv.SelectedItems | ForEach-Object { $_.Text })
    if ($t.Count -eq 0) { Show-Info "Sélectionnez au moins un poste." "Message"; return }
    $text = [Microsoft.VisualBasic.Interaction]::InputBox("Message à afficher à l'usager de :`r`n" + ($t -join ", "), "Envoyer un message", "")
    if (-not $text.Trim()) { return }
    Invoke-OnTargets $t { param($m, $who) msg.exe * /TIME:600 "$who : $m"; "message envoyé (code $LASTEXITCODE)" } @($text, $sender) "Envoyer un message"
})
$miShare.Add_Click({ foreach ($n in @($lv.SelectedItems | ForEach-Object { $_.Text } | Select-Object -First 5)) { Start-Process explorer.exe "\\$n\C$" } })
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miCols    = $menu.Items.Add("Colonnes affichées...")
$lv.ContextMenuStrip = $menu
$miCols.Add_Click({
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Colonnes affichées"; $dlg.ClientSize = New-Object System.Drawing.Size(330, 330); $dlg.StartPosition = "CenterParent"
    $dlg.FormBorderStyle = "FixedDialog"; $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false; $dlg.Font = $form.Font
    $lbl = New-Object System.Windows.Forms.Label; $lbl.Location = New-Object System.Drawing.Point(12, 10); $lbl.Size = New-Object System.Drawing.Size(306, 36)
    $lbl.Text = "Cochez les colonnes à afficher. Pour les déplacer, faites glisser leur en-tête."
    $list = New-Object System.Windows.Forms.CheckedListBox; $list.Location = New-Object System.Drawing.Point(12, 50); $list.Size = New-Object System.Drawing.Size(306, 200); $list.CheckOnClick = $true
    for ($i = 1; $i -lt $lv.Columns.Count; $i++) { [void]$list.Items.Add($lv.Columns[$i].Text, -not $script:hiddenCols.Contains($i)) }
    $bOk = New-Object System.Windows.Forms.Button; $bOk.Text = "OK"; $bOk.Location = New-Object System.Drawing.Point(12, 285); $bOk.Size = New-Object System.Drawing.Size(80, 32)
    $bDef = New-Object System.Windows.Forms.Button; $bDef.Text = "Disposition par défaut"; $bDef.Location = New-Object System.Drawing.Point(100, 285); $bDef.Size = New-Object System.Drawing.Size(218, 32)
    $bOk.Add_Click({
        for ($i = 1; $i -lt $lv.Columns.Count; $i++) {
            $show = $list.GetItemChecked($i - 1)
            if ($show -and $script:hiddenCols.Contains($i)) { [void]$script:hiddenCols.Remove($i); $lv.Columns[$i].Width = $script:colDefaults[$i] }
            elseif (-not $show) { [void]$script:hiddenCols.Add($i); $lv.Columns[$i].Width = 0 }
        }
        $script:autoFill = $false; Save-Layout; $dlg.Close()
    })
    $bDef.Add_Click({
        $script:hiddenCols.Clear()
        for ($i = 0; $i -lt $lv.Columns.Count; $i++) { $lv.Columns[$i].DisplayIndex = $i; $lv.Columns[$i].Width = $script:colDefaults[$i] }
        Remove-Item $layoutFile -Force -ErrorAction SilentlyContinue
        $script:autoFill = $true; $dlg.Close()
    })
    $dlg.Controls.AddRange(@($lbl, $list, $bOk, $bDef))
    [void]$dlg.ShowDialog($form)
})
# Copies the tooltip text of the selected rows (network state with the exact reason, last logon), one line per computer
$miCopy.Add_Click({
    $items = @($lv.SelectedItems)
    if ($items.Count -eq 0) { Show-Info "Sélectionnez au moins un poste (clic droit sur sa ligne)." "Copier"; return }
    $text = ($items | ForEach-Object { if ($_.ToolTipText) { $_.ToolTipText } else { "$($_.Text) : pas encore de vérification réseau" } }) -join "`r`n"
    try { [System.Windows.Forms.Clipboard]::SetText($text) } catch { Show-Info "Copie impossible : $($_.Exception.Message)" "Copier" }
})
$miQueue.Add_Click({
    $t = Get-MenuTargets
    if ($t.Count -eq 0) { Show-Info "Sélectionnez ou cochez au moins un poste." "File d'attente"; return }
    $q = @(Get-AgentQueue)
    $new = @($t | Where-Object { $c = $_; -not ($q | Where-Object { $_.Computer -eq $c }) })
    if ($new.Count -eq 0) { Show-Info "Ces postes sont déjà dans la file." "File d'attente"; return }
    foreach ($c in $new) { $q += [pscustomobject]@{ Computer = $c; Added = (Get-Date).ToString('s'); From = "panel" } }
    Save-JsonFile $agentQueueFile @($q)
    Update-Data
    $when = if ($script:agentSettings.Queue.When -eq 'Anytime') { "dès qu'ils répondront" } else { "dès qu'ils répondront, en dehors des heures ouvrables" }
    $warn = if (-not $script:agentSettings.Enabled) { "`r`n`r`nAttention : l'agent est désactivé (Paramètres de l'agent)." } elseif ($script:agentSettings.DryRun) { "`r`n`r`nL'agent est en mode test : il notera ce qu'il ferait, sans lancer." } else { "" }
    Show-Info ("{0} poste(s) ajouté(s) à la file. L'agent les déploiera {1}.{2}" -f $new.Count, $when, $warn) "File d'attente"
})
$miUnqueue.Add_Click({
    $t = Get-MenuTargets
    if ($t.Count -eq 0) { Show-Info "Sélectionnez ou cochez au moins un poste." "File d'attente"; return }
    $q = @(Get-AgentQueue | Where-Object { $t -notcontains $_.Computer })
    Save-JsonFile $agentQueueFile @($q)
    Update-Data
})
$miExclude.Add_Click({
    $t = Get-MenuTargets
    if ($t.Count -eq 0) { Show-Info "Sélectionnez ou cochez au moins un poste." "Exclure"; return }
    if (-not (Confirm-Action "Exclure ces postes ? Ils disparaîtront du tableau et le paquet refusera de s'y exécuter, même lancé depuis PDQ." $t)) { return }
    $n = Add-ToListFile $excludeFile $t "Excluded"
    foreach ($c in $t) { [void]$checked.Remove($c) }
    Update-Data
    Show-Info "$n poste(s) ajouté(s) à la liste des exclus." "Exclure"
})
$miNight.Add_Click({
    $t = Get-MenuTargets
    if ($t.Count -eq 0) { Show-Info "Sélectionnez ou cochez au moins un poste." "En dehors des heures ouvrables"; return }
    if (-not (Confirm-Action "Marquer ces postes « en dehors des heures ouvrables » ?" $t)) { return }
    [void](Add-ToListFile $nightFile $t "Outside business hours only")
    Update-Data
})
$miDay.Add_Click({
    $t = Get-MenuTargets
    if ($t.Count -eq 0) { Show-Info "Sélectionnez ou cochez au moins un poste." "En dehors des heures ouvrables"; return }
    if (-not (Confirm-Action "Retirer ces postes de « en dehors des heures ouvrables » ?" $t)) { return }
    [void](Remove-FromListFile $nightFile $t)
    Update-Data
})

# Window listing the excluded computers, to put some back
function Show-ExcludedDialog {
    $entries = @(Get-ExcludeEntries)
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Postes exclus"; $dlg.ClientSize = New-Object System.Drawing.Size(560, 420); $dlg.StartPosition = "CenterParent"
    $dlg.FormBorderStyle = "FixedDialog"; $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false; $dlg.Font = $form.Font
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Location = New-Object System.Drawing.Point(15, 12); $lbl.Size = New-Object System.Drawing.Size(530, 40)
    $lbl.Text = "Postes de exclude.txt : le paquet ne s'y exécute pas. Cochez ceux à réintégrer."
    $dlg.Controls.Add($lbl)
    $list = New-Object System.Windows.Forms.CheckedListBox
    $list.Location = New-Object System.Drawing.Point(15, 55); $list.Size = New-Object System.Drawing.Size(530, 300); $list.CheckOnClick = $true
    foreach ($en in $entries) {
        $what = if ($en.Entry -match '[\*\?]') { "$($en.Entry)  ($($en.Matches.Count) poste(s) : $($en.Matches -join ', '))" }
                elseif ($en.Matches.Count -eq 0) { "$($en.Entry)  (plus dans l'AD)" } else { $en.Entry }
        [void]$list.Items.Add($(if ($en.Reason) { "$what  —  $($en.Reason)" } else { $what }))
    }
    $dlg.Controls.Add($list)
    $btnBack = New-Object System.Windows.Forms.Button
    $btnBack.Text = "Réintégrer les postes cochés"; $btnBack.Location = New-Object System.Drawing.Point(15, 370); $btnBack.Size = New-Object System.Drawing.Size(260, 34)
    $btnBack.Add_Click({
        $sel = @($list.CheckedIndices | ForEach-Object { $entries[$_].Entry })
        if ($sel.Count -eq 0) { Show-Info "Cochez au moins un poste." "Postes exclus"; return }
        if (-not (Confirm-Action "Réintégrer ces postes ? Ils réapparaîtront dans le tableau." $sel)) { return }
        [void](Remove-FromListFile $excludeFile $sel)
        $dlg.Close()
    })
    $dlg.Controls.Add($btnBack)
    $btnClose = New-Object System.Windows.Forms.Button
    $btnClose.Text = "Fermer"; $btnClose.Location = New-Object System.Drawing.Point(445, 370); $btnClose.Size = New-Object System.Drawing.Size(100, 34)
    $btnClose.Add_Click({ $dlg.Close() })
    $dlg.Controls.Add($btnClose)
    [void]$dlg.ShowDialog($form)
    Update-Data
}

# --- Scheduler: create a schedule (checked computers or all computers to do) ---
function New-Ctl($Type, $Parent, [int]$X, [int]$Y, [int]$W, [int]$H, [string]$Text) {
    $c = New-Object "System.Windows.Forms.$Type"
    $c.Location = New-Object System.Drawing.Point($X, $Y); $c.Size = New-Object System.Drawing.Size($W, $H)
    if ($Text) { $c.Text = $Text }
    $Parent.Controls.Add($c); $c
}
function Show-ScheduleDialog([string[]]$Computers) {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Planifier un déploiement"; $dlg.ClientSize = New-Object System.Drawing.Size(500, 430); $dlg.StartPosition = "CenterParent"
    $dlg.FormBorderStyle = "FixedDialog"; $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false; $dlg.Font = $form.Font
    [void](New-Ctl Label $dlg 15 15 60 22 "Nom :")
    $txtName = New-Ctl TextBox $dlg 80 12 400 24 ("Déploiement du " + (Get-Date -Format 'yyyy-MM-dd'))
    $grpT = New-Ctl GroupBox $dlg 15 45 470 105 "Postes visés"
    $rbSel = New-Ctl RadioButton $grpT 15 25 440 22 ("Les postes cochés ({0}) : {1}" -f $Computers.Count, (($Computers | Select-Object -First 6) -join ', ') + $(if ($Computers.Count -gt 6) { '...' }))
    $rbAll = New-Ctl RadioButton $grpT 15 50 440 22 "Tous les postes à faire, en échec ou incomplets au moment du passage"
    $chkNever = New-Ctl CheckBox $grpT 35 75 420 22 "Inclure aussi les postes « jamais faits »"
    if ($Computers.Count -gt 0) { $rbSel.Checked = $true } else { $rbSel.Enabled = $false; $rbAll.Checked = $true }
    $grpW = New-Ctl GroupBox $dlg 15 158 470 160 "Quand"
    $rbOnce = New-Ctl RadioButton $grpW 15 25 120 22 "Une fois, le"
    $dtOnce = New-Ctl DateTimePicker $grpW 140 23 200 24 ""
    $dtOnce.Format = "Custom"; $dtOnce.CustomFormat = "yyyy-MM-dd HH:mm"
    $dtOnce.Value = (Get-Date).Date.AddHours([Math]::Max((Get-Date).Hour + 1, [int]$script:agentSettings.BusinessHours.End)).AddMinutes(30)
    $rbWeek = New-Ctl RadioButton $grpW 15 60 200 22 "Chaque semaine, les jours :"
    $dayBoxes = [ordered]@{}; $x = 35
    foreach ($d in $dayNames.Keys) { $dayBoxes[$d] = New-Ctl CheckBox $grpW $x 88 58 22 $dayNames[$d]; $dayBoxes[$d].Checked = (@('Saturday', 'Sunday') -notcontains $d); $x += 60 }
    [void](New-Ctl Label $grpW 35 122 40 22 "à")
    $dtTime = New-Ctl DateTimePicker $grpW 75 119 90 24 ""
    $dtTime.Format = "Custom"; $dtTime.CustomFormat = "HH:mm"; $dtTime.ShowUpDown = $true; $dtTime.Value = (Get-Date).Date.AddHours(18).AddMinutes(30)
    $rbOnce.Checked = $true
    $chkQueue = New-Ctl CheckBox $dlg 15 328 470 22 "Mettre en file d'attente les postes qui ne répondent pas au moment du passage"
    $chkQueue.Checked = $true
    $lblNote = New-Ctl Label $dlg 15 352 470 36 "Les postes « Fait » ne sont jamais redéployés. Les postes en violet attendent la fin des heures ouvrables."
    $lblNote.ForeColor = [System.Drawing.Color]::DimGray
    $btnOk = New-Ctl Button $dlg 270 390 110 32 "Planifier"
    $btnCancel = New-Ctl Button $dlg 390 390 95 32 "Annuler"
    $btnCancel.Add_Click({ $dlg.Close() })
    $btnOk.Add_Click({
        if (-not $txtName.Text.Trim()) { Show-Info "Donnez un nom à la planification." "Planifier"; return }
        $days = @($dayBoxes.Keys | Where-Object { $dayBoxes[$_].Checked })
        if ($rbWeek.Checked -and $days.Count -eq 0) { Show-Info "Cochez au moins un jour." "Planifier"; return }
        if ($rbOnce.Checked -and $dtOnce.Value -le (Get-Date)) { Show-Info "Choisissez une date et une heure à venir." "Planifier"; return }
        $sch = [pscustomobject]@{
            Id = [guid]::NewGuid().ToString(); Name = $txtName.Text.Trim(); Enabled = $true
            Targets = $(if ($rbSel.Checked) { "Selected" } else { "AllToDo" }); Computers = @($(if ($rbSel.Checked) { $Computers }))
            IncludeNever = $chkNever.Checked
            Once = $(if ($rbOnce.Checked) { $dtOnce.Value.ToString('s') } else { $null })
            Days = @($(if ($rbWeek.Checked) { $days })); Time = $(if ($rbWeek.Checked) { $dtTime.Value.ToString('HH:mm') } else { $null })
            QueueIfUnavailable = $chkQueue.Checked; LastRun = $null
        }
        $set = Get-AgentSettings
        $set.Schedules = @($set.Schedules) + $sch
        Save-JsonFile $agentSettingsFile $set
        $script:scheduleSaved = $sch
        $dlg.Close()
    })
    $script:scheduleSaved = $null
    [void]$dlg.ShowDialog($form)
    if ($script:scheduleSaved) {
        Update-Data
        $warn = if (-not $script:agentSettings.Enabled) { "`r`n`r`nAttention : l'agent est désactivé (Paramètres de l'agent)." } elseif ($script:agentSettings.DryRun) { "`r`n`r`nL'agent est en mode test : il notera ce qu'il ferait, sans lancer." } else { "" }
        Show-Info ("Planification « {0} » enregistrée : {1}.{2}" -f $script:scheduleSaved.Name, (Format-When $script:scheduleSaved), $warn) "Planifier"
    }
}

# Re-registers the agent task on this VM when its interval changes (same account as the deployments)
function Update-AgentTask([int]$Minutes) {
    # The agent itself runs on this VM as the deployment account (it opens the WinRM sessions): its task needs the password
    try {
        $cred = Get-DeployCredential
        if (-not $cred) { return "mot de passe du compte de déploiement non fourni" }
        $agentScript = Join-Path $scripts "agent.ps1"
        $action  = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$agentScript`""
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $Minutes)
        $tset    = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName "MAJ-Logiciels-Agent" -Action $action -Trigger $trigger -Settings $tset -User $cred.UserName -Password $cred.GetNetworkCredential().Password -RunLevel Highest -Force -ErrorAction Stop | Out-Null
        "OK"
    } catch { $_.Exception.Message }
}

# --- Agent settings window: agent, business hours, queue, schedules ---
function Show-AgentSettingsDialog {
    $set = Get-AgentSettings
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Paramètres de l'agent"; $dlg.ClientSize = New-Object System.Drawing.Size(700, 600); $dlg.StartPosition = "CenterParent"
    $dlg.FormBorderStyle = "FixedDialog"; $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false; $dlg.Font = $form.Font

    $gA = New-Ctl GroupBox $dlg 15 10 330 135 "Agent"
    $chkOn  = New-Ctl CheckBox $gA 15 25 300 22 "Agent actif"; $chkOn.Checked = [bool]$set.Enabled
    $chkDry = New-Ctl CheckBox $gA 15 50 300 22 "Mode test (note ce qu'il ferait, ne lance rien)"; $chkDry.Checked = [bool]$set.DryRun
    [void](New-Ctl Label $gA 15 80 200 22 "Passage toutes les (minutes) :")
    $numInt = New-Ctl NumericUpDown $gA 230 78 80 24 ""; $numInt.Minimum = 5; $numInt.Maximum = 120; $numInt.Value = [int]$set.IntervalMinutes
    [void](New-Ctl Label $gA 15 106 200 22 "Postes lancés au plus par passage :")
    $numMax = New-Ctl NumericUpDown $gA 230 104 80 24 ""; $numMax.Minimum = 1; $numMax.Maximum = 200; $numMax.Value = [int]$set.MaxPerRun

    $gB = New-Ctl GroupBox $dlg 355 10 330 135 "Heures ouvrables"
    $bhBoxes = [ordered]@{}; $x = 12
    foreach ($d in $dayNames.Keys) { $bhBoxes[$d] = New-Ctl CheckBox $gB $x 28 44 22 $dayNames[$d]; $bhBoxes[$d].Checked = (@($set.BusinessHours.Days) -contains $d); $x += 45 }
    [void](New-Ctl Label $gB 15 66 40 22 "De")
    $numStart = New-Ctl NumericUpDown $gB 55 64 60 24 ""; $numStart.Minimum = 0; $numStart.Maximum = 23; $numStart.Value = [int]$set.BusinessHours.Start
    [void](New-Ctl Label $gB 125 66 30 22 "h à")
    $numEnd = New-Ctl NumericUpDown $gB 160 64 60 24 ""; $numEnd.Minimum = 1; $numEnd.Maximum = 24; $numEnd.Value = [int]$set.BusinessHours.End
    [void](New-Ctl Label $gB 225 66 20 22 "h")
    $lblBh = New-Ctl Label $gB 15 96 305 34 "Servent aux postes en violet, à la file et à « Cocher les postes à faire »."; $lblBh.ForeColor = [System.Drawing.Color]::DimGray

    $gQ = New-Ctl GroupBox $dlg 15 152 670 85 "File d'attente « dès que disponible »"
    $chkQ = New-Ctl CheckBox $gQ 15 25 140 22 "File active"; $chkQ.Checked = [bool]$set.Queue.Enabled
    $rbAny = New-Ctl RadioButton $gQ 160 25 160 22 "À toute heure"
    $rbOff = New-Ctl RadioButton $gQ 330 25 330 22 "Seulement en dehors des heures ouvrables"
    if ($set.Queue.When -eq 'Anytime') { $rbAny.Checked = $true } else { $rbOff.Checked = $true }
    [void](New-Ctl Label $gQ 15 55 300 22 "Retirer un poste de la file après (jours) :")
    $numDrop = New-Ctl NumericUpDown $gQ 320 53 70 24 ""; $numDrop.Minimum = 1; $numDrop.Maximum = 90; $numDrop.Value = [int]$set.Queue.DropAfterDays

    $gS = New-Ctl GroupBox $dlg 15 244 670 300 "Planifications"
    $lvS = New-Ctl ListView $gS 15 25 640 225 ""
    $lvS.View = "Details"; $lvS.FullRowSelect = $true; $lvS.GridLines = $true; $lvS.HideSelection = $false
    foreach ($h in @(@("Nom", 170), @("Postes", 150), @("Quand", 130), @("Prochain passage", 110), @("État", 70))) { [void]$lvS.Columns.Add($h[0], $h[1]) }
    $schedules = New-Object System.Collections.ArrayList
    foreach ($sch in @($set.Schedules)) { [void]$schedules.Add($sch) }
    $fill = {
        $lvS.Items.Clear()
        foreach ($sch in $schedules) {
            $it = New-Object System.Windows.Forms.ListViewItem([string]$sch.Name)
            [void]$it.SubItems.Add($(if ($sch.Targets -eq 'Selected') { "$(@($sch.Computers).Count) poste(s) : " + (@($sch.Computers) -join ', ') } else { "Tous les postes à faire" + $(if ($sch.IncludeNever) { " + jamais faits" }) }))
            [void]$it.SubItems.Add((Format-When $sch))
            $nr = Get-NextRun $sch
            [void]$it.SubItems.Add($(if ($nr) { $nr.ToString('yyyy-MM-dd HH:mm') } elseif ($sch.Once -and $sch.LastRun) { "fait le " + ([datetime]$sch.LastRun).ToString('MM-dd HH:mm') } else { "-" }))
            [void]$it.SubItems.Add($(if ($sch.Enabled) { "Active" } else { "Suspendue" }))
            if (-not $sch.Enabled) { $it.ForeColor = [System.Drawing.Color]::Gray }
            [void]$lvS.Items.Add($it)
        }
    }
    & $fill
    $btnToggle = New-Ctl Button $gS 15 258 180 32 "Activer / suspendre"
    $btnToggle.Add_Click({ foreach ($i in @($lvS.SelectedIndices)) { $schedules[$i].Enabled = -not $schedules[$i].Enabled }; & $fill })
    $btnDel = New-Ctl Button $gS 205 258 120 32 "Supprimer"
    $btnDel.Add_Click({
        $idx = @($lvS.SelectedIndices | Sort-Object -Descending)
        if ($idx.Count -eq 0) { return }
        if (-not (Confirm-Action "Supprimer ces planifications ?" @($idx | ForEach-Object { $schedules[$_].Name }))) { return }
        foreach ($i in $idx) { $schedules.RemoveAt($i) }
        & $fill
    })
    $btnNewAll = New-Ctl Button $gS 335 258 320 32 "Nouvelle planification « tous les postes à faire »..."
    $btnNewAll.Add_Click({ $dlg.Tag = "new"; $dlg.Close() })

    $btnSave = New-Ctl Button $dlg 455 555 120 34 "Enregistrer"
    $btnCancel = New-Ctl Button $dlg 585 555 100 34 "Annuler"
    $btnCancel.Add_Click({ $dlg.Close() })
    $btnSave.Add_Click({
        $days = @($bhBoxes.Keys | Where-Object { $bhBoxes[$_].Checked })
        if ([int]$numEnd.Value -le [int]$numStart.Value) { Show-Info "L'heure de fin doit être après l'heure de début." "Paramètres"; return }
        $oldInterval = [int]$set.IntervalMinutes
        $set.Enabled = $chkOn.Checked; $set.DryRun = $chkDry.Checked
        $set.IntervalMinutes = [int]$numInt.Value; $set.MaxPerRun = [int]$numMax.Value
        $set.BusinessHours = [pscustomobject]@{ Days = $days; Start = [int]$numStart.Value; End = [int]$numEnd.Value }
        $set.Queue = [pscustomobject]@{ Enabled = $chkQ.Checked; When = $(if ($rbAny.Checked) { "Anytime" } else { "OutsideBusinessHours" }); DropAfterDays = [int]$numDrop.Value }
        $set.Schedules = @($schedules)
        Save-JsonFile $agentSettingsFile $set
        if ($set.IntervalMinutes -ne $oldInterval) {
            $r = Update-AgentTask $set.IntervalMinutes
            if ($r -ne "OK") { Show-Info "Paramètres enregistrés, mais la tâche de l'agent n'a pas pu être mise à jour : $r" "Paramètres" }
        }
        $dlg.Tag = "saved"; $dlg.Close()
    })
    [void]$dlg.ShowDialog($form)
    Update-Data
    if ($dlg.Tag -eq "new") { Show-ScheduleDialog @() }
}

# --- Help window: "?" button and F1 ---
$helpText = @(
    ("AIDE - MISES À JOUR DES LOGICIELS" + $(if ($orgName) { " ($orgName)" })),
    '',
    ("Le programme suit les mises à jour de " + (@($script:tracked | ForEach-Object { $_.Label }) -join ", ") + " sur les postes Windows, les déploie, et installe des logiciels du catalogue. Il tourne sur la VM de l'équipe TI, avec le compte de déploiement. Liste des logiciels suivis : packages.txt et package.json (modules) sur le partage."),
    '',
    '1. LE TABLEAU',
    'Une ligne par poste Windows 10 ou 11 actif dans l''Active Directory. Les postes exclus (exclude.txt) et les serveurs n''y apparaissent pas.',
    '',
    'État',
    '  Fait : le dernier passage du paquet a réussi, après la plus récente version connue.',
    '  À faire : une version plus récente est sortie depuis le dernier passage.',
    '  Échec : le dernier passage a échoué (le code et la raison sont dans Détail).',
    '  Incomplet : passage interrompu, ou le travailleur n''a rien écrit.',
    '  Jamais fait : le paquet n''a jamais été déployé sur ce poste.',
    '  Pendant un déploiement ou une installation : En cours, Installation, Terminé, Installé, Échec.',
    '',
    'Forme devant le nom (passer la souris dessus donne la raison exacte)',
    '  Rond vert : en ligne, identité du poste confirmée.',
    '  Triangle jaune : en ligne, mais identité non vérifiée (WinRM n''a pas répondu). Le déploiement fonctionne quand même.',
    '  Rond rouge : hors ligne, partage de fichiers bloqué, mauvais poste (adresse DNS périmée) ou pas de DNS.',
    '  Nom en violet gras : poste « en dehors des heures ouvrables » (jamais déployé automatiquement pendant les heures ouvrables).',
    '',
    'Colonnes',
    '  Dernière connexion : dernière ouverture de session du poste sur le domaine (en rouge : plus de 90 jours).',
    '  Dernier passage : date du dernier journal du paquet.',
    '  Détail : résumé du dernier passage, ou progression en cours.',
    '  Remarque : heures ouvrables, file d''attente (depuis combien de jours), planification.',
    '  Usager : personne connectée à l''écran du poste.',
    '  Redémarré : dernier redémarrage (en rouge : redémarrage en attente, ou plus de 7 jours).',
    '',
    'Clic sur un en-tête : trier. Glisser un en-tête : déplacer la colonne. Double-clic sur un poste : sa fiche complète.',
    'La disposition des colonnes et de la fenêtre est retenue à la fermeture.',
    '',
    '2. LES CASES DU HAUT',
    '  Échec, Incomplet, À faire, Jamais fait, Fait, En file, Violet : un clic n''affiche que ces postes, un 2e clic affiche tout.',
    '  En ligne, Hors ligne : filtre réseau, qui se combine avec les cases d''état (ex. : À faire + En ligne).',
    '  Inactifs : postes sans connexion au domaine depuis plus de 90 jours (oubliés dans l''AD ?).',
    '  Référence : la plus récente version connue, qui détermine Fait ou À faire.',
    '  Ligne de l''agent : orange = mode test, vert = actif, rouge = mot de passe manquant. Un clic ouvre son journal.',
    '',
    '3. ACTUALISER',
    'Relit l''AD et les journaux, puis vérifie le réseau de tout le parc : DNS (posé directement au serveur), ping, partage de fichiers (port 445) et identité de chaque poste (WinRM).',
    'L''identité d''un poste confirmée depuis moins de 4 heures, à la même adresse, n''est pas revérifiée (plus rapide).',
    'Maj + clic sur Actualiser : tout revérifier, y compris les identités.',
    '',
    '4. LES BOUTONS',
    'Tout le parc',
    '  Nouvelles versions sur le web : vérifie si de nouvelles versions sont sorties (sans contacter les postes).',
    '  Vérifier tout le parc : interroge chaque poste et compare les versions installées.',
    '  Ouvrir le dernier rapport : le dernier rapport de vérification, dans Excel.',
    '  Postes exclus : liste de exclude.txt, avec possibilité de réintégrer.',
    '  Paramètres de l''agent : agent, heures ouvrables, file d''attente, planifications.',
    'Postes cochés',
    '  Déployer maintenant : lance le paquet sur les postes cochés et joignables, avec suivi dans le tableau (en SYSTEM par défaut ; LaunchAs dans config.json).',
    '  Suivre en direct : une fenêtre de suivi par poste (10 au maximum).',
    '  Vérifier : compare les versions installées sur les postes cochés.',
    '  Planifier les postes cochés : déploiement par l''agent, une fois ou chaque semaine.',
    '  Créer la liste PDQ : fichier de postes joignables pour PDQ Deploy.',
    'Sous le tableau : Cocher les postes à faire (laisse décochés les postes en violet pendant les heures ouvrables), Tout décocher.',
    '',
    '5. LE CLIC DROIT (sur les lignes sélectionnées, ou cochées)',
    '  Exclure : le poste disparaît, et le paquet refusera de s''y exécuter.',
    '  Marquer / retirer « en dehors des heures ouvrables ».',
    '  Déployer dès que disponible : met le poste en file d''attente pour l''agent. Retirer de la file d''attente.',
    '  Copier le détail : copie la bulle d''aide (réseau, dernière connexion) dans le presse-papiers.',
    '  Actualiser ce poste : vérification complète de ce poste seulement.',
    '  Redémarrer : avec un délai et un avertissement à l''usager. Annuler un redémarrage prévu.',
    '  Envoyer un message à l''usager.',
    '  Ouvrir C$.',
    '  Réveiller (Wake-on-LAN) : seulement si le BIOS le permet, surtout en câble, et sur le même sous-réseau que la VM.',
    '  Installer un logiciel : inventaire du catalogue sur les postes, puis installation en SYSTEM de ce qui manque.',
    '  Colonnes affichées : choisir les colonnes, ou revenir à la disposition par défaut.',
    '',
    '6. L''AGENT',
    'Tâche planifiée MAJ-Logiciels-Agent sur la VM, avec le compte de déploiement, toutes les 15 minutes (réglable).',
    'À chaque passage : il exécute les planifications dues, puis déploie les postes de la file d''attente qui répondent.',
    'Il respecte les heures ouvrables, le nombre maximum de postes par passage, et réveille par Wake-on-LAN les postes d''une planification qui ne répondent pas.',
    'En mode test, il écrit seulement ce qu''il ferait (« would launch ») dans son journal, sans rien lancer.',
    '',
    '7. LES FICHIERS',
    "Sur le partage $deployRoot",
    '  Winget-upgrade : le paquet (winget-upgrade.cmd, winget-upgrade.ps1, winget-worker.ps1, packages.txt) et catalog.json (catalogue d''installation).',
    '  Winget-upgrade-logs : un journal par poste, exclude.txt, night-only.txt (postes en violet), last-check.json (versions de référence).',
    '  Winget-upgrade\package.json : modules optionnels (Adobe Reader, 1Password) et messages aux usagers.',
    '  Adobe, Adobe-x64, 1Password : installeurs et correctifs des modules activés.',
    "Sur la VM, $agentBase",
    '  Outils : outils-maj.ps1 (ce programme), agent.ps1, scripts de vérification, config.ps1, config.json (réglages du site) et profil.ps1 (export/import du profil de site).',
    '  agent-settings.json, agent-queue.json, agent-status.json, mac-cache.json.',
    '  Rapports\Agent : journal de l''agent (agent-AAAA-MM.log) et panel.log (enregistrement ou effacement du mot de passe).',
    '  Rapports\Verification : rapports de vérification du parc.',
    'Dans le profil, %LOCALAPPDATA%\MAJ-Logiciels',
    '  state.bin : mot de passe du compte de déploiement, chiffré (lisible seulement par ce compte, sur cette VM).',
    '  layout.json : disposition des colonnes et de la fenêtre.',
    'Sur chaque poste, C:\ProgramData\MAJ-Logiciels (accessible seulement à SYSTEM et aux administrateurs)',
    '  winget-upgrade\winget-upgrade.log : journal du dernier passage du paquet.',
    '  install : journaux des installations (install.log, winget-1.log, ...).',
    '',
    '8. EN CAS DE PROBLÈME',
    'Le programme ne répond plus : le fermer par le Gestionnaire des tâches (processus powershell.exe du programme).',
    'Une erreur ne s''affiche pas : lancer le programme depuis une console PowerShell pour voir les erreurs en rouge :',
    "  powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$agentBase\Outils\outils-maj.ps1`"",
    "Le mot de passe est redemandé : voir la raison dans $agentBase\Rapports\Agent\panel.log.",
    'Un poste est rouge alors qu''il fonctionne : lire sa bulle d''aide. « Mauvais poste » = enregistrement DNS périmé à supprimer dans la console DNS.',
    'Une installation semble longue : Adobe Reader peut prendre 10 à 16 minutes. Le programme abandonne le suivi après 30 minutes.',
    'Revenir à la version précédente (programme fermé) : recopier dans le dossier Outils les scripts de la version publiée précédente (release GitHub). config.json est conservé.',
    'Toute demande de changement ou de correction : passer par l''équipe TI.'
) -join "`r`n"
function Show-Help {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Aide - Mises à jour des logiciels"; $dlg.ClientSize = New-Object System.Drawing.Size(820, 640); $dlg.StartPosition = "CenterParent"; $dlg.Font = $form.Font
    $dlg.MinimumSize = New-Object System.Drawing.Size(500, 400)
    $tb = New-Object System.Windows.Forms.TextBox
    $tb.Multiline = $true; $tb.ReadOnly = $true; $tb.ScrollBars = "Vertical"; $tb.WordWrap = $true; $tb.BackColor = [System.Drawing.Color]::White
    $tb.Font = New-Object System.Drawing.Font("Segoe UI", 10); $tb.Location = New-Object System.Drawing.Point(12, 12); $tb.Size = New-Object System.Drawing.Size(796, 572)
    $tb.Anchor = "Top, Bottom, Left, Right"; $tb.Text = $helpText; $tb.SelectionStart = 0
    $bClose = New-Object System.Windows.Forms.Button; $bClose.Text = "Fermer"; $bClose.Location = New-Object System.Drawing.Point(708, 596); $bClose.Size = New-Object System.Drawing.Size(100, 32); $bClose.Anchor = "Bottom, Right"
    $bClose.Add_Click({ $dlg.Close() })
    $dlg.Controls.AddRange(@($tb, $bClose)); $dlg.CancelButton = $bClose
    [void]$dlg.ShowDialog($form)
}
New-Button $form "?" 530 68 28 "Aide (F1)" { Show-Help }
$form.KeyPreview = $true
$form.Add_KeyDown({ param($s, $e) if ($e.KeyCode -eq 'F1') { Show-Help; $e.Handled = $true } })
# --- Right, top: whole fleet ---
$grpFleet = New-Object System.Windows.Forms.GroupBox
$grpFleet.Text = "Tout le parc"; $grpFleet.Location = New-Object System.Drawing.Point(610, 96); $grpFleet.Size = New-Object System.Drawing.Size(315, 254)
$form.Controls.Add($grpFleet)
New-Button $grpFleet "Nouvelles versions sur le web" 15 28 285 "Rapide, sans contacter les postes : y a-t-il des versions pas encore déployées? Le tableau s'actualise ensuite." {
    $cu = Join-Path $scripts "check-updates.ps1"
    if (-not (Test-Path $cu)) { Show-Info "Script introuvable : $cu" "Nouvelles versions"; return }
    $before = @($script:rows | Where-Object { $_.Status -eq 'TODO' }).Count
    $out = Join-Path $env:TEMP "check-updates-output.txt"
    Show-Loading "Recherche de nouvelles versions sur le web...`r`n`r`nwinget et modules activés (environ 30 secondes)"
    $p = Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$cu`"" -WindowStyle Hidden -RedirectStandardOutput $out -PassThru
    while (-not $p.HasExited) { Start-Sleep -Milliseconds 300; [System.Windows.Forms.Application]::DoEvents() }
    Hide-Loading
    $lines = @(Get-Content $out -Encoding UTF8 -ErrorAction SilentlyContinue)
    $news  = @($lines | Where-Object { $_ -match '\s(NEW|PENDING)\s' } | ForEach-Object { "  " + (($_ -replace '\s{2,}', '  ').Trim()) })
    $fail  = @($lines | Where-Object { $_ -match 'NOT FOUND|Could not save' } | ForEach-Object { "  " + (($_ -replace '\s{2,}', '  ').Trim()) })
    Update-Data
    $after = @($script:rows | Where-Object { $_.Status -eq 'TODO' }).Count
    $msg = if ($news.Count) { "Versions pas encore déployées :`r`n" + ($news -join "`r`n") } else { "Rien de nouveau : toutes les versions vues ont été déployées depuis." }
    $msg += "`r`n`r`nPostes « À faire » : $before avant, $after maintenant."
    if ($fail.Count) { $msg += "`r`n`r`nÀ vérifier :`r`n" + ($fail -join "`r`n") }
    $msg += "`r`n`r`nVoir le détail complet ?"
    if ([System.Windows.Forms.MessageBox]::Show((Get-MsgOwner), $msg, "Nouvelles versions sur le web", "YesNo", "Information") -eq "Yes") { Start-Process notepad.exe -ArgumentList "`"$out`"" }
}
New-Button $grpFleet "Vérifier tout le parc" 15 72 285 "Interroge les postes et compare les versions réellement installées" {
    Start-Tool "verify-winget-upgrade.ps1" ""
}
New-Button $grpFleet "Ouvrir le dernier rapport" 15 116 285 "Ouvre dans Excel le dernier CSV de la vérification" {
    $last = Get-ChildItem "$agentBase\Rapports\Verification" -Filter "winget-upgrade-report-*.csv" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($last) { Invoke-Item $last.FullName } else { Show-Info "Aucun rapport dans $agentBase\Rapports\Verification. Lancez d'abord une vérification." "Aucun rapport" }
}
New-Button $grpFleet "Postes exclus..." 15 160 285 "Affiche les postes de exclude.txt et permet de les réintégrer. Pour exclure : clic droit sur un poste du tableau." { Show-ExcludedDialog }
New-Button $grpFleet "Paramètres de l'agent..." 15 204 285 "Agent, heures ouvrables, file d'attente et planifications" { Show-AgentSettingsDialog }

# --- Deployment without PDQ: deployment account password, saved encrypted (DPAPI: only this account on this VM can read it) ---
$credFile = Join-Path $env:LOCALAPPDATA "MAJ-Logiciels\state.bin"
# Every save or removal of the saved password is written here, with the reason
function Write-PanelLog([string]$Message) {
    try { "{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message | Add-Content -Path (Join-Path $agentLogDir "panel.log") -Encoding UTF8 } catch { }
}
function Get-DeployCredential {
    if (Test-Path $credFile) {
        try { return (Import-Clixml $credFile) } catch {
            Write-PanelLog "Saved password could not be read ($($_.Exception.Message)): removed, asked again"
            Show-Info "Le mot de passe enregistré n'a pas pu être relu : il va être redemandé.`r`n`r`n$($_.Exception.Message)" "Déploiement"
            Remove-Item $credFile -Force -ErrorAction SilentlyContinue
        }
    }
    $c = Get-Credential -UserName $deployAccount -Message "Mot de passe du compte de déploiement (enregistré chiffré, pour cette VM et ce compte seulement)"
    if (-not $c) { return $null }
    # One check against a domain controller, never retried automatically (failed tries count toward account lockout)
    Add-Type -AssemblyName System.DirectoryServices.AccountManagement
    $ctx = New-Object System.DirectoryServices.AccountManagement.PrincipalContext('Domain')
    $ok  = $ctx.ValidateCredentials(($c.UserName -replace '^.*\\', ''), $c.GetNetworkCredential().Password)
    $ctx.Dispose()
    if (-not $ok) { Show-Info "Mot de passe refusé par le domaine : rien n'a été enregistré." "Déploiement"; return $null }
    New-Item -Path (Split-Path $credFile) -ItemType Directory -Force | Out-Null
    $c | Export-Clixml $credFile
    (Get-Item $credFile -Force).Attributes += 'Hidden'
    Write-PanelLog "Password saved for $($c.UserName) (validated by the domain)"
    $c
}

# Starts the package on each computer through a one-time scheduled task. LaunchAs (config.json):
#  - System (default): the task runs as SYSTEM, the computer account reads the share; no password is sent or stored
#  - DeployAccount: the task runs as the deployment account (same account as PDQ; Windows stores its password)
# The task starts 20 s later and deletes itself afterwards.
function Start-RemoteDeploy([string[]]$Names, [System.Management.Automation.PSCredential]$Cred) {
    $user = if ($Cred) { $Cred.UserName } else { $null }
    $pw   = if ($Cred) { $Cred.GetNetworkCredential().Password } else { $null }
    $launcher = "$deployRoot\Winget-upgrade\winget-upgrade.ps1"
    $opt = New-PSSessionOption -OpenTimeout 10000 -OperationTimeout 60000
    $res = @(Invoke-Command -ComputerName $Names -SessionOption $opt -ThrottleLimit 30 -ErrorAction SilentlyContinue -ErrorVariable remoteErrors -ArgumentList $user, $pw, $launcher -ScriptBlock {
        param($user, $pw, $launcher)
        try {
            $name     = "MAJ-Logiciels-Launch"
            $action   = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$launcher`""
            $trigger  = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(20)
            $trigger.EndBoundary = (Get-Date).AddHours(1).ToString('s')
            $settings = New-ScheduledTaskSettingsSet -DeleteExpiredTaskAfter (New-TimeSpan -Minutes 30) -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
                                                     -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
            if ($user) {
                Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Settings $settings -User $user -Password $pw -RunLevel Highest -Force -ErrorAction Stop | Out-Null
            } else {
                $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
                Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force -ErrorAction Stop | Out-Null
            }
            [pscustomobject]@{ Result = "OK"; Detail = "" }
        } catch {
            [pscustomobject]@{ Result = "ERROR"; Detail = $_.Exception.Message }
        }
    })
    $pw = $null
    $out = @{}
    foreach ($r in $res) { $out[$r.PSComputerName] = $r }
    foreach ($e in $remoteErrors) { $out[[string]$e.TargetObject] = [pscustomobject]@{ Result = "ERROR"; Detail = "connexion impossible : $($e.Exception.Message)" } }
    $out
}

# --- Live progress of a deployment started from this program (reads each computer's log through \\POSTE\C$) ---
$spinFrames = @("$([char]0x25CF)$([char]0x25CB)$([char]0x25CB)", "$([char]0x25CB)$([char]0x25CF)$([char]0x25CB)", "$([char]0x25CB)$([char]0x25CB)$([char]0x25CF)")
$script:deploy = $null; $script:spin = 0; $script:tick = 0
$shortNames = @{}; foreach ($t in $script:tracked) { if ($t.Id) { $shortNames[$t.Id] = $t.Label } }

# Estimated progress from the log: preparation (launcher), then each winget package, Adobe, 1Password, summary, end
function Get-DeployProgress([string]$Name, [datetime]$Since) {
    # Log of the package on the computer (ProgramData since the protected work folder; Windows\Temp before)
    $logPath = "\\$Name\C$\ProgramData\MAJ-Logiciels\winget-upgrade\winget-upgrade.log"
    if (-not (Test-Path $logPath)) { $logPath = "\\$Name\C$\Windows\Temp\winget-upgrade\winget-upgrade.log" }
    $lines = @(Get-Content $logPath -Encoding UTF8 -ErrorAction SilentlyContinue)
    $first = if ($lines.Count -and $lines[0] -match '^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)') { [datetime]$Matches[1] } else { $null }
    if (-not $first -or $first -lt $Since.AddSeconds(-60)) { return @{ Pct = 2; Text = "En attente du démarrage"; Done = $false } }
    $final = $lines | Where-Object { $_ -match 'Launcher: .*exit (\d+)\s*$' } | Select-Object -Last 1
    if ($final -match 'exit (\d+)\s*$') {
        $code = [int]$Matches[1]
        return @{ Pct = 100; Text = $(if ($code -eq 0) { "Terminé" } else { "Échec (code $code)" }); Done = $true; Ok = ($code -eq 0) }
    }
    if ($lines -match 'Launcher: .*nothing done') { return @{ Pct = 100; Text = "Rien à faire (exclu ou serveur)"; Done = $true; Ok = $true } }
    if ($lines -match 'Summary:') { return @{ Pct = 96; Text = "Finalisation"; Done = $false } }
    $last = $lines[-1]
    if ($lines -match '1Password : msiexec|1Password : install stalled') { return @{ Pct = 92; Text = "1Password"; Done = $false } }
    if ($lines -match 'Adobe .*: msiexec|Adobe : user warned') { return @{ Pct = 88; Text = $(if ($last -match 'user warned') { "Adobe (avertissement à l'usager)" } else { "Adobe" }); Done = $false } }
    $run = $lines | Where-Object { $_ -match 'Running as .*, (\d+) winget package' } | Select-Object -First 1
    if ($run -match ', (\d+) winget package') {
        $n = [Math]::Max(1, [int]$Matches[1])
        $ids = @($lines | ForEach-Object { if ($_ -match '^\S+ \S+ ([\w\+\-]+(\.[\w\+\-]+)+) : ') { $Matches[1] } } | Select-Object -Unique)
        # The worker writes a line when a package is done: the one in progress is the next one of packages.txt
        if (-not $script:pkgOrder) { $script:pkgOrder = @(Get-Content "$deployRoot\Winget-upgrade\packages.txt" -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { ($_ -split '\|')[0].Trim() }) }
        $current = if ($ids.Count -lt $script:pkgOrder.Count -and (-not $ids.Count -or $script:pkgOrder[$ids.Count - 1] -eq $ids[-1])) { $script:pkgOrder[$ids.Count] } elseif ($ids.Count) { $ids[-1] } else { "" }
        $label = if ($shortNames.ContainsKey($current)) { $shortNames[$current] } elseif ($current) { $current } else { "winget" }
        if ($last -match 'user warned') { $label += " (avertissement à l'usager)" }
        return @{ Pct = [Math]::Min(85, 30 + [int](55 * [Math]::Max(0, $ids.Count - 0.5) / $n)); Text = $label; Done = $false }
    }
    if ($lines -match '1Password: ') { return @{ Pct = 22; Text = "Préparation 1Password"; Done = $false } }
    if ($lines -match 'Adobe') { return @{ Pct = 12; Text = "Préparation Adobe"; Done = $false } }
    @{ Pct = 6; Text = "Démarrage"; Done = $false }
}

# Writes the progress of a tracked computer into its row (State and Detail columns)
function Set-RowProgress($Item) {
    $ins = if ($script:installs) { $script:installs[$Item.Text] } else { $null }
    if ($ins) {
        $Item.SubItems[1].Text = $(if ($ins.Done) { if ($ins.Ok) { "Installé" } else { "Échec" } } else { "Installation" })
        $Item.SubItems[4].Text = $(if ($ins.Done) { $ins.Text } else { "{0}  {1}" -f $spinFrames[$script:spin], $ins.Text })
        return
    }
    if (-not $script:deploy -or -not $script:deploy.Active) { return }
    $p = $script:deploy.Items[$Item.Text]
    if (-not $p) { return }
    $Item.SubItems[1].Text = $(if ($p.Done) { if ($p.Ok) { "Terminé" } else { "Échec" } } else { "En cours" })
    $Item.SubItems[4].Text = $(if ($p.Done) { $p.Text } else { "{0}  {1} % – {2}" -f $spinFrames[$script:spin], $p.Pct, $p.Text })
}

function Show-DeployProgress {
    foreach ($it in $lv.Items) { Set-RowProgress $it }
    $all = @($script:deploy.Items.Values)
    $done = @($all | Where-Object { $_.Done }).Count
    $pct = [int](($all | ForEach-Object { $_.Pct } | Measure-Object -Average).Average)
    $pbDeploy.Value = [Math]::Min(100, [Math]::Max(0, $pct))
    $lblDeploy.Text = "Déploiement : {0}/{1} terminé(s) · {2} %" -f $done, $all.Count, $pct
}

function Update-DeployProgress {
    foreach ($n in @($script:deploy.Items.Keys)) {
        if ($script:deploy.Items[$n].Done) { continue }
        $start   = [datetime]$script:deploy.Items[$n].Start
        $elapsed = ((Get-Date) - $start).TotalMinutes
        $p = Get-DeployProgress $n $start
        if (-not $p.Done -and $p.Pct -le 2 -and $elapsed -gt 4) { $p = @{ Pct = 100; Text = "Pas démarré (tâche non lancée sur le poste)"; Done = $true; Ok = $false } }
        elseif (-not $p.Done -and $elapsed -gt 55) { $p = @{ Pct = 100; Text = "Délai dépassé (voir le journal)"; Done = $true; Ok = $false } }
        $p.Start = $start
        $script:deploy.Items[$n] = $p
    }
    if (@($script:deploy.Items.Values | Where-Object { -not $_.Done }).Count -eq 0) {
        if (-not (Test-InstallsActive)) { $timer.Stop() }
        $okCount  = @($script:deploy.Items.Values | Where-Object { $_.Ok }).Count
        $badNames = @($script:deploy.Items.GetEnumerator() | Where-Object { -not $_.Value.Ok } | ForEach-Object { "$($_.Key) : $($_.Value.Text)" })
        Show-DeployProgress
        $script:deploy.Active = $false
        $lblDeploy.Text = "Déploiement terminé à {0} : {1} réussi(s), {2} échec(s)" -f (Get-Date -Format 'HH:mm'), $okCount, $badNames.Count
        Update-Data
        if ($badNames.Count -gt 0) { Show-Info ("Déploiement terminé.`r`n`r`nÀ vérifier :`r`n  " + ($badNames -join "`r`n  ")) "Déploiement" }
    }
}

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 750
$timer.Add_Tick({
    $script:spin = ($script:spin + 1) % 3
    $script:tick++
    if ($script:tick % 4 -eq 0) {   # read the logs every ~3 s
        if ($script:deploy -and $script:deploy.Active) { Update-DeployProgress }
        if (Test-InstallsActive) { Update-InstallProgress }
    }
    if ($script:deploy -and $script:deploy.Active) { Show-DeployProgress }
    if (Test-InstallsActive) { foreach ($it in $lv.Items) { Set-RowProgress $it } }
})

# A new deployment is added to the ones still followed (each computer keeps its own start time)
function Start-DeployTracking([string[]]$Names, [datetime]$At = (Get-Date)) {
    if (-not $script:deploy -or -not $script:deploy.Active) { $script:deploy = @{ Items = @{}; Active = $true } }
    foreach ($n in $Names) { $script:deploy.Items[$n] = @{ Pct = 2; Text = "En attente du démarrage"; Done = $false; Start = $At } }
    $lblHelp.Visible = $false; $pbDeploy.Visible = $true; $lblDeploy.Visible = $true
    $script:tick = 0
    Show-DeployProgress
    $timer.Start()
}

# --- Right, bottom: checked computers ---
$grpSel = New-Object System.Windows.Forms.GroupBox
$grpSel.Text = "Postes cochés"; $grpSel.Location = New-Object System.Drawing.Point(610, 358); $grpSel.Size = New-Object System.Drawing.Size(315, 254)
$form.Controls.Add($grpSel)
# Reachability as PDQ needs it: name in DNS, unique address, SMB (TCP 445) answering. Tests run in parallel.
function Test-Reachable([string[]]$Names) {
    $res = @{}; $ips = @{}
    foreach ($n in $Names) {
        $ip = $null
        try { $ip = [Net.Dns]::GetHostAddresses($n) | Where-Object { $_.AddressFamily -eq 'InterNetwork' } | Select-Object -First 1 } catch { }
        if ($ip) { $ips[$n] = $ip } else { $res[$n] = "pas de DNS" }
    }
    # Two names on the same address: stale DNS, we cannot know which computer answers
    $ips.GetEnumerator() | Group-Object { $_.Value.ToString() } | Where-Object Count -gt 1 | ForEach-Object {
        $names = @($_.Group | ForEach-Object Key)
        foreach ($n in $names) { $res[$n] = "DNS en double ($($_.Name), aussi $(($names | Where-Object { $_ -ne $n }) -join ', '))"; [void]$ips.Remove($n) }
    }
    $probes = foreach ($kv in @($ips.GetEnumerator())) {
        $c = New-Object Net.Sockets.TcpClient
        [pscustomobject]@{ Name = $kv.Key; Client = $c; Task = $c.ConnectAsync($kv.Value, 445) }
    }
    Start-Sleep -Milliseconds 2500
    foreach ($pr in $probes) {
        $ok = $pr.Task.IsCompleted -and -not $pr.Task.IsFaulted -and $pr.Client.Connected
        $res[$pr.Name] = if ($ok) { "OK" } else { "aucune réponse" }
        $pr.Client.Close()
    }
    $res
}

New-Button $grpSel "Planifier les postes cochés..." 15 160 285 "Déployer les postes cochés à une date et une heure, une fois ou chaque semaine (par l'agent)" {
    Show-ScheduleDialog @($checked | Sort-Object)
}
New-Button $grpSel "Créer la liste PDQ" 15 204 285 "Vérifie que les postes cochés sont joignables, puis écrit ceux qui le sont dans deploy-todo.txt (PDQ : Deploy Once > Choose Targets > Text File)" {
    if ($checked.Count -eq 0) { Show-Info "Cochez au moins un poste." "Liste PDQ"; return }
    $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    $reach = Test-Reachable @($checked)
    $form.Cursor = [System.Windows.Forms.Cursors]::Default
    $ok  = @($reach.GetEnumerator() | Where-Object Value -eq "OK" | ForEach-Object Key | Sort-Object)
    $bad = @($reach.GetEnumerator() | Where-Object Value -ne "OK" | Sort-Object Key | ForEach-Object { "  $($_.Key) : $($_.Value)" })
    $file = "$agentBase\PDQ\deploy-todo.txt"
    $ok | Set-Content -Path $file -Encoding ASCII
    $text = "{0} poste(s) joignable(s) écrit(s) dans {1}" -f $ok.Count, $file
    if ($bad.Count -gt 0) { $text += "`r`n`r`nÉcartés ({0}) :`r`n{1}" -f $bad.Count, ($bad -join "`r`n") }
    $nightNow = @($ok | Where-Object { $script:nightOnly.Contains($_) })
    if ($nightNow.Count -gt 0 -and (Test-BusinessHours)) {
        $text += "`r`n`r`nAttention, heures ouvrables : poste(s) « En dehors des heures ouvrables » dans la liste :`r`n  " + ($nightNow -join "`r`n  ")
    }
    if ($ok.Count -gt 0) { $text += "`r`n`r`nDans PDQ : Deploy Once > Choose Targets > Text File." }
    Show-Info $text "Liste PDQ"
}
New-Button $grpSel "Déployer maintenant" 15 28 285 "Lance la mise à jour sur les postes cochés et joignables, sans PDQ (même paquet, même compte)" {
    if ($checked.Count -eq 0) { Show-Info "Cochez au moins un poste." "Déploiement"; return }
    $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    $reach = Test-Reachable @($checked)
    $form.Cursor = [System.Windows.Forms.Cursors]::Default
    $ok  = @($reach.GetEnumerator() | Where-Object Value -eq "OK" | ForEach-Object Key | Sort-Object)
    $bad = @($reach.GetEnumerator() | Where-Object Value -ne "OK" | Sort-Object Key | ForEach-Object { "  $($_.Key) : $($_.Value)" })
    if ($ok.Count -eq 0) { Show-Info ("Aucun poste coché n'est joignable.`r`n`r`n" + ($bad -join "`r`n")) "Déploiement"; return }
    $msg = "Lancer la mise à jour sur {0} poste(s) joignable(s) ?`r`n  {1}" -f $ok.Count, ($ok -join ", ")
    if ($bad.Count -gt 0) { $msg += "`r`n`r`nÉcartés (non joignables) :`r`n" + ($bad -join "`r`n") }
    $nightNow = @($ok | Where-Object { $script:nightOnly.Contains($_) })
    if ($nightNow.Count -gt 0 -and (Test-BusinessHours)) { $msg += "`r`n`r`nAttention, heures ouvrables : " + ($nightNow -join ", ") + " (en dehors des heures ouvrables)" }
    if ([System.Windows.Forms.MessageBox]::Show($msg, "Déploiement", "YesNo", "Question") -ne "Yes") { return }

    # SYSTEM mode: no password needed (the panel's own Kerberos ticket opens the WinRM sessions)
    $cred = $null
    if ($launchAs -eq 'DeployAccount') {
        $cred = Get-DeployCredential
        if (-not $cred) { return }
    }
    $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    $launchTime = Get-Date   # taken before launching: the computers' logs start after it
    $result = Start-RemoteDeploy $ok $cred
    $form.Cursor = [System.Windows.Forms.Cursors]::Default
    $started = @($result.GetEnumerator() | Where-Object { $_.Value.Result -eq "OK" } | ForEach-Object Key | Sort-Object)
    $failed  = @($result.GetEnumerator() | Where-Object { $_.Value.Result -ne "OK" } | Sort-Object Key | ForEach-Object { "  $($_.Key) : $($_.Value.Detail)" })
    # A failure that may be about the password: ask the domain before forgetting it (never guess from the error text)
    if ($cred -and $failed.Count -gt 0 -and ($failed -match 'password|mot de passe|logon|ouverture de session|1326')) {
        Add-Type -AssemblyName System.DirectoryServices.AccountManagement
        $ctx = New-Object System.DirectoryServices.AccountManagement.PrincipalContext('Domain')
        $valid = $ctx.ValidateCredentials(($cred.UserName -replace '^.*\\', ''), $cred.GetNetworkCredential().Password)
        $ctx.Dispose()
        if ($valid) {
            Write-PanelLog "Launch failure mentioning logon, but the saved password is still valid: kept"
        } else {
            Remove-Item $credFile -Force -ErrorAction SilentlyContinue
            Write-PanelLog "Saved password removed: rejected by the domain after a launch failure"
            $failed += "  (mot de passe refusé par le domaine : il sera redemandé au prochain déploiement)"
        }
    }
    if ($started.Count -gt 0) { Start-DeployTracking $started $launchTime }
    if ($failed.Count -gt 0) {
        Show-Info ("Lancé sur {0} poste(s), suivi dans le tableau.`r`n`r`nNon lancés ({1}) :`r`n{2}" -f $started.Count, $failed.Count, ($failed -join "`r`n")) "Déploiement"
    }
}
New-Button $grpSel "Vérifier" 15 116 285 "Compare les versions installées sur les postes cochés" {
    if ($checked.Count -eq 0) { Show-Info "Cochez au moins un poste." "Aucun poste"; return }
    Start-Tool "verify-winget-upgrade.ps1" ("-ComputerName " + ((@($checked) | Sort-Object | ForEach-Object { "'$_'" }) -join ','))
}
New-Button $grpSel "Suivre en direct" 15 72 285 "Une fenêtre de suivi par poste coché (10 au maximum)" {
    if ($checked.Count -eq 0) { Show-Info "Cochez au moins un poste." "Suivi en direct"; return }
    if ($checked.Count -gt 10) { Show-Info "Cochez au plus 10 postes pour le suivi en direct." "Suivi en direct"; return }
    foreach ($c in (@($checked) | Sort-Object)) { Start-Tool "watch-winget-upgrade.ps1" "-ComputerName '$c'" }
}

# Main action stands out
$btnDeploy = $grpSel.Controls | Where-Object { $_.Text -eq "Déployer maintenant" } | Select-Object -First 1
if ($btnDeploy) {
    $btnDeploy.UseVisualStyleBackColor = $false
    $btnDeploy.BackColor = [System.Drawing.Color]::FromArgb(16, 124, 16)
    $btnDeploy.ForeColor = [System.Drawing.Color]::White
    $btnDeploy.Font      = New-Object System.Drawing.Font("Segoe UI", 9.5, [System.Drawing.FontStyle]::Bold)
}

$lblHelp = New-Object System.Windows.Forms.Label
$lblHelp.Location = New-Object System.Drawing.Point(610, 618); $lblHelp.Size = New-Object System.Drawing.Size(315, 58); $lblHelp.ForeColor = [System.Drawing.Color]::DimGray
$lblHelp.Text = "Cocher les postes à faire, puis « Déployer maintenant » ou « Planifier ». Clic droit : exclure, heures ouvrables, file d'attente."
$form.Controls.Add($lblHelp)
$pbDeploy = New-Object System.Windows.Forms.ProgressBar
$pbDeploy.Location = New-Object System.Drawing.Point(610, 620); $pbDeploy.Size = New-Object System.Drawing.Size(315, 22); $pbDeploy.Visible = $false
$form.Controls.Add($pbDeploy)
$lblDeploy = New-Object System.Windows.Forms.Label
$lblDeploy.Location = New-Object System.Drawing.Point(610, 646); $lblDeploy.Size = New-Object System.Drawing.Size(315, 32); $lblDeploy.Visible = $false
$form.Controls.Add($lblDeploy)

# --- Resizable window: the table grows with the window, the buttons follow its edges ---
$lv.Anchor = "Top, Bottom, Left, Right"; $lblLoading.Anchor = "Top, Bottom, Left, Right"
$lblRef.Anchor = "Top, Left, Right"; $lblOnline.Anchor = "Top, Right"; $lblOffline.Anchor = "Top, Right"; $lblStale.Anchor = "Top, Right"
$lblAgent.Anchor = "Top, Right"; $lblNight.Anchor = "Top, Right"; $lblQueue.Anchor = "Top, Right"
$grpFleet.Anchor = "Top, Right"; $grpSel.Anchor = "Top, Right"
$lblHelp.Anchor = "Bottom, Right"; $pbDeploy.Anchor = "Bottom, Right"; $lblDeploy.Anchor = "Bottom, Right"
$lblCount.Anchor = "Bottom, Left"
$form.Controls | Where-Object { $_ -is [System.Windows.Forms.Button] -and $_.Text -in "Cocher les postes à faire", "Tout décocher" } | ForEach-Object { $_.Anchor = "Bottom, Left" }
# The last column (Remarque) takes the extra width; the other columns can still be resized by hand
$lv.Add_Resize({
    $used = 0; for ($i = 0; $i -lt $lv.Columns.Count; $i++) { if ($i -ne 5) { $used += $lv.Columns[$i].Width } }
    $w = $lv.ClientSize.Width - $used - 4
    if ($script:autoFill -and -not $script:hiddenCols.Contains(5) -and $w -ge 150) { $lv.Columns[5].Width = $w }
})

$layoutFile = Join-Path $env:LOCALAPPDATA "MAJ-Logiciels\layout.json"
function Save-Layout {
    $b = if ($form.WindowState -eq 'Normal') { $form.Bounds } else { $form.RestoreBounds }
    $cols = for ($i = 0; $i -lt $lv.Columns.Count; $i++) {
        [pscustomobject]@{ Name = $lv.Columns[$i].Text; Width = $lv.Columns[$i].Width; Order = $lv.Columns[$i].DisplayIndex; Hidden = $script:hiddenCols.Contains($i) }
    }
    $data = [pscustomobject]@{ Columns = @($cols); Maximized = ($form.WindowState -eq 'Maximized'); X = $b.X; Y = $b.Y; Width = $b.Width; Height = $b.Height }
    try { New-Item -Path (Split-Path $layoutFile) -ItemType Directory -Force | Out-Null; Save-JsonFile $layoutFile $data } catch { }
}
function Restore-Layout {
    $d = Read-JsonFile $layoutFile
    if (-not $d) { return }
    $saved = @($d.Columns)
    if ($saved.Count -ne $lv.Columns.Count) { return }   # columns changed since: keep the default layout
    $script:autoFill = $false
    for ($i = 0; $i -lt $saved.Count; $i++) {
        if ($saved[$i].Hidden -and $i -gt 0) { [void]$script:hiddenCols.Add($i); $lv.Columns[$i].Width = 0 } else { $lv.Columns[$i].Width = [int]$saved[$i].Width }
    }
    $order = @(for ($i = 0; $i -lt $saved.Count; $i++) { [pscustomobject]@{ I = $i; O = [int]$saved[$i].Order } }) | Sort-Object O
    foreach ($o in $order) { $lv.Columns[$o.I].DisplayIndex = $o.O }
    # Window: only if it fits on a screen (a screen may have been removed since)
    $r = New-Object System.Drawing.Rectangle([int]$d.X, [int]$d.Y, [int]$d.Width, [int]$d.Height)
    if ([System.Windows.Forms.Screen]::AllScreens | Where-Object { $_.WorkingArea.IntersectsWith($r) }) {
        $form.StartPosition = "Manual"; $form.Bounds = $r
    }
    if ($d.Maximized) { $form.WindowState = "Maximized" }
}
$script:minSize = $form.Size
Restore-Layout
$form.Add_FormClosing({ Save-Layout })
$form.Add_Shown({ $form.MinimumSize = $script:minSize; Clear-OldReports; Update-Data; Update-Network })
[void]$form.ShowDialog()