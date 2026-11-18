# LiteDeploy-Module
# Label: Installer from the shared cache
# Processes:
# Install: yes
# Instances: yes
# Parameter: Product default= Name of the product, shown in the messages (the instance name when empty)
# Parameter: File Path of the installer under the shared cache, e.g. CachedInstaller/ExampleVendor/app-2.4.1.msi
# Parameter: Type default= msi or exe (empty: from the file extension)
# Parameter: Arguments default= Silent install arguments (msi: /qn /norestart when empty; required for an exe)
# Parameter: DetectName default= Wildcard on the DisplayName of the Uninstall entry, e.g. "Contoso Example App*"
# Parameter: ProductCode default= MSI product code {GUID} of the Uninstall entry (instead of DetectName)
# Parameter: Version default= Version expected once installed (empty: ProductVersion of the MSI, or version of the EXE)
# Parameter: Publisher Organisation (O=) of the Authenticode signer of the installer, e.g. ExampleVendor Inc.
# Parameter: CloseProcesses default= Processes closed by the module right before the install (comma-separated, without .exe)
<#
.SYNOPSIS
    LiteDeploy update module: installs or updates a vendor MSI or EXE installer
    kept in the shared installer cache, one product per instance.

.DESCRIPTION
    Generic: everything about the product comes from the parameters of the
    instance ("module:CachedInstaller/<Instance>" in the package list, values
    in Package list > Parameters... or imported from a catalog, see
    docs/modules.md). Nothing in this script is specific to an organisation.

    Check: finds the installed version in the Uninstall entries of the registry
    (64 and 32-bit views), by ProductCode or by DetectName (wildcard on
    DisplayName), and compares it with the expected version: the Version
    parameter, else the ProductVersion of the MSI (read from the file in the
    cache, without installing it), else the product or file version of the EXE.
    No entry -> notinstalled; older -> outdated; else uptodate.

    Update (outdated only) and Install (notinstalled or outdated): copies the
    installer from <cache>\<File> to %windir%\LiteDeploy\work\CachedInstaller
    (never C:\Windows\Temp), refuses it unless its Authenticode signature is
    Valid and its signer's subject has O=<Publisher>, closes the processes of
    CloseProcesses, runs it (msiexec /i for an msi, the exe itself otherwise)
    with the Arguments, reads its exit code (0 success; 3010 and 1641 success,
    restart needed) and checks again. The computers only read the cache.

    The Processes line of the header is the same for every instance: it is
    empty. The processes of a product are closed by the module itself
    (CloseProcesses), right before its installer runs; only plain process
    names are accepted and system processes are refused.

.PARAMETER Action
    Check, Update or Install.

.EXAMPLE
    powershell.exe -NoProfile -File CachedInstaller.ps1 -Action Check
    Reports the installed and expected versions of the product of the instance
    (run as SYSTEM by LiteDeploy.Worker.exe, with LITEDEPLOY_CACHE,
    LITEDEPLOY_INSTANCE and the LITEDEPLOY_PARAM_ variables set).
