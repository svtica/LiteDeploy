# LiteDeploy-Module
# Label: PowerShell 7 (MSI, all users)
# Processes: pwsh
# Install: yes
# Parameter: Version default= Version to keep installed, e.g. 7.6.10 (empty: the newest MSI of the cache folder PowerShell, else the latest LTS release)
# Parameter: MsiProperties default= Extra MSI properties, NAME=value separated by spaces, e.g. ADD_PATH=1 USE_MU=0 ENABLE_MU=0
<#
.SYNOPSIS
    LiteDeploy update module: PowerShell 7 installed for the whole computer
    from the official MSI package, and the version of Windows PowerShell 5.1
    (read only).

.DESCRIPTION
    Why a module and not the winget package Microsoft.PowerShell: since 7.6.0
    winget installs the MSIX package of PowerShell by default (per user, not
    for the computer), PowerShell 7.7 and later have no MSI package at all,
    and winget is missing on Windows Server 2022 and earlier. The MSI of the
    LTS line is the package Microsoft documents for enterprise deployment.

    Check: the MSI install of PowerShell 7 (Uninstall entry "PowerShell 7-x64",
    "-x86" or "-arm64", 64 and 32-bit views) against the target version:
      - the Version parameter when set;
      - else the newest PowerShell-<version>-win-<arch>.msi of the folder
        PowerShell of the shared installer cache (LITEDEPLOY_CACHE), when it
        holds one: the administrator chooses the version, the computers do
        not go to the Internet;
      - else the latest LTS release of tools/metadata.json of the PowerShell
        repository (the file install-powershell.ps1 reads).
    No MSI entry -> notinstalled (error when pwsh.exe is found in
    %ProgramFiles%\PowerShell\7 without one: a ZIP copy, not managed here);
    older -> outdated; else uptodate (a newer version is never downgraded).
    A target of 7.7 or later is an error: there is no MSI package for it.
    The message also gives the version of Windows PowerShell (part of
    Windows: never updated by this module) and the MSIX packages of
    PowerShell installed for some users (updated by the Store, not here).

    Update (outdated only) and Install (notinstalled only): the MSI comes from
    the cache, else from the GitHub release of the version, to
    %windir%\LiteDeploy\work\PowerShell7 (never C:\Windows\Temp). It is
    refused unless its Authenticode signature is Valid with
    O=Microsoft Corporation, its ProductName is PowerShell 7 for the same
    architecture and its ProductVersion is the target. Then
    msiexec /i /qn /norestart with MsiProperties (log kept on failure). Exit
    codes 0; 3010 and 1641: restart needed (reported as 3010). The module
    checks again: the target version must be installed. The worker closes
    the processes of the header (pwsh) before Update; nothing is closed
    before Install. A downloaded MSI is never written to the cache.

    Architecture: the one of the installed entry; for an install, the one of
    Windows (x64, arm64 or x86).

.PARAMETER Action
    Check, Update or Install.

.EXAMPLE
    powershell.exe -NoProfile -File PowerShell7.ps1 -Action Check
    Reports the installed and target versions of PowerShell 7 (run as SYSTEM
    by LiteDeploy.Worker.exe, with LITEDEPLOY_CACHE and the LITEDEPLOY_PARAM_
    variables set).
