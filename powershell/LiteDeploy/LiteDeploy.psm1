<#
.SYNOPSIS
    LiteDeploy PowerShell module: the software updates of LiteDeploy from scripts.

.DESCRIPTION
    Lets the scripts of the MAJ-Logiciels tool (and others) live next to
    LiteDeploy: they read and change the same files and run the same worker.
      - Get-LiteDeployState         state of the computers (updates.xml)
      - Get-LiteDeployPackage       package list (packages.txt)
      - Invoke-LiteDeployWorker     LiteDeploy.Worker.exe on computers through
                                    PsExec, as SYSTEM: scan, upgrade, info,
                                    processes; results as objects
      - Set-LiteDeployPause         pause or resume computers
      - Send-LiteDeployWakeOnLan    wake computers (MAC addresses of the state)
      - ConvertFrom-LiteDeployMessage  LDW1 lines to objects
    Files: %LOCALAPPDATA%\LiteDeploy (the files of LiteDeploy for the same
    user). The module is shipped in the "powershell" folder of LiteDeploy.zip
    and signed with the release. ASCII only (Windows PowerShell 5.1).

.EXAMPLE
    Import-Module "C:\Tools\LiteDeploy\powershell\LiteDeploy"
    Invoke-LiteDeployWorker -ComputerName PC01, PC02 -Command scan | Where-Object Status -eq outdated
#>

$script:DataDir = Join-Path $env:LOCALAPPDATA 'LiteDeploy'
# LiteDeploy.exe is two folders up (<LiteDeploy>\powershell\LiteDeploy)
$script:AppDir = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function ConvertFrom-LiteDeployMessage {
    <#
    .SYNOPSIS
        Converts the "LDW1" lines of LiteDeploy.Worker.exe into objects (other lines are ignored).
    .PARAMETER Line
        Output lines of the worker.
    .PARAMETER ComputerName
        Added to each object.
    #>
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)] [string[]] $Line,
        [string] $ComputerName
    )
    process {
        foreach ($l in $Line) {
            if (-not $l) { continue }
            $parts = $l.Trim() -split "`t"
            if ($parts[0] -ne 'LDW1' -or $parts.Count -lt 2) { continue }
            $o = [ordered]@{ ComputerName = $ComputerName }
            foreach ($p in $parts[1..($parts.Count - 1)]) {
                $eq = $p.IndexOf('=')
                if ($eq -le 0) { continue }
                $key = $p.Substring(0, 1).ToUpperInvariant() + $p.Substring(1, $eq - 1)
                $o[$key] = [uri]::UnescapeDataString($p.Substring($eq + 1))
            }
            if ($o.Contains('Type')) { [pscustomobject]$o }
        }
    }
}

function Get-LiteDeployState {
    <#
    .SYNOPSIS
        State of the software updates per computer and package, from %LOCALAPPDATA%\LiteDeploy\updates.xml.
    .PARAMETER ComputerName
        Only these computers (wildcards allowed).
    #>
    [CmdletBinding()]
    param([string[]] $ComputerName = '*')
    $path = Join-Path $script:DataDir 'updates.xml'
    if (-not (Test-Path $path)) { return }
    [xml]$doc = Get-Content $path -Raw -Encoding UTF8
    foreach ($c in $doc.LiteDeployUpdates.Computer) {
        if (-not ($ComputerName | Where-Object { $c.Name -like $_ })) { continue }
        $base = [ordered]@{
            ComputerName = $c.Name; Paused = ($c.Paused -eq 'true'); PauseReason = $c.PauseReason
            NightOnly = ($c.NightOnly -eq 'true'); LastChange = $c.LastChange; LastError = $c.LastError
        }
        $packages = @($c.Package)
        if ($packages.Count -eq 0 -or -not $packages[0]) { [pscustomobject]$base; continue }
        foreach ($p in $packages) {
            $o = [ordered]@{}
            foreach ($k in $base.Keys) { $o[$k] = $base[$k] }
            $o.Package = $p.Id; $o.Status = $p.Status; $o.Installed = $p.Installed; $o.Available = $p.Available
            $o.LastResult = $p.LastResult; $o.LastMessage = $p.LastMessage; $o.LastUpgrade = $p.LastUpgrade
            [pscustomobject]$o
        }
    }
}

