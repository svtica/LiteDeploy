# LiteDeploy-Module
# Label: Java (Oracle JRE 8)
# Processes:
<#
.SYNOPSIS
    LiteDeploy update module: Oracle Java Runtime Environment 8 (winget package
    Oracle.JavaRuntimeEnvironment), from an earlier in-house update script.

.DESCRIPTION
    winget removes the old Java before installing the new one and fails while
    the system Java is running, which leaves the computer without Java. So this
    module notes, then stops only the system Java (Oracle\Java and Program
    Files\Java; Java runtimes bundled with other applications are left alone),
    upgrades, checks that the system Java still answers (java8path), then starts
    again what it stopped: a Windows service with Start-Service; a program of a
    desktop session in that session, as its user, with the same arguments
    (one-time scheduled task with an interactive principal: no password,
    nothing elevated; the new Java through java8path when the old folder is
    gone; working folder: the folder of the -jar file). The message names what
    was relaunched. It asks for a restart (exit code 3010) only when something
    could not be started again (a process of session 0 that is not a service,
    a user who logged off). See docs/modules.md for the contract.

.PARAMETER Action
    Check or Update.
#>
param(
    [ValidateSet('Check', 'Update')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
$id = 'Oracle.JavaRuntimeEnvironment'
$systemJava = '\\Oracle\\Java\\|\\Program Files( \(x86\))?\\Java\\'

function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

# winget.exe of the newest App Installer (not on the PATH of SYSTEM)
function Get-Winget {
    Resolve-Path "$env:ProgramFiles\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe\winget.exe" -ErrorAction SilentlyContinue |
        Sort-Object Path | Select-Object -Last 1 -ExpandProperty Path
}

function Get-InstalledJava {
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    Get-ItemProperty $keys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -match '^Java( \d+)? Update \d+' -and $_.Publisher -match 'Oracle' -and $_.DisplayVersion } |
        Sort-Object { [version]$_.DisplayVersion } -Descending | Select-Object -First 1
}

# Runs a program and returns its output, standard error included, as text. With
# $ErrorActionPreference = 'Stop', Windows PowerShell 5.1 turns each line a program
# writes to standard error (2>&1) into a terminating error: "java -version" writes
# its version there, which made a successful upgrade end in the catch block.
function Invoke-Native([string] $FilePath, [string[]] $Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $FilePath @Arguments 2>&1 | ForEach-Object { "$_" }
        return ($lines -join "`n")
    }
    finally { $ErrorActionPreference = $previous }
}

function Get-SystemJavaProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -match $systemJava })
}

# The arguments of a command line, without its program (quoted or not)
function Get-CommandArguments([string] $CommandLine) {
    if (-not $CommandLine) { return '' }
    $c = $CommandLine.Trim()
    if ($c.StartsWith('"')) {
        $end = $c.IndexOf('"', 1)
        if ($end -lt 0) { return '' }
        return $c.Substring($end + 1).Trim()
    }
    $space = $c.IndexOf(' ')
    if ($space -lt 0) { return '' }
    return $c.Substring($space + 1).Trim()
}

# The folder of the jar of "-jar <file>" (quoted or not), else $null: the working folder of a relaunch
function Get-JarFolder([string] $Arguments) {
    if ($Arguments -match '-jar\s+"([^"]+)"') { return (Split-Path -Parent $Matches[1]) }
    if ($Arguments -match '-jar\s+(\S+)') { return (Split-Path -Parent $Matches[1]) }
    return $null
}

# What to start again after the upgrade: a service it belongs to, or the program, its arguments and the
# user of its desktop session (processes of session 0 that are not services cannot be relaunched)
function Save-JavaProcesses($Processes) {
    $saved = @()
    foreach ($p in $Processes) {
        $service = Get-CimInstance Win32_Service -Filter "ProcessId=$($p.ProcessId)" -ErrorAction SilentlyContinue | Select-Object -First 1
        $owner = $null
        try {
            $o = Invoke-CimMethod -InputObject $p -MethodName GetOwner -ErrorAction Stop
            if ($o.User) { $owner = if ($o.Domain) { "$($o.Domain)\$($o.User)" } else { $o.User } }
        } catch { }
        $saved += [pscustomobject]@{
            Name      = $p.Name
            Path      = $p.ExecutablePath
            Arguments = Get-CommandArguments $p.CommandLine
            SessionId = [int]$p.SessionId
            User      = $owner
            Service   = if ($service) { $service.Name } else { $null }
        }
    }
    # The same program, arguments and user once (several instances of one application)
    return @($saved | Sort-Object Service, User, Path, Arguments -Unique)
}