#>
param(
    [ValidateSet('Check', 'Update', 'Install')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
$metadataUrl = 'https://raw.githubusercontent.com/PowerShell/PowerShell/master/tools/metadata.json'
$releaseUrl = 'https://github.com/PowerShell/PowerShell/releases/download'
# First version published without an MSI package
$firstWithoutMsi = [version]'7.7.0'

function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

function Get-Param([string] $Name) {
    $value = [Environment]::GetEnvironmentVariable("LITEDEPLOY_PARAM_$Name")
    if ($value) { return $value.Trim() }
    return ''
}

# "7.6.10", "v7.6.10", "7.6.10.0" -> [version] 7.6.10 (three parts); $null for a preview or no version
function ConvertTo-PwshVersion([string] $Text) {
    $m = [regex]::Match([string]$Text, '^\s*v?(\d+)\.(\d+)\.(\d+)(\.\d+)?\s*$')
    if (-not $m.Success) { return $null }
    return New-Object Version ([int]$m.Groups[1].Value), ([int]$m.Groups[2].Value), ([int]$m.Groups[3].Value)
}

# Architecture of an MSI of PowerShell from the processor of Windows (not of this PowerShell process)
function Get-WindowsArchitecture {
    $cpu = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    switch ($cpu) {
        'AMD64' { return 'x64' }
        'ARM64' { return 'arm64' }
        'x86'   { return 'x86' }
    }
    throw "Processor architecture not supported: $cpu"
}

# MSI file name of a version and an architecture
function Get-MsiName([version] $Version, [string] $Architecture) {
    return "PowerShell-$($Version.Major).$($Version.Minor).$($Version.Build)-win-$Architecture.msi"
}

# Among file names, the MSI of the architecture to use: the one of Version when given, else the newest;
# $null when there is none. Returns Name and Version.
function Select-CacheMsi([string[]] $Names, [string] $Architecture, [version] $Pinned) {
    $pattern = '^PowerShell-(\d+\.\d+\.\d+)-win-' + [regex]::Escape($Architecture) + '\.msi$'
    $found = @()
    foreach ($name in $Names) {
        $m = [regex]::Match([string]$name, $pattern, 'IgnoreCase')
        if (-not $m.Success) { continue }
        $found += [pscustomobject]@{ Name = $name; Version = (ConvertTo-PwshVersion $m.Groups[1].Value) }
    }
    if ($Pinned) { $found = @($found | Where-Object { $_.Version -eq $Pinned }) }
    if ($found.Count -eq 0) { return $null }
    return ($found | Sort-Object Version -Descending | Select-Object -First 1)
}

# Latest LTS version of the metadata of the PowerShell repository (LTSReleaseTag, a list); $null when absent
function Get-LtsVersion($Metadata) {
    $versions = @(@($Metadata.LTSReleaseTag) | ForEach-Object { ConvertTo-PwshVersion $_ } | Where-Object { $_ })
    if ($versions.Count -eq 0) { return $null }
    return ($versions | Sort-Object -Descending | Select-Object -First 1)
}

# MsiProperties: NAME=value tokens only (no quote, no switch, nothing else on the command line)
function Get-MsiProperties([string] $Text) {
    $tokens = @(([string]$Text).Trim() -split '\s+' | Where-Object { $_ })
    foreach ($t in $tokens) {
        if ($t -cnotmatch '^[A-Z][A-Z0-9_]{0,63}=[A-Za-z0-9_.,:\\-]{0,260}$') { throw "Invalid MsiProperties item (NAME=value expected, no quotes): $t" }
    }
    return ($tokens -join ' ')
}

# O=Microsoft Corporation in a certificate subject
function Test-MicrosoftSubject([string] $Subject) {
    return ([string]$Subject -match '(^|,\s*)O=("?)Microsoft Corporation\2\s*(,|$)')
}

# Uninstall entries of PowerShell 7 MSI installs (64 and 32-bit views): DisplayVersion, Architecture
function Get-PowerShell7Entries {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $root = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
            if (-not $root) { continue }
            try {
                foreach ($name in $root.GetSubKeyNames()) {
                    $key = $root.OpenSubKey($name)
                    if (-not $key) { continue }
                    try {
                        $m = [regex]::Match([string]$key.GetValue('DisplayName'), '^PowerShell 7-(x64|x86|arm64)$')
                        $v = ConvertTo-PwshVersion ([string]$key.GetValue('DisplayVersion'))
                        if ($m.Success -and $v) {
                            [pscustomobject]@{ KeyName = $name; Version = $v; Architecture = $m.Groups[1].Value }
                        }
                    } finally { $key.Close() }
                }
            } finally { $root.Close() }
        } finally { $base.Close() }
    }
}

