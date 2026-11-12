# LiteDeploy-Module
# Label: 1Password
# Processes: 1Password, 1Password-BrowserSupport
<#
.SYNOPSIS
    LiteDeploy update module: 1Password installed per machine (MSI), from the
    MAJ-Logiciels tool of Patrick Bedard.

.DESCRIPTION
    Check: installed version (registry) against the version winget knows
    (AgileBits.1Password). Update: downloads the official MSI from
    downloads.1password.com (or the shared cache, LITEDEPLOY_CACHE\1Password),
    refuses it unless it is signed by AgileBits,
    installs it with MANAGED_INSTALL=1 MANAGED_UPDATE=1. When another version
    blocks the install (1638), the old one is removed first. The MSI action
    AI_UnpinFromStartScreen can hang on the Start menu host: the host is
    restarted when that happens (Windows relaunches it). The processes of the
    header are closed by LiteDeploy before Update (an open 1Password locks its
    files). See docs/modules.md for the contract.

.PARAMETER Action
    Check or Update.
#>
param(
    [ValidateSet('Check', 'Update')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
$url = 'https://downloads.1password.com/win/1PasswordSetup-latest.msi'

function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

function Get-Winget {
    Resolve-Path "$env:ProgramFiles\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe\winget.exe" -ErrorAction SilentlyContinue |
        Sort-Object Path | Select-Object -Last 1 -ExpandProperty Path
}

function Get-Installed {
    Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq '1Password' -and $_.DisplayVersion } | Select-Object -First 1
}

# Latest version known to winget ("Version: 8.11.4")
function Get-LatestVersion {
    $winget = Get-Winget
    if (-not $winget) { return $null }
    $out = & $winget show --id AgileBits.1Password -e --source winget --accept-source-agreements --disable-interactivity 2>&1 | Out-String
    $m = [regex]::Match($out, '(?m)^\s*Version\s*:\s*([\d\.]+)')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

# Installer from the shared cache (LITEDEPLOY_CACHE, set by LiteDeploy) when it holds a copy with a
# valid signature of the vendor, else downloaded; a downloaded file is copied to the cache for the
# other computers (best effort, one writer at a time). Returns the local path, or throws.
function Get-Installer([string] $Url, [string] $FileName, [string] $Folder, [string] $SignerPattern) {
    $work = Join-Path $env:windir 'LiteDeploy\work'
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $local = Join-Path $work $FileName
    Remove-Item $local -Force -ErrorAction SilentlyContinue
    $cacheDir = if ($env:LITEDEPLOY_CACHE) { Join-Path $env:LITEDEPLOY_CACHE $Folder } else { $null }
    $cached = if ($cacheDir) { Join-Path $cacheDir $FileName } else { $null }

    if ($cached -and (Test-Path $cached)) {
        try {
            Copy-Item $cached $local -Force
            if (Test-VendorSignature $local $SignerPattern) { return $local }
            Remove-Item $local -Force -ErrorAction SilentlyContinue
        } catch { }
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $Url -OutFile $local -UseBasicParsing
    if (-not (Test-VendorSignature $local $SignerPattern)) {
        Remove-Item $local -Force -ErrorAction SilentlyContinue
        throw "Installer refused: not signed by the vendor ($FileName)"
    }

    if ($cacheDir) {
        $lock = $null
        try {
            New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
            $lock = [IO.File]::Open((Join-Path $cacheDir '.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
            if (-not (Test-Path $cached)) {
                Copy-Item $local "$cached.partial" -Force
                Move-Item "$cached.partial" $cached -Force
            }
        } catch {
            # Read-only cache or another computer writing: the cache stays as it is
        } finally {
            if ($lock) { $lock.Close() }
        }
    }
    return $local
}

function Test-VendorSignature([string] $Path, [string] $SignerPattern) {
    $sig = Get-AuthenticodeSignature $Path
    return ($sig.Status -eq 'Valid' -and $sig.SignerCertificate.Subject -match $SignerPattern)
}

function Invoke-Msi([string] $Arguments, [string] $MsiLog) {
    $proc = Start-Process msiexec.exe -ArgumentList $Arguments -PassThru
    $null = $proc.Handle
    $restarts = 0
    while (-not $proc.WaitForExit(10000)) {
        $last = Get-Content $MsiLog -Tail 1 -ErrorAction SilentlyContinue
        $idle = if (Test-Path $MsiLog) { ((Get-Date) - (Get-Item $MsiLog).LastWriteTime).TotalSeconds } else { 0 }
        if ($last -match 'AI_UnpinFromStartScreen' -and $idle -gt 30 -and $restarts -lt 3) {
            Get-Process StartMenuExperienceHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            $restarts++
        }
    }
    return $proc.ExitCode
}

try {
    $installed = Get-Installed
    if (-not $installed) { Write-LiteDeployResult 'notinstalled'; exit 0 }
    $latest = Get-LatestVersion
    if (-not $latest) { Write-LiteDeployResult 'error' $installed.DisplayVersion $null 'Latest version unknown (winget)'; exit 1 }
    $outdated = [version]$installed.DisplayVersion -lt [version]$latest

    if ($Action -eq 'Check' -or -not $outdated) {
        if ($outdated) { Write-LiteDeployResult 'outdated' $installed.DisplayVersion $latest }
        else { Write-LiteDeployResult 'uptodate' $installed.DisplayVersion }
        exit 0
    }

    # Work folder writable by SYSTEM and administrators only (never C:\Windows\Temp); the MSI
    # is cached under the version announced by winget
    $msi = Get-Installer $url "1PasswordSetup-$latest.msi" '1Password' 'O=AgileBits'
    $log = Join-Path (Split-Path $msi) '1password.log'

    $arguments = "/i `"$msi`" MANAGED_INSTALL=1 MANAGED_UPDATE=1 /qn /norestart /l*v `"$log`""
    $code = Invoke-Msi $arguments $log
    if ($code -eq 1638) {
        # Another version is installed and the MSI does not upgrade it in place: remove it first
        Start-Process msiexec.exe -ArgumentList "/x $($installed.PSChildName) /qn /norestart" -Wait | Out-Null
        $code = Invoke-Msi $arguments $log
    }
    Remove-Item $msi -Force -ErrorAction SilentlyContinue

    $now = Get-Installed
    if ($code -eq 0 -or $code -eq 3010) {
        Write-LiteDeployResult 'uptodate' ($now.DisplayVersion) $null "Updated to $latest"
        exit $code
    }
    Write-LiteDeployResult 'error' ($now.DisplayVersion) $latest "msiexec exit code $code (log: $log)"
    exit 1
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