# The java.exe or javaw.exe to start: the same path when it still exists, else the one of java8path
function Get-RelaunchProgram($Saved) {
    if ($Saved.Path -and (Test-Path -LiteralPath $Saved.Path)) { return $Saved.Path }
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not $root) { continue }
        $candidate = Join-Path $root ("Common Files\Oracle\Java\java8path\" + $Saved.Name)
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

# Starts again what the upgrade stopped: services with Start-Service, the programs of a desktop session in
# that session, as its user (one-time task with an interactive principal: no password, nothing elevated).
# Returns one text per program; Relaunched/NotRelaunched count them.
function Restart-JavaProcesses($Saved) {
    $script:Relaunched = 0
    $script:NotRelaunched = 0
    $texts = @()
    $n = 0
    foreach ($s in $Saved) {
        $n++
        $label = if ($s.Service) { "service $($s.Service)" } else { $s.Name + $(if ($s.Arguments) { ' ' + $s.Arguments } else { '' }) }
        try {
            if ($s.Service) {
                Start-Service -Name $s.Service -ErrorAction Stop
                $script:Relaunched++
                $texts += "relaunched $label"
                continue
            }
            if ($s.SessionId -le 0 -or -not $s.User) { throw 'not in a desktop session' }
            $program = Get-RelaunchProgram $s
            if (-not $program) { throw 'java8path not found' }
            $folder = Get-JarFolder $s.Arguments
            if (-not $folder -or -not (Test-Path -LiteralPath $folder)) { $folder = Split-Path -Parent $program }
            $task = "LiteDeploy-Java-Relaunch-$PID-$n"
            $taskAction = if ($s.Arguments) { New-ScheduledTaskAction -Execute $program -Argument $s.Arguments -WorkingDirectory $folder }
                          else { New-ScheduledTaskAction -Execute $program -WorkingDirectory $folder }
            $principal = New-ScheduledTaskPrincipal -UserId $s.User -LogonType Interactive
            Register-ScheduledTask -TaskName $task -Action $taskAction -Principal $principal -Force | Out-Null
            try {
                Start-ScheduledTask -TaskName $task
                Start-Sleep -Seconds 3
            }
            finally { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue }
            $script:Relaunched++
            $texts += "relaunched $label for $($s.User)"
        }
        catch {
            $script:NotRelaunched++
            $texts += "NOT relaunched $label ($($_.Exception.Message))"
        }
    }
    return $texts
}

# Dot-sourced by the tests: the functions only
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    $installed = Get-InstalledJava
    if (-not $installed) { Write-LiteDeployResult 'notinstalled'; exit 0 }
    $winget = Get-Winget
    if (-not $winget) { Write-LiteDeployResult 'error' $installed.DisplayVersion $null 'winget.exe not found'; exit 1 }

    # A row in the list means an upgrade is available (an up-to-date package gives no row)
    $list = Invoke-Native $winget @('list', '--id', $id, '-e', '--source', 'winget', '--upgrade-available', '--accept-source-agreements', '--disable-interactivity')
    $outdated = $list -match [regex]::Escape($id)
    $available = $null
    if ($outdated) {
        $row = ($list -split "`n" | Where-Object { $_ -match [regex]::Escape($id) } | Select-Object -First 1)
        $tokens = @($row.Substring($row.IndexOf($id) + $id.Length).Trim() -split '\s+')
        if ($tokens.Count -ge 2) { $available = $tokens[1] }
    }

    if ($Action -eq 'Check' -or -not $outdated) {
        if ($outdated) { Write-LiteDeployResult 'outdated' $installed.DisplayVersion $available }
        else { Write-LiteDeployResult 'uptodate' $installed.DisplayVersion }
        exit 0
    }

    # Stop the system Java only, after noting what to start again
    $saved = @()
    $running = Get-SystemJavaProcesses
    if ($running.Count -gt 0) {
        $saved = Save-JavaProcesses $running
        $running | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 3
        if ((Get-SystemJavaProcesses).Count -gt 0) {
            Write-LiteDeployResult 'error' $installed.DisplayVersion $available 'Could not stop the system Java: not upgraded'
            exit 1
        }
    }

    $out = Invoke-Native $winget @('upgrade', '--id', $id, '-e', '--source', 'winget', '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity')
    $code = $LASTEXITCODE

    # The system Java must still answer (applications start "java" through java8path or the PATH)
    $shim = "${env:ProgramFiles(x86)}\Common Files\Oracle\Java\java8path\java.exe"
    if (-not (Test-Path $shim)) { $shim = "$env:ProgramFiles\Common Files\Oracle\Java\java8path\java.exe" }
    $javaVersion = if (Test-Path $shim) { ((Invoke-Native $shim @('-version')) -split "`n" | Select-Object -First 1).Trim() } else { '' }

    # What was stopped starts again, whatever the result of the upgrade (the old Java when it failed)
    $relaunch = ''
    if ($saved.Count -gt 0) { $relaunch = '; ' + ((Restart-JavaProcesses $saved) -join '; ') }

    $now = Get-InstalledJava
    if ($code -ne 0) {
        Write-LiteDeployResult 'error' ($now.DisplayVersion) $available (("winget upgrade failed: 0x{0:X8}" -f $code) + $relaunch)
        exit 1
    }
    if ($javaVersion -notmatch 'version') {
        Write-LiteDeployResult 'error' ($now.DisplayVersion) $null ('JAVA CHECK FAILED: java8path missing or not working, reinstall Java' + $relaunch)
        exit 1
    }
    Write-LiteDeployResult 'uptodate' ($now.DisplayVersion) $null "Upgraded ($javaVersion)$relaunch"
    # A restart only when something could not be started again
    if ($saved.Count -gt 0 -and $script:NotRelaunched -gt 0) { exit 3010 }
    exit 0
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