#>
param(
    [ValidateSet('Check', 'Update', 'Install')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
$sep = [IO.Path]::DirectorySeparatorChar
# Never closed, whatever CloseProcesses says
$protectedProcesses = @('system', 'idle', 'registry', 'smss', 'csrss', 'wininit', 'winlogon', 'services', 'lsass', 'lsaiso',
    'svchost', 'dwm', 'fontdrvhost', 'explorer', 'msiexec', 'powershell', 'pwsh', 'conhost', 'litedeploy.worker', 'memory compression')

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

# Full path of the installer in the cache; throws for a path that leaves the cache
function Resolve-CacheFile([string] $Relative) {
    if (-not $env:LITEDEPLOY_CACHE) { throw 'No shared installer cache: set one in App > Options > Software updates' }
    $rel = $Relative.Trim().Replace('/', [string]$sep).TrimStart($sep)
    if (-not $rel -or $rel.Contains(':') -or ($rel.Split($sep) -contains '..') -or [IO.Path]::IsPathRooted($rel)) {
        throw "Invalid File parameter (a path under the cache is expected): $Relative"
    }
    $root = [IO.Path]::GetFullPath($env:LITEDEPLOY_CACHE).TrimEnd($sep) + $sep
    $full = [IO.Path]::GetFullPath((Join-Path $root $rel))
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid File parameter (outside the cache): $Relative" }
    return $full
}

# "2.4.1.0", "v2.4 build 7" -> [version]; $null when there is no number
function ConvertTo-Version([string] $Text) {
    if (-not $Text) { return $null }
    $m = [regex]::Match($Text, '\d+(\.\d+){0,3}')
    if (-not $m.Success) { return $null }
    $v = $m.Value
    if ($v -notmatch '\.') { $v += '.0' }
    try { return [version]$v } catch { return $null }
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

# Version the installer brings: the Version parameter, else the MSI ProductVersion, else the EXE version
function Get-TargetVersion([string] $Path, [string] $Type) {
    $given = Get-Param 'Version'
    if ($given) { return $given }
    if ($Type -eq 'msi') { return (Get-MsiProperty $Path 'ProductVersion') }
    $info = (Get-Item -LiteralPath $Path).VersionInfo
    foreach ($text in @($info.ProductVersion, $info.FileVersion)) { if (ConvertTo-Version $text) { return $text.Trim() } }
    return $null
}

# Uninstall entries of the 64 and 32-bit views (whatever the bitness of this PowerShell)
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
                            KeyName        = $name
                            DisplayName    = [string]$key.GetValue('DisplayName')
                            DisplayVersion = [string]$key.GetValue('DisplayVersion')
                        }
                    } finally { $key.Close() }
                }
            } finally { $root.Close() }
        } finally { $base.Close() }
    }
}

# Installed entry of the product (highest version when several match); $null: not installed
function Get-Installed([string] $ProductCode, [string] $DetectName) {
    $entries = @(Get-UninstallEntries | Where-Object {
        if ($ProductCode) { $_.KeyName -eq $ProductCode } else { $_.DisplayName -and ($_.DisplayName -like $DetectName) }
    })
    if ($entries.Count -eq 0) { return $null }
    return ($entries | Sort-Object { ConvertTo-Version $_.DisplayVersion } -Descending | Select-Object -First 1)
}