function Get-LiteDeployPackage {
    <#
    .SYNOPSIS
        Package list of the software updates: the user's (%LOCALAPPDATA%\LiteDeploy\packages.txt), else the shipped one.
    #>
    [CmdletBinding()]
    param()
    $path = Join-Path $script:DataDir 'packages.txt'
    if (-not (Test-Path $path)) { $path = Join-Path $script:AppDir 'updates\packages.txt' }
    if (-not (Test-Path $path)) { return }
    foreach ($line in Get-Content $path) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $p = @($t -split '\|' | ForEach-Object { $_.Trim() })
        [pscustomobject]@{
            Id        = $p[0]
            Processes = if ($p.Count -gt 1) { @($p[1] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) } else { @() }
            Label     = if ($p.Count -gt 2 -and $p[2]) { $p[2] } else { $p[0] }
        }
    }
}

# PsExec as LiteDeploy finds it: its folder, then LiteTask's tools folder, then the downloaded copy
function Get-PsExecPath {
    $candidates = @(
        (Join-Path $script:AppDir 'PsExec.exe'), (Join-Path $script:AppDir 'PsExec64.exe'),
        (Join-Path (Split-Path $script:AppDir -Parent) 'PsExec64.exe'), (Join-Path (Split-Path $script:AppDir -Parent) 'PsExec.exe'),
        (Join-Path $script:DataDir 'tools\PsExec64.exe')
    )
    $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

# Quoting of one argument with the CommandLineToArgvW rules
function ConvertTo-Argument([string] $Value) {
    if ($null -eq $Value) { $Value = '' }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    $backslashes = 0
    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq '\') { $backslashes++; continue }
        if ($ch -eq '"') { [void]$sb.Append([char]'\', $backslashes * 2 + 1).Append('"') }
        else { [void]$sb.Append([char]'\', $backslashes).Append($ch) }
        $backslashes = 0
    }
    [void]$sb.Append([char]'\', $backslashes * 2).Append('"')
    $sb.ToString()
}

function Invoke-LiteDeployWorker {
    <#
    .SYNOPSIS
        Runs LiteDeploy.Worker.exe on computers through PsExec, as SYSTEM, and returns its messages as objects.
    .PARAMETER ComputerName
        Target computers.
    .PARAMETER Command
        scan (versions), upgrade (update the outdated packages), info (computer card), processes (handles).
    .PARAMETER Package
        winget Ids or module:Name (scan and upgrade); default: the package list of LiteDeploy.
    .PARAMETER Credential
        Account used by PsExec (the worker still runs as SYSTEM); default: the current user.
    .NOTES
        The state file of LiteDeploy is not changed: run a check in LiteDeploy to see the result there.
        Update modules must already be in %windir%\LiteDeploy\modules of the computer (LiteDeploy copies them).
    .EXAMPLE
        Invoke-LiteDeployWorker -ComputerName PC01 -Command info
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)] [string[]] $ComputerName,
        [ValidateSet('scan', 'upgrade', 'info', 'processes')] [string] $Command = 'scan',
        [string[]] $Package,
        [pscredential] $Credential
    )
    $psexec = Get-PsExecPath
    if (-not $psexec) { throw 'PsExec not found (LiteDeploy folder, LiteTask tools folder or %LOCALAPPDATA%\LiteDeploy\tools)' }
    $worker = Join-Path $script:AppDir 'LiteDeploy.Worker.exe'
    if (-not (Test-Path $worker)) { throw "LiteDeploy.Worker.exe not found in $script:AppDir" }

    $workerArgs = $Command
    if ($Command -in 'scan', 'upgrade') {
        $list = @(Get-LiteDeployPackage)
        if ($Package) {
            $list = @($list | Where-Object { $Package -contains $_.Id })
            foreach ($id in $Package | Where-Object { @($list.Id) -notcontains $_ }) { $list += [pscustomobject]@{ Id = $id; Processes = @(); Label = $id } }
        }
        if (-not $list) { throw 'No package' }
        foreach ($p in $list) { $workerArgs += ' --package ' + (ConvertTo-Argument ("{0}|{1}|{2}" -f $p.Id, ($p.Processes -join ','), $p.Label)) }
        $workerArgs += ' --timeout 15'
    }

    foreach ($computer in $ComputerName) {
        if (-not $PSCmdlet.ShouldProcess($computer, "LiteDeploy.Worker.exe $Command")) { continue }
        $auth = ''
        if ($Credential) {
            $auth = ' -u ' + (ConvertTo-Argument $Credential.UserName) + ' -p ' + (ConvertTo-Argument $Credential.GetNetworkCredential().Password)
        }
        $arguments = "-accepteula -nobanner \\$computer -h$auth -s -c -f -n 20 " + (ConvertTo-Argument $worker) + " $workerArgs"
        $psi = New-Object System.Diagnostics.ProcessStartInfo($psexec, $arguments)
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
        $p = [System.Diagnostics.Process]::Start($psi)
        $err = $p.StandardError.ReadToEndAsync()
        $out = $p.StandardOutput.ReadToEnd()
        $p.WaitForExit()
        $messages = @($out -split "`r?`n" | ConvertFrom-LiteDeployMessage -ComputerName $computer)
        if ($messages.Count -eq 0) {
            Write-Error "$computer : no answer from the worker (PsExec exit code $($p.ExitCode)) $($err.Result.Trim())"
        }
        $messages
        $p.Dispose()
    }
}

function Set-LiteDeployPause {
    <#
    .SYNOPSIS
        Pauses computers (skipped by LiteDeploy until resumed) or resumes them, in updates.xml.
    .PARAMETER Resume
        Resume instead of pausing.
    .NOTES
        Close the Software Updates window of LiteDeploy first: it keeps its own copy while it is open.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)] [string[]] $ComputerName,
        [string] $Reason,
        [switch] $Resume
    )
    $path = Join-Path $script:DataDir 'updates.xml'
    [xml]$doc = if (Test-Path $path) { Get-Content $path -Raw -Encoding UTF8 } else { '<LiteDeployUpdates />' }
    $root = $doc.DocumentElement
    foreach ($name in $ComputerName) {
        $c = $root.SelectNodes('Computer') | Where-Object { $_.GetAttribute('Name') -eq $name } | Select-Object -First 1
        if (-not $c) { $c = $doc.CreateElement('Computer'); $c.SetAttribute('Name', $name); [void]$root.AppendChild($c) }
        if (-not $PSCmdlet.ShouldProcess($name, $(if ($Resume) { 'Resume' } else { 'Pause' }))) { continue }
        $c.SetAttribute('Paused', $(if ($Resume) { 'false' } else { 'true' }))
        $old = $c.SelectSingleNode('PauseReason')
        if ($old) { [void]$c.RemoveChild($old) }
        if (-not $Resume -and $Reason) {
            $r = $doc.CreateElement('PauseReason'); $r.InnerText = $Reason
            [void]$c.PrependChild($r)
        }
    }
    New-Item -ItemType Directory -Force -Path $script:DataDir | Out-Null
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $w = [System.Xml.XmlWriter]::Create($path, $settings)
    try { $doc.Save($w) } finally { $w.Close() }
}

