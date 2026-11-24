# LiteDeploy-Module
# Label: PowerShell 7 (MSI, machine)
# Processes:
# Install: yes
# Parameter: Version default= Version to install, e.g. 7.6.10 (empty: the latest MSI release of Microsoft, the 7.6 LTS line)
# Parameter: Download default=yes yes: download the MSI from GitHub when the shared cache has no copy; no: the shared cache only (<cache>\PowerShell)
# Parameter: ClosePwsh default=yes yes: close the running pwsh.exe of PowerShell 7 before the install (unsaved work is lost); no: do not install while one runs
# Parameter: MsiProperties default= Options of the PowerShell MSI, e.g. "ADD_PATH=1 USE_MU=0 ENABLE_MU=0" (known names only, values 0 or 1)
<#
.SYNOPSIS
    LiteDeploy update module: PowerShell 7 installed for the machine from the
    MSI of Microsoft (Uninstall entry "PowerShell 7-<arch>").

.DESCRIPTION
    Why a module and not the winget package Microsoft.PowerShell: from 7.7,
    Microsoft publishes PowerShell 7 as MSIX and zip only; the 7.6 LTS line is
    the last one with an MSI. winget only offers the newest version of a
    package (an MSIX, installed per user), so it cannot keep an MSI
    installation on its line. This module keeps the MSI installation current
    on the latest MSI release (or the Version parameter).

    Check: the installed MSI (Uninstall entries, 64 and 32-bit views,
    DisplayName "PowerShell 7-x64", "-x86" or "-arm64"; the preview,
    "PowerShell 7-preview-...", is ignored) against the target version: the
    Version parameter, else the ReleaseTag of tools/metadata.json of the
    PowerShell repository (the file the official install-powershell.ps1 reads
    for its MSI, the latest 7.6.x), else (Download=no) the newest
    PowerShell-<version>-win-<arch>.msi of <cache>\PowerShell. No MSI entry:
    notinstalled (the message names an MSIX PowerShell 7 when one is
    installed: this module leaves it alone), or error when pwsh.exe is in
    %ProgramFiles%\PowerShell\7 without one (a zip copy, never overwritten).
    The architecture of the MSI is the one of the installed entry (an x86
    PowerShell 7 on an x64 computer stays x86), else the one of Windows.
    The message always gives the version of Windows PowerShell 5.1, read
    only: it is part of Windows and updated by Windows Update, never by
    LiteDeploy.

    Update (outdated only) and Install (notinstalled; refused next to an MSIX
    PowerShell 7): takes PowerShell-<version>-win-<arch>.msi from
    <cache>\PowerShell, else (Download=yes) from the GitHub release of
    Microsoft over HTTPS, copies it to %windir%\LiteDeploy\work\PowerShell7
    (never C:\Windows\Temp), refuses it unless its Authenticode signature is
    Valid with O=Microsoft Corporation, its ProductName is
    "PowerShell 7-<arch>" and its ProductVersion is the target,
    puts a downloaded copy in the cache for the other computers (best effort,
    one writer at a time), closes the pwsh.exe processes started from the
    installed PowerShell 7 folder (ClosePwsh=yes; with no, the install waits
    for a run where none is running), then msiexec /i /qn /norestart with
    the MsiProperties and a log (kept on failure). Exit code 0: success;
    3010 or 1641: success, restart needed (reported as 3010). It checks
    again: the target version must be installed.

    This module runs in Windows PowerShell 5.1 (powershell.exe of the worker),
    never in pwsh: closing pwsh does not stop it. Windows PowerShell 5.1 is
    never changed.

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
# No progress bar: Invoke-WebRequest of Windows PowerShell 5.1 downloads much slower with one
$ProgressPreference = 'SilentlyContinue'
$metadataUrl = 'https://raw.githubusercontent.com/PowerShell/PowerShell/master/tools/metadata.json'
$releaseUrl = 'https://github.com/PowerShell/PowerShell/releases/download'
$signer = 'Microsoft Corporation'
# Options of the PowerShell MSI accepted in MsiProperties (documented by Microsoft), values 0 or 1
$allowedMsiProperties = @('ADD_PATH', 'ADD_EXPLORER_CONTEXT_MENU_OPENPOWERSHELL', 'ADD_FILE_CONTEXT_MENU_RUNPOWERSHELL',
    'ENABLE_PSREMOTING', 'REGISTER_MANIFEST', 'USE_MU', 'ENABLE_MU', 'DISABLE_TELEMETRY')

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