# Valid signature and O=<Publisher> in the subject of the signer (quoted when it has a comma)
function Test-PublisherSignature([string] $Path, [string] $Publisher) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return $false }
    $subject = $sig.SignerCertificate.Subject
    $escaped = [regex]::Escape($Publisher)
    return ($subject -match "(^|,\s*)O=$escaped(,|$)") -or ($subject -match "(^|,\s*)O=`"$escaped`"(,|$)")
}

# Plain process names of CloseProcesses (without .exe); throws for an invalid or protected name
function Get-ProcessesToClose([string] $List) {
    $names = @()
    foreach ($item in ($List -split ',')) {
        $name = $item.Trim()
        if (-not $name) { continue }
        if ($name.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) { $name = $name.Substring(0, $name.Length - 4) }
        if ($name -notmatch '^[A-Za-z0-9 ._+-]{1,64}$') { throw "Invalid process name in CloseProcesses: $item" }
        if ($protectedProcesses -contains $name.ToLowerInvariant()) { throw "CloseProcesses cannot close the system process $name" }
        $names += $name
    }
    return $names
}

function Close-Processes([string[]] $Names) {
    if (-not $Names -or $Names.Count -eq 0) { return }
    foreach ($name in $Names) { Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
    $still = @($Names | Where-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue })
    if ($still.Count -gt 0) { throw "Could not close $($still -join ', ')" }
}

# Runs the installer (13 minutes at most, the worker allows 15); returns its exit code
function Invoke-Installer([string] $FilePath, [string] $Arguments) {
    $proc = if ($Arguments) { Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru -WindowStyle Hidden }
            else { Start-Process -FilePath $FilePath -PassThru -WindowStyle Hidden }
    $null = $proc.Handle
    if (-not $proc.WaitForExit(13 * 60 * 1000)) {
        try { $proc.Kill() } catch { }
        throw 'The installer did not finish within 13 minutes'
    }
    return $proc.ExitCode
}

function Test-Outdated($Installed, [string] $Target) {
    $have = ConvertTo-Version $Installed.DisplayVersion
    $want = ConvertTo-Version $Target
    if ($have -and $want) { return $have -lt $want }
    return ($Installed.DisplayVersion -ne $Target)
}

$work = Join-Path $env:windir 'LiteDeploy\work\CachedInstaller'
$local = $null
try {
    $instance = if ($env:LITEDEPLOY_INSTANCE) { $env:LITEDEPLOY_INSTANCE } else { 'CachedInstaller' }
    $product = Get-Param 'Product'
    if (-not $product) { $product = $instance }
    $file = Get-Param 'File'
    $publisher = Get-Param 'Publisher'
    $productCode = Get-Param 'ProductCode'
    $detectName = Get-Param 'DetectName'
    if (-not $file) { throw 'Parameter File missing' }
    if (-not $publisher) { throw 'Parameter Publisher missing: the signer of the installer must be named' }
    if ($productCode -and $productCode -notmatch '^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$') {
        throw "Invalid ProductCode (a {GUID} is expected): $productCode"
    }
    if (-not $productCode -and ($detectName -replace '[\*\?\[\]\s]', '').Length -lt 3) {
        throw 'Parameter ProductCode or DetectName missing (DetectName needs at least 3 characters besides wildcards)'
    }

    $source = Resolve-CacheFile $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Installer not found in the cache: $file" }
    $type = (Get-Param 'Type').ToLowerInvariant()
    if (-not $type) { $type = [IO.Path]::GetExtension($source).TrimStart('.').ToLowerInvariant() }
    if ($type -ne 'msi' -and $type -ne 'exe') { throw "Type must be msi or exe: $type" }
    $arguments = Get-Param 'Arguments'
    if ($arguments -match '[\r\n]') { throw 'Arguments must be on one line' }
    if (-not $arguments) {
        if ($type -eq 'exe') { throw 'Parameter Arguments missing: an exe needs its silent install arguments' }
        $arguments = '/qn /norestart'
    }

    $installed = Get-Installed $productCode $detectName
    $target = Get-TargetVersion $source $type
    if (-not $target) { throw "Version of the installer unknown: set the Version parameter ($file)" }
    $installedVersion = if ($installed) { $installed.DisplayVersion } else { $null }
    $outdated = $installed -and (Test-Outdated $installed $target)

    if ($Action -eq 'Check' -or ($installed -and -not $outdated) -or ($Action -eq 'Update' -and -not $installed)) {
        if (-not $installed) { Write-LiteDeployResult 'notinstalled' $null $target "$product not installed" }
        elseif ($outdated) { Write-LiteDeployResult 'outdated' $installedVersion $target }
        else { Write-LiteDeployResult 'uptodate' $installedVersion }
        exit 0
    }

    # Update of an outdated product, or Install: copy, signature, install
    $closing = Get-ProcessesToClose (Get-Param 'CloseProcesses')
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $local = Join-Path $work (Split-Path -Leaf $source)
    Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $source -Destination $local -Force
    if (-not (Test-PublisherSignature $local $publisher)) { throw "Installer refused: no valid signature of $publisher ($file)" }
    # The version of the local, verified copy is the one installed
    $target = Get-TargetVersion $local $type

    Close-Processes $closing
    if ($type -eq 'msi') {
        $log = Join-Path $work "$instance.log"
        $code = Invoke-Installer (Join-Path $env:windir 'System32\msiexec.exe') "/i `"$local`" $arguments /l*v `"$log`""
    } else {
        $log = $null
        $code = Invoke-Installer $local $arguments
    }

    $now = Get-Installed $productCode $detectName
    $nowVersion = if ($now) { $now.DisplayVersion } else { $null }
    $restart = ($code -eq 3010 -or $code -eq 1641)
    if ($code -ne 0 -and -not $restart) {
        $where = if ($log) { " (log: $log)" } else { '' }
        Write-LiteDeployResult 'error' $nowVersion $target "Installer of $product failed, exit code $code$where"
        exit 1
    }
    if (-not $now) {
        Write-LiteDeployResult 'error' $null $target "Installer of $product finished (exit code $code) but the product is not found (check DetectName or ProductCode)"
        exit 1
    }
    if (Test-Outdated $now $target) {
        Write-LiteDeployResult 'error' $nowVersion $target "Installer of $product finished (exit code $code) but version $nowVersion is installed, $target expected"
        exit 1
    }
    if ($log) { Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue }
    $verb = if ($installed) { 'Updated' } else { 'Installed' }
    Write-LiteDeployResult 'uptodate' $nowVersion $null "$verb $product $nowVersion"
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