function Send-LiteDeployWakeOnLan {
    <#
    .SYNOPSIS
        Wakes computers with the MAC addresses LiteDeploy keeps in updates.xml (reported by the worker).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [string[]] $ComputerName)
    $path = Join-Path $script:DataDir 'updates.xml'
    if (-not (Test-Path $path)) { throw "No state file: $path" }
    [xml]$doc = Get-Content $path -Raw -Encoding UTF8
    $udp = New-Object System.Net.Sockets.UdpClient
    $udp.EnableBroadcast = $true
    try {
        foreach ($name in $ComputerName) {
            $c = $doc.LiteDeployUpdates.Computer | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if (-not $c -or -not $c.Macs) { Write-Warning "$name : no MAC address known (run a check in LiteDeploy first)"; continue }
            $targets = @([System.Net.IPAddress]::Broadcast)
            foreach ($ip in ("$($c.Ips)" -split ' ' | Where-Object { $_ })) {
                $b = ([System.Net.IPAddress]$ip).GetAddressBytes(); $b[3] = 255
                $targets += New-Object System.Net.IPAddress (, $b)
            }
            foreach ($mac in ($c.Macs -split ' ' | Where-Object { $_ })) {
                $bytes = [byte[]]($mac -split '[-:]' | ForEach-Object { [Convert]::ToByte($_, 16) })
                $packet = [byte[]]((,0xFF * 6) + ($bytes * 16))
                foreach ($t in $targets) { foreach ($port in 9, 7) { [void]$udp.Send($packet, $packet.Length, (New-Object System.Net.IPEndPoint($t, $port))) } }
            }
            Write-Verbose "$name : Wake-on-LAN sent"
        }
    } finally {
        $udp.Close()
    }
}

Export-ModuleMember -Function Get-LiteDeployState, Get-LiteDeployPackage, Invoke-LiteDeployWorker, Set-LiteDeployPause, Send-LiteDeployWakeOnLan, ConvertFrom-LiteDeployMessage