# yes/no parameter (yes, true, 1 / no, false, 0); throws for anything else
function Get-YesNo([string] $Name, [bool] $Default) {
    $value = (Get-Param $Name).ToLowerInvariant()
    if (-not $value) { return $Default }
    if (@('yes', 'true', '1') -contains $value) { return $true }
    if (@('no', 'false', '0') -contains $value) { return $false }
    throw "Parameter $Name must be yes or no: $value"
}

# "7.6.10", "v7.6.10", "7.6.10.0" -> [version] 7.6.10 (missing parts are zero); $null without a number
function ConvertTo-Version([string] $Text) {
    if (-not $Text) { return $null }
    $m = [regex]::Match($Text, '\d+(\.\d+){0,3}')
    if (-not $m.Success) { return $null }
    $parts = @($m.Value.Split('.') | ForEach-Object { [int]$_ })
    while ($parts.Count -lt 4) { $parts += 0 }
    return New-Object Version($parts[0], $parts[1], $parts[2], $parts[3])
}

# Three-part version of a release tag or a version ("v7.6.10" -> "7.6.10"); throws for a preview or an invalid text
function Get-ReleaseVersion([string] $Text) {
    $t = ([string]$Text).Trim()
    if ($t.StartsWith('v')) { $t = $t.Substring(1) }
    if ($t -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid PowerShell version (x.y.z expected, no preview): $Text" }
    return $t
}

# Architecture of the MSI for this computer: x64, arm64 or x86 (whatever the bitness of this PowerShell)
function Get-PwshArchitecture([string] $Processor, [string] $Wow) {
    $arch = if ($Wow) { $Wow } else { $Processor }
    switch (([string]$arch).ToUpperInvariant()) {
        'AMD64' { return 'x64' }
        'ARM64' { return 'arm64' }
        'X86'   { return 'x86' }
    }
    throw "Processor architecture not supported by the PowerShell MSI: $arch"
}

# File name of the MSI of a release: PowerShell-7.6.10-win-x64.msi
function Get-MsiFileName([string] $Version, [string] $Arch) {
    return "PowerShell-$Version-win-$Arch.msi"
}

# msiexec options from MsiProperties: NAME=0 or NAME=1, known names only, separated by spaces; throws otherwise
function Get-MsiPropertyArguments([string] $Text) {
    $items = @()
    foreach ($token in (([string]$Text).Trim() -split '\s+')) {
        if (-not $token) { continue }
        $m = [regex]::Match($token, '^([A-Za-z_]+)=([01])$')
        if (-not $m.Success) { throw "Invalid MsiProperties item (NAME=0 or NAME=1 expected): $token" }
        $name = $m.Groups[1].Value.ToUpperInvariant()
        if ($allowedMsiProperties -notcontains $name) { throw "MsiProperties: $name is not an option of the PowerShell MSI accepted by this module" }
        $items += "$name=$($m.Groups[2].Value)"
    }
    return ($items -join ' ')
}

# Uninstall entries of the 64 and 32-bit views
function Get-UninstallEntries {
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
                        [pscustomobject]@{
                            KeyName         = $name
                            DisplayName     = [string]$key.GetValue('DisplayName')
                            DisplayVersion  = [string]$key.GetValue('DisplayVersion')
                            InstallLocation = [string]$key.GetValue('InstallLocation')
                        }
                    } finally { $key.Close() }
                }
            } finally { $root.Close() }
        } finally { $base.Close() }
    }
}

