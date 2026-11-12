# LiteDeploy-Module
# Label: Example product
# Processes: example, example-helper
<#
.SYNOPSIS
    Template of a LiteDeploy update module (see docs/modules.md).

.DESCRIPTION
    Copy this file to %LOCALAPPDATA%\LiteDeploy\modules\<Name>.ps1, adapt the
    three functions below, then sign it with the LiteSuite certificate:
        Set-AuthenticodeSignature .\<Name>.ps1 -Certificate $cert -TimestampServer http://timestamp.digicert.com
    LiteDeploy copies it to %windir%\LiteDeploy\modules on the computer and
    LiteDeploy.Worker.exe runs it as SYSTEM, only if the signature is valid:
        powershell.exe -File <Name>.ps1 -Action Check    (report the versions)
        powershell.exe -File <Name>.ps1 -Action Update   (install the update)
    The processes listed in the header are closed by the worker before Update.
    Keep this file ASCII only (Windows PowerShell 5.1 reads it as ANSI).

.PARAMETER Action
    Check or Update.
#>
param(
    [ValidateSet('Check', 'Update')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'

# One result line for LiteDeploy: status uptodate, outdated, notinstalled or error
function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

# Installed version, from the uninstall entries of the registry ($null: not installed)
function Get-InstalledVersion {
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    $entry = Get-ItemProperty $keys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like 'Example product*' } | Select-Object -First 1
    if ($entry) { return [string]$entry.DisplayVersion }
    return $null
}

# Latest version published by the vendor, and the URL of its installer
function Get-LatestRelease {
    # Example: ask the vendor's API or read a version file of your share
    return [pscustomobject]@{ Version = '1.2.3'; Url = 'https://vendor.example/downloads/example-1.2.3-x64.msi' }
}

try {
    $installed = Get-InstalledVersion
    if (-not $installed) { Write-LiteDeployResult 'notinstalled'; exit 0 }
    $latest = Get-LatestRelease
    $outdated = [version]$installed -lt [version]$latest.Version

    if ($Action -eq 'Check') {
        if ($outdated) { Write-LiteDeployResult 'outdated' $installed $latest.Version }
        else { Write-LiteDeployResult 'uptodate' $installed }
        exit 0
    }

    if (-not $outdated) { Write-LiteDeployResult 'uptodate' $installed; exit 0 }

    # Download to a folder only SYSTEM and administrators can write to, never to C:\Windows\Temp
    $work = Join-Path $env:windir 'LiteDeploy\work'
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $msi = Join-Path $work 'example.msi'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $latest.Url -OutFile $msi -UseBasicParsing

    # The installer must be signed by the vendor
    $sig = Get-AuthenticodeSignature $msi
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Example Vendor Inc\.') {
        Remove-Item $msi -Force
        Write-LiteDeployResult 'error' $installed $latest.Version "Installer refused: signature $($sig.Status)"
        exit 1
    }

    $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart" -Wait -PassThru
    Remove-Item $msi -Force -ErrorAction SilentlyContinue
    if ($p.ExitCode -eq 0 -or $p.ExitCode -eq 3010) {
        Write-LiteDeployResult 'uptodate' (Get-InstalledVersion) $null "Updated to $($latest.Version)"
        exit $p.ExitCode
    }
    Write-LiteDeployResult 'error' $installed $latest.Version "msiexec exit code $($p.ExitCode)"
    exit 1
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
