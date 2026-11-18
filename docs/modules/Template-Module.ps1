# LiteDeploy-Module
# Label: Example product
# Processes: example, example-helper
# Install: yes
# Parameter: Environment default=Production Name of the environment the product connects to
# Parameter: InstallerFile default=example.msi File name of the installer in the shared cache
# Parameter: EnrollmentToken secret Enrollment token (password of a stored credential)
<#
.SYNOPSIS
    Template of a LiteDeploy update module (see docs/modules.md).

.DESCRIPTION
    Copy this file to %LOCALAPPDATA%\LiteDeploy\modules\<Name>.ps1 (or to the
    shared modules folder of App > Options > Software updates, writable by IT
    administrators only), adapt the three functions below, then sign it with
    the LiteSuite certificate:
        Set-AuthenticodeSignature .\<Name>.ps1 -Certificate $cert -TimestampServer http://timestamp.digicert.com
    LiteDeploy copies it to %windir%\LiteDeploy\modules on the computer and
    LiteDeploy.Worker.exe runs it as SYSTEM, only if the signature is valid:
        powershell.exe -File <Name>.ps1 -Action Check    (report the versions)
        powershell.exe -File <Name>.ps1 -Action Update   (install the update)
        powershell.exe -File <Name>.ps1 -Action Install  (install the product: only
                                                          with the header line "# Install: yes")
    The processes listed in the header are closed by the worker before Update.
    Install is an explicit action of the administrator (Software Updates, right-click >
    Install), run only where Check reported notinstalled; without "# Install: yes" the
    worker refuses it. Remove that line and the Install branch if the module cannot install.
    Parameters (header lines "# Parameter: Name [secret] [default=value] description")
    are set in LiteDeploy (Package list > Parameters...) and given to this
    process only, as environment variables LITEDEPLOY_PARAM_<Name>. A required
    parameter (no default) without value: the worker does not start the module.
    A secret value must never be written to the output, a log or an error.
    Keep this file ASCII only (Windows PowerShell 5.1 reads it as ANSI).

.PARAMETER Action
    Check, Update or Install.
#>
param(
    [ValidateSet('Check', 'Update', 'Install')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'

# Parameters of the header (empty when an older LiteDeploy runs the module: check them)
$environment   = $env:LITEDEPLOY_PARAM_Environment
$installerFile = $env:LITEDEPLOY_PARAM_InstallerFile
$token         = $env:LITEDEPLOY_PARAM_EnrollmentToken   # secret: never write it out

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
    if (-not $environment -or -not $token) {
        Write-LiteDeployResult 'error' $null $null 'Parameters missing: Environment and EnrollmentToken (Package list > Parameters...)'
        exit 1
    }
    $installed = Get-InstalledVersion
    $latest = Get-LatestRelease

    if ($Action -eq 'Install') {
        # Install: only where the product is missing; an installed product is left alone
        if ($installed) {
            if ([version]$installed -lt [version]$latest.Version) { Write-LiteDeployResult 'outdated' $installed $latest.Version 'Already installed' }
            else { Write-LiteDeployResult 'uptodate' $installed $null 'Already installed' }
            exit 0
        }
    }
    else {
        if (-not $installed) { Write-LiteDeployResult 'notinstalled'; exit 0 }
        $outdated = [version]$installed -lt [version]$latest.Version

        if ($Action -eq 'Check') {
            if ($outdated) { Write-LiteDeployResult 'outdated' $installed $latest.Version }
            else { Write-LiteDeployResult 'uptodate' $installed }
            exit 0
        }

        if (-not $outdated) { Write-LiteDeployResult 'uptodate' $installed; exit 0 }
    }

    # Update and Install: same installer, same checks

    # Download to a folder only SYSTEM and administrators can write to, never to C:\Windows\Temp
    $work = Join-Path $env:windir 'LiteDeploy\work'
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $msi = Join-Path $work 'example.msi'
    # From the shared cache (LITEDEPLOY_CACHE) when it holds the file named by InstallerFile
    $cached = if ($env:LITEDEPLOY_CACHE -and $installerFile) { Join-Path $env:LITEDEPLOY_CACHE (Split-Path $installerFile -Leaf) } else { $null }
    if ($cached -and (Test-Path -LiteralPath $cached)) {
        Copy-Item -LiteralPath $cached -Destination $msi -Force
    }
    else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $latest.Url -OutFile $msi -UseBasicParsing
    }

    # The installer must be signed by the vendor
    $sig = Get-AuthenticodeSignature $msi
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Example Vendor Inc\.') {
        Remove-Item $msi -Force
        Write-LiteDeployResult 'error' $installed $latest.Version "Installer refused: signature $($sig.Status)"
        exit 1
    }

    # The token goes to the installer only (no /l*v log: it would record it)
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart ENVIRONMENT=`"$environment`" ENROLLMENT_TOKEN=`"$token`"" -Wait -PassThru
    $token = $null
    Remove-Item $msi -Force -ErrorAction SilentlyContinue
    if ($p.ExitCode -eq 0 -or $p.ExitCode -eq 3010) {
        Write-LiteDeployResult 'uptodate' (Get-InstalledVersion) $null "$(if ($Action -eq 'Install') { 'Installed' } else { 'Updated to' }) $($latest.Version)"
        exit $p.ExitCode
    }
    Write-LiteDeployResult 'error' $installed $latest.Version "msiexec exit code $($p.ExitCode)"
    exit 1
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