# The MSI entry of PowerShell 7 among Uninstall entries: "PowerShell 7-<arch>", the one of this
# architecture first, else the highest version; the preview is ignored. $null: not installed
function Select-PwshEntry($Entries, [string] $Arch) {
    $all = @($Entries | Where-Object { $_.DisplayName -match '^PowerShell 7-(x64|x86|arm64)$' })
    if ($all.Count -eq 0) { return $null }
    $same = @($all | Where-Object { $_.DisplayName -eq "PowerShell 7-$Arch" })
    $pool = if ($same.Count -gt 0) { $same } else { $all }
    return ($pool | Sort-Object { ConvertTo-Version $_.DisplayVersion } -Descending | Select-Object -First 1)
}

# Architecture of an MSI entry ("PowerShell 7-x86" -> x86): an update keeps the architecture installed
function Get-EntryArchitecture($Entry) {
    $m = [regex]::Match([string]$Entry.DisplayName, '^PowerShell 7-(x64|x86|arm64)$')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

# A copy of PowerShell 7 without MSI entry (zip extracted to the MSI folder): the MSI would overwrite it
function Test-UnmanagedCopy([string] $ProgramFiles) {
    return (Test-Path -LiteralPath (Join-Path $ProgramFiles 'PowerShell\7\pwsh.exe') -PathType Leaf)
}

# Version of an MSIX PowerShell 7 (any user), '' when there is none
function Get-MsixPwshVersion {
    try {
        $pkg = @(Get-AppxPackage -AllUsers -Name 'Microsoft.PowerShell*' -ErrorAction Stop |
            Where-Object { $_.Name -notmatch 'Preview' } | Sort-Object { [version]$_.Version } -Descending)
        if ($pkg.Count -gt 0) { return [string]$pkg[0].Version }
    } catch { }
    return ''
}

# Windows PowerShell 5.1 (this process): read only, for the message
function Get-WindowsPowerShellNote {
    return "Windows PowerShell $($PSVersionTable.PSVersion) (part of Windows, updated by Windows Update, not by LiteDeploy)"
}

# Newest PowerShell-<version>-win-<arch>.msi of a folder: version text, '' when there is none
function Get-CacheVersion([string] $Folder, [string] $Arch) {
    if (-not $Folder -or -not (Test-Path -LiteralPath $Folder -PathType Container)) { return '' }
    $best = $null; $bestText = ''
    foreach ($f in Get-ChildItem -LiteralPath $Folder -Filter "PowerShell-*-win-$Arch.msi" -File -ErrorAction SilentlyContinue) {
        $m = [regex]::Match($f.Name, "^PowerShell-(\d+\.\d+\.\d+)-win-$([regex]::Escape($Arch))\.msi$")
        if (-not $m.Success) { continue }
        $v = ConvertTo-Version $m.Groups[1].Value
        if (-not $best -or $v -gt $best) { $best = $v; $bestText = $m.Groups[1].Value }
    }
    return $bestText
}

# Latest MSI release of Microsoft: ReleaseTag of metadata.json (the line install-powershell.ps1 -UseMSI installs)
function Get-WebVersion {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $json = (Invoke-WebRequest -Uri $metadataUrl -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    $tag = [string]$json.ReleaseTag
    if (-not $tag) { throw 'Latest PowerShell MSI release unknown (no ReleaseTag in metadata.json)' }
    return (Get-ReleaseVersion $tag)
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

# O=Microsoft Corporation in a certificate subject (quoted or not)
function Test-MicrosoftSubject([string] $Subject) {
    return ([string]$Subject -match "(^|,\s*)O=(`"?)$([regex]::Escape($signer))\2\s*(,|$)")
}

# '' when the MSI has a valid signature of Microsoft and is the PowerShell 7 MSI of the expected version and
# architecture, else why it is refused
function Get-MsiRefusal([string] $Path, [string] $Version, [string] $Arch) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return "no valid signature ($($sig.Status))" }
    if (-not (Test-MicrosoftSubject $sig.SignerCertificate.Subject)) { return "not signed by $signer ($($sig.SignerCertificate.Subject))" }
    $productName = Get-MsiProperty $Path 'ProductName'
    if ($productName -ne "PowerShell 7-$Arch") { return "ProductName $productName instead of PowerShell 7-$Arch" }
    $msiVersion = Get-MsiProperty $Path 'ProductVersion'
    if ((ConvertTo-Version $msiVersion) -ne (ConvertTo-Version $Version)) { return "ProductVersion $msiVersion instead of $Version" }
    return ''
}

# The verified MSI of the version in the work folder: from the cache, else downloaded (Download=yes)
# and put in the cache (best effort, one writer at a time). Returns the local path, or throws.
function Get-Installer([string] $Version, [string] $Arch, [string] $CacheDir, [bool] $Download, [string] $Work) {
    $name = Get-MsiFileName $Version $Arch
    $local = Join-Path $Work $name
    Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
    $cached = if ($CacheDir) { Join-Path $CacheDir $name } else { $null }
    $refusal = ''
    if ($cached -and (Test-Path -LiteralPath $cached -PathType Leaf)) {
        Copy-Item -LiteralPath $cached -Destination $local -Force
        $refusal = Get-MsiRefusal $local $Version $Arch
        if (-not $refusal) { return $local }
        Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
        if (-not $Download) { throw "Installer of the cache refused ($name): $refusal" }
    }
    if (-not $Download) { throw "$name is not in the shared cache (<cache>\PowerShell) and Download is no" }

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri "$releaseUrl/v$Version/$name" -OutFile $local -UseBasicParsing -TimeoutSec 600
    $refusal = Get-MsiRefusal $local $Version $Arch
    if ($refusal) {
        Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
        throw "Downloaded installer refused ($name): $refusal"
    }

    if ($CacheDir) {
        $lock = $null
        try {
            New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null
            $lock = [IO.File]::Open((Join-Path $CacheDir '.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
            if (-not (Test-Path -LiteralPath $cached)) {
                Copy-Item -LiteralPath $local -Destination "$cached.partial" -Force
                Move-Item -LiteralPath "$cached.partial" -Destination $cached -Force
            }
        } catch {
            # Read-only cache or another computer writing: the cache stays as it is
        } finally {
            if ($lock) { $lock.Close() }
        }
    }
    return $local
}

# pwsh.exe processes of the installed PowerShell 7 (a process whose path cannot be read counts too)
function Get-PwshProcesses([string] $Folder) {
    $prefix = if ($Folder) { $Folder.TrimEnd('\') + '\' } else { '' }
    return @(Get-Process -Name 'pwsh' -ErrorAction SilentlyContinue | Where-Object {
        $path = $null
        try { $path = $_.Path } catch { }
        (-not $path) -or (-not $prefix) -or $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
    })
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

# Dot-sourced by the tests: the functions only
if ($MyInvocation.InvocationName -eq '.') { return }

$work = Join-Path $env:windir 'LiteDeploy\work\PowerShell7'
$local = $null
try {
    $note = Get-WindowsPowerShellNote
    $arch = Get-PwshArchitecture $env:PROCESSOR_ARCHITECTURE $env:PROCESSOR_ARCHITEW6432
    $download = Get-YesNo 'Download' $true
    $closePwsh = Get-YesNo 'ClosePwsh' $true
    $msiOptions = Get-MsiPropertyArguments (Get-Param 'MsiProperties')
    $cacheDir = if ($env:LITEDEPLOY_CACHE) { Join-Path $env:LITEDEPLOY_CACHE 'PowerShell' } else { $null }

    $installed = Select-PwshEntry (Get-UninstallEntries) $arch
    if ($installed) { $arch = Get-EntryArchitecture $installed }
    elseif (Test-UnmanagedCopy $env:ProgramFiles) {
        # A zip copy in the folder of the MSI: not managed, never overwritten
        throw "pwsh.exe found in $(Join-Path $env:ProgramFiles 'PowerShell\7') without an MSI install (zip copy?): not managed by this module; remove it first. $note"
    }

    $target = Get-Param 'Version'
    if ($target) { $target = Get-ReleaseVersion $target }
    elseif ($download) { $target = Get-WebVersion }
    else {
        if (-not $cacheDir) { throw 'Download is no and no shared installer cache is set (App > Options > Software updates)' }
        $target = Get-CacheVersion $cacheDir $arch
        if (-not $target) { throw "No PowerShell-<version>-win-$arch.msi in the shared cache (<cache>\PowerShell)" }
    }
    if ((ConvertTo-Version $target) -ge [version]'7.7.0.0') {
        throw "No MSI is published for PowerShell $target (7.7 and later: MSIX or zip only); set Version to a 7.6.x release"
    }

    $installedVersion = if ($installed) { $installed.DisplayVersion } else { $null }
    $outdated = $installed -and ((ConvertTo-Version $installedVersion) -lt (ConvertTo-Version $target))
    $msix = if (-not $installed) { Get-MsixPwshVersion } else { '' }

    if ($Action -eq 'Check' -or ($installed -and -not $outdated) -or ($Action -eq 'Update' -and -not $installed)) {
        if (-not $installed) {
            $why = if ($msix) { "PowerShell 7 MSI not installed (MSIX PowerShell $msix found: not managed by this module)" } else { 'PowerShell 7 MSI not installed' }
            Write-LiteDeployResult 'notinstalled' $null $target "$why; $note"
        }
        elseif ($outdated) { Write-LiteDeployResult 'outdated' $installedVersion $target $note }
        else { Write-LiteDeployResult 'uptodate' $installedVersion $null $note }
        exit 0
    }
    if (-not $installed -and $msix) {
        throw "Install refused: PowerShell $msix is installed as MSIX; remove it first or manage it with MsixProvision"
    }

    # Update of an outdated MSI, or Install: verified MSI, running pwsh, msiexec
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $local = Get-Installer $target $arch $cacheDir $download $work

    $folder = if ($installed -and $installed.InstallLocation) { $installed.InstallLocation } else { Join-Path $env:ProgramFiles 'PowerShell\7' }
    $running = Get-PwshProcesses $folder
    if ($running.Count -gt 0) {
        if (-not $closePwsh) { throw "Not installed: $($running.Count) pwsh.exe running and ClosePwsh is no (try again later)" }
        $running | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 3
        if ((Get-PwshProcesses $folder).Count -gt 0) { throw 'Could not close pwsh.exe: not installed' }
    }

    $log = Join-Path $work "PowerShell-$target.log"
    $arguments = "/i `"$local`" /qn /norestart"
    if ($msiOptions) { $arguments += " $msiOptions" }
    $code = Invoke-Msiexec "$arguments /l*v `"$log`""

    $now = Select-PwshEntry (Get-UninstallEntries) $arch
    $nowVersion = if ($now) { $now.DisplayVersion } else { $null }
    $restart = ($code -eq 3010 -or $code -eq 1641)
    if ($code -ne 0 -and -not $restart) {
        Write-LiteDeployResult 'error' $nowVersion $target "PowerShell $target MSI failed, exit code $code (log: $log)"
        exit 1
    }
    if (-not $now -or (ConvertTo-Version $nowVersion) -lt (ConvertTo-Version $target)) {
        Write-LiteDeployResult 'error' $nowVersion $target "PowerShell $target MSI finished (exit code $code) but version $nowVersion is installed"
        exit 1
    }
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    $verb = if ($installed) { 'Updated' } else { 'Installed' }
    Write-LiteDeployResult 'uptodate' $nowVersion $null "$verb PowerShell $nowVersion; $note"
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