function Get-Installed {
    return (@(Get-PowerShell7Entries) | Sort-Object Version -Descending | Select-Object -First 1)
}

# Read only: Windows PowerShell (this process) and the MSIX packages of PowerShell of the users
function Get-Notes {
    $notes = @("Windows PowerShell $($PSVersionTable.PSVersion) (part of Windows, not updated)")
    try {
        $msix = @(Get-AppxPackage -AllUsers -Name 'Microsoft.PowerShell*' -ErrorAction Stop |
            ForEach-Object { "$($_.Name) $($_.Version)" } | Sort-Object -Unique)
        if ($msix.Count -gt 0) { $notes += "MSIX for some users (updated by the Store): $($msix -join ', ')" }
    } catch { }
    return ($notes -join '; ')
}

# Target: Version and Source (full path in the cache, else $null: download)
function Get-Target([string] $Architecture) {
    $pinnedText = Get-Param 'Version'
    $pinned = $null
    if ($pinnedText) {
        $pinned = ConvertTo-PwshVersion $pinnedText
        if (-not $pinned) { throw "Invalid Version parameter (7.x.y expected): $pinnedText" }
    }
    if ($env:LITEDEPLOY_CACHE) {
        $folder = Join-Path $env:LITEDEPLOY_CACHE 'PowerShell'
        if (Test-Path -LiteralPath $folder -PathType Container) {
            $names = @(Get-ChildItem -LiteralPath $folder -Filter '*.msi' -File | ForEach-Object { $_.Name })
            $hit = Select-CacheMsi $names $Architecture $pinned
            if ($hit) { return [pscustomobject]@{ Version = $hit.Version; Source = (Join-Path $folder $hit.Name) } }
        }
    }
    if ($pinned) { return [pscustomobject]@{ Version = $pinned; Source = $null } }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    $metadata = (Invoke-WebRequest -Uri $metadataUrl -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    $lts = Get-LtsVersion $metadata
    if (-not $lts) { throw "Latest LTS version of PowerShell unknown ($metadataUrl)" }
    return [pscustomobject]@{ Version = $lts; Source = $null }
}

# A property of the Property table of an MSI, read without installing it
function Get-MsiProperty([string] $Path, [string] $Property) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $null; $view = $null; $record = $null
    try {
        $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Path, 0))
        $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$Property'"))
        $null = $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($record) { return [string]$record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1)) }
        return $null
    }
    finally {
        if ($view) { try { $null = $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) } catch { } }
        foreach ($o in @($record, $view, $db, $installer)) { if ($o) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($o) } }
    }
}

# '' when the MSI is the PowerShell 7 package expected (signature, name, version), else why it is refused
function Get-MsiRefusal([string] $Path, [version] $Version, [string] $Architecture) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return "no valid signature ($($sig.Status))" }
    if (-not (Test-MicrosoftSubject $sig.SignerCertificate.Subject)) { return "not signed by Microsoft Corporation ($($sig.SignerCertificate.Subject))" }
    $name = Get-MsiProperty $Path 'ProductName'
    if ($name -ne "PowerShell 7-$Architecture") { return "ProductName is $name, PowerShell 7-$Architecture expected" }
    $have = ConvertTo-PwshVersion (Get-MsiProperty $Path 'ProductVersion')
    if ($have -ne $Version) { return "ProductVersion is $have, $Version expected" }
    return ''
}

# Runs msiexec (13 minutes at most, the worker allows 15); returns its exit code
function Invoke-Msiexec([string] $Arguments) {
    $proc = Start-Process -FilePath (Join-Path $env:windir 'System32\msiexec.exe') -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    $null = $proc.Handle
    if (-not $proc.WaitForExit(13 * 60 * 1000)) {
        try { $proc.Kill() } catch { }
        throw 'msiexec did not finish within 13 minutes'
    }
    return $proc.ExitCode
}

