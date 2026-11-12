# LiteDeploy-Module
# Label: Java (Oracle JRE 8)
# Processes:
<#
.SYNOPSIS
    LiteDeploy update module: Oracle Java Runtime Environment 8 (winget package
    Oracle.JavaRuntimeEnvironment), from the MAJ-Logiciels tool of Patrick Bedard.

.DESCRIPTION
    winget removes the old Java before installing the new one and fails while
    the system Java is running, which leaves the computer without Java. So this
    module stops only the system Java (Oracle\Java and Program Files\Java;
    Java runtimes bundled with other applications are left alone), upgrades,
    checks that the system Java still answers (java8path), and asks for a
    restart (exit code 3010) when it had to stop Java, so the applications that
    start it at logon start again. See docs/modules.md for the contract.

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

function Get-SystemJavaProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -match $systemJava })
}

try {
    $installed = Get-InstalledJava
    if (-not $installed) { Write-LiteDeployResult 'notinstalled'; exit 0 }
    $winget = Get-Winget
    if (-not $winget) { Write-LiteDeployResult 'error' $installed.DisplayVersion $null 'winget.exe not found'; exit 1 }

    # A row in the list means an upgrade is available (an up-to-date package gives no row)
    $list = & $winget list --id $id -e --source winget --upgrade-available --accept-source-agreements --disable-interactivity 2>&1 | Out-String
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

    # Stop the system Java only
    $stopped = $false
    $running = Get-SystemJavaProcesses
    if ($running.Count -gt 0) {
        $running | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 3
        if ((Get-SystemJavaProcesses).Count -gt 0) {
            Write-LiteDeployResult 'error' $installed.DisplayVersion $available 'Could not stop the system Java: not upgraded'
            exit 1
        }
        $stopped = $true
    }

    $out = & $winget upgrade --id $id -e --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity 2>&1 | Out-String
    $code = $LASTEXITCODE

    # The system Java must still answer (applications start "java" through java8path or the PATH)
    $shim = "${env:ProgramFiles(x86)}\Common Files\Oracle\Java\java8path\java.exe"
    if (-not (Test-Path $shim)) { $shim = "$env:ProgramFiles\Common Files\Oracle\Java\java8path\java.exe" }
    $javaVersion = if (Test-Path $shim) { (& $shim -version 2>&1 | Select-Object -First 1 | Out-String).Trim() } else { '' }

    $now = Get-InstalledJava
    if ($code -ne 0) {
        Write-LiteDeployResult 'error' ($now.DisplayVersion) $available ("winget upgrade failed: 0x{0:X8}" -f $code)
        exit 1
    }
    if ($javaVersion -notmatch 'version') {
        Write-LiteDeployResult 'error' ($now.DisplayVersion) $null 'JAVA CHECK FAILED: java8path missing or not working, reinstall Java'
        exit 1
    }
    Write-LiteDeployResult 'uptodate' ($now.DisplayVersion) $null "Upgraded ($javaVersion)"
    if ($stopped) { exit 3010 }
    exit 0
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