# Dot-sourced (unit tests): the functions only
if ($MyInvocation.InvocationName -eq '.') { return }

$work = Join-Path $env:windir 'LiteDeploy\work\PowerShell7'
$local = $null
try {
    $notes = Get-Notes
    $properties = Get-MsiProperties (Get-Param 'MsiProperties')
    $installed = Get-Installed
    $installedText = if ($installed) { [string]$installed.Version } else { $null }

    if (-not $installed) {
        $zip = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'
        if (Test-Path -LiteralPath $zip) {
            Write-LiteDeployResult 'error' $null $null "pwsh.exe found in $(Split-Path $zip) without an MSI install (ZIP copy?): not managed by this module. $notes"
            exit 1
        }
        if ($Action -ne 'Install') {
            Write-LiteDeployResult 'notinstalled' $null $null "PowerShell 7 (MSI) not installed. $notes"
            exit 0
        }
    }
    elseif ($Action -eq 'Install') {
        Write-LiteDeployResult 'uptodate' $installedText $null "Already installed. $notes"
        exit 0
    }

    $architecture = if ($installed) { $installed.Architecture } else { Get-WindowsArchitecture }
    $target = Get-Target $architecture
    if ($target.Version -ge $firstWithoutMsi) {
        Write-LiteDeployResult 'error' $installedText ([string]$target.Version) "PowerShell $($target.Version) has no MSI package (7.7 and later): set Version to a 7.6 release or put its MSI in the cache. $notes"
        exit 1
    }
    $outdated = $installed -and ($installed.Version -lt $target.Version)

    if ($Action -eq 'Check' -or ($installed -and -not $outdated)) {
        if ($outdated) { Write-LiteDeployResult 'outdated' $installedText ([string]$target.Version) $notes }
        elseif ($installed.Version -gt $target.Version) { Write-LiteDeployResult 'uptodate' $installedText $null "Newer than the target $($target.Version): left as it is. $notes" }
        else { Write-LiteDeployResult 'uptodate' $installedText $null $notes }
        exit 0
    }

    # Update of an outdated install, or Install: MSI from the cache or GitHub, checked, then msiexec
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $msiName = Get-MsiName $target.Version $architecture
    $local = Join-Path $work $msiName
    Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
    if ($target.Source) {
        Copy-Item -LiteralPath $target.Source -Destination $local -Force
    }
    else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
        $tag = "v$($target.Version.Major).$($target.Version.Minor).$($target.Version.Build)"
        Invoke-WebRequest -Uri "$releaseUrl/$tag/$msiName" -OutFile $local -UseBasicParsing -TimeoutSec 600
    }
    $refusal = Get-MsiRefusal $local $target.Version $architecture
    if ($refusal) { throw "MSI refused ($msiName): $refusal" }

    $log = Join-Path $work 'PowerShell7.log'
    $arguments = "/i `"$local`" /qn /norestart"
    if ($properties) { $arguments += " $properties" }
    $code = Invoke-Msiexec "$arguments /l*v `"$log`""
    $restart = ($code -eq 3010 -or $code -eq 1641)
    $now = Get-Installed
    $nowText = if ($now) { [string]$now.Version } else { $null }
    if ($code -ne 0 -and -not $restart) {
        Write-LiteDeployResult 'error' $nowText ([string]$target.Version) "msiexec exit code $code (log: $log)"
        exit 1
    }
    if (-not $now -or $now.Version -lt $target.Version) {
        Write-LiteDeployResult 'error' $nowText ([string]$target.Version) "msiexec finished (exit code $code) but PowerShell $($target.Version) is not installed (log: $log)"
        exit 1
    }
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    $verb = if ($installed) { 'Updated to' } else { 'Installed' }
    Write-LiteDeployResult 'uptodate' $nowText $null "$verb PowerShell $nowText$(if ($restart) { ' (restart needed)' }). $notes"
    if ($restart) { exit 3010 }
    exit 0
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
finally {
    if ($local) { Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue }
}
