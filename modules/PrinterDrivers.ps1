# LiteDeploy-Module
# Label: Printer drivers (shared cache)
# Processes:
<#
.SYNOPSIS
    LiteDeploy update module: pre-stages printer drivers from the shared
    installer cache.

.DESCRIPTION
    The list of drivers comes from the shared installer cache set in LiteDeploy
    (LITEDEPLOY_CACHE): <cache>\PrinterDrivers\drivers.csv, columns Name,Inf.
    Name is the driver name as Get-PrinterDriver shows it once installed; Inf is
    the path of the driver INF relative to the PrinterDrivers folder. The driver
    package (the folder of the INF, with its .cat, .cab, .dll... files) sits
    next to the CSV.

    Check: every listed driver installed (Get-PrinterDriver) -> uptodate; some
    missing -> outdated (the message says how many and which ones); no cache
    configured or CSV unreadable -> error.

    Update: for each missing driver, copies the folder of its INF to
    %windir%\LiteDeploy\work\PrinterDrivers (a folder only SYSTEM and
    administrators can write to), refuses the package unless its catalog
    (.cat) files have a valid Authenticode signature, stages it with
    pnputil /add-driver, installs the driver with Add-PrinterDriver, cleans the
    work folder, then reports like Check. No process is closed and no printer
    is created or changed. See docs/modules.md for the contract and the cache
    layout.

.PARAMETER Action
    Check or Update.

.EXAMPLE
    powershell.exe -NoProfile -File PrinterDrivers.ps1 -Action Check
    Reports whether the drivers listed in the cache are installed (run as
    SYSTEM by LiteDeploy.Worker.exe, with LITEDEPLOY_CACHE set).
#>
param(
    [ValidateSet('Check', 'Update')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'

function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

# <cache>\PrinterDrivers, or throws when no cache is configured
function Get-SourceFolder {
    if (-not $env:LITEDEPLOY_CACHE) {
        throw 'No shared installer cache configured (App > Options > Software updates): the driver list is read from <cache>\PrinterDrivers\drivers.csv'
    }
    return (Join-Path $env:LITEDEPLOY_CACHE 'PrinterDrivers')
}

# Rows of drivers.csv (Name, Inf), or throws when the file is missing, unreadable or empty
function Get-DriverList([string] $Source) {
    $csv = Join-Path $Source 'drivers.csv'
    if (-not (Test-Path -LiteralPath $csv -PathType Leaf)) { throw "Driver list not found or not readable: $csv" }
    $rows = @(Import-Csv -LiteralPath $csv)
    if ($rows.Count -gt 0) {
        $columns = @($rows[0].PSObject.Properties | ForEach-Object { $_.Name })
        if ($columns -notcontains 'Name' -or $columns -notcontains 'Inf') { throw "drivers.csv must have the columns Name,Inf: $csv" }
    }
    $list = @($rows | Where-Object { $_.Name -and $_.Name.Trim() } |
        ForEach-Object { [pscustomobject]@{ Name = $_.Name.Trim(); Inf = ([string]$_.Inf).Trim() } })
    if ($list.Count -eq 0) { throw "No driver listed in $csv" }
    return $list
}

# Names of the drivers listed but not installed
function Get-MissingDriver($List) {
    $installed = @(Get-PrinterDriver | ForEach-Object { $_.Name })
    return @($List | Where-Object { $installed -notcontains $_.Name })
}

# Full path of an INF given relative to the PrinterDrivers folder; throws when it leaves that folder
function Resolve-InfPath([string] $Source, [string] $Inf) {
    if (-not $Inf -or [IO.Path]::IsPathRooted($Inf) -or $Inf -notmatch '\.inf$') {
        throw "Inf must be the relative path of an .inf file below PrinterDrivers: '$Inf'"
    }
    $root = [IO.Path]::GetFullPath($Source).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath((Join-Path $Source $Inf))
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Inf outside the PrinterDrivers folder: '$Inf'" }
    # The folder of the INF is copied: it must be the driver's own folder, not the whole cache
    if ((Split-Path -Parent $full).TrimEnd('\') + '\' -eq $root) { throw "Inf must be in a subfolder of PrinterDrivers (one folder per driver package): '$Inf'" }
    return $full
}

# Catalogs of a package: those named by CatalogFile in the INF that exist, else every .cat next to it
function Get-PackageCatalog([string] $InfPath) {
    $dir = Split-Path -Parent $InfPath
    $named = @(Get-Content -LiteralPath $InfPath | ForEach-Object {
        $m = [regex]::Match($_, '^\s*CatalogFile(\.[A-Za-z0-9_]+)?\s*=\s*([^;\s]+)', 'IgnoreCase')
        if ($m.Success) { Join-Path $dir $m.Groups[2].Value.Trim('"') }
    } | Select-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($named.Count -gt 0) { return $named }
    return @(Get-ChildItem -LiteralPath $dir -Filter *.cat -File | ForEach-Object { $_.FullName })
}

# pnputil of the 64-bit system even from a 32-bit PowerShell
function Get-PnpUtil {
    $native = Join-Path $env:windir 'Sysnative\pnputil.exe'
    if (Test-Path -LiteralPath $native) { return $native }
    return (Join-Path $env:windir 'System32\pnputil.exe')
}

# Runs a console program; returns its exit code and output (stderr does not stop the script)
function Invoke-Native([string] $FilePath, [string[]] $Arguments) {
    $ErrorActionPreference = 'Continue'
    $out = & $FilePath @Arguments 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out.Trim() }
}

function Write-Status($List, $Missing) {
    $present = $List.Count - $Missing.Count
    if ($Missing.Count -eq 0) {
        Write-LiteDeployResult 'uptodate' "$present" $null "$present printer driver(s) installed"
        return
    }
    $names = ($Missing | Select-Object -First 5 | ForEach-Object { $_.Name }) -join ', '
    if ($Missing.Count -gt 5) { $names += ', ...' }
    Write-LiteDeployResult 'outdated' "$present" "$($List.Count)" "$($Missing.Count) of $($List.Count) printer driver(s) missing: $names"
}

$work = Join-Path $env:windir 'LiteDeploy\work\PrinterDrivers'
try {
    $source = Get-SourceFolder
    $list = @(Get-DriverList $source)
    $missing = @(Get-MissingDriver $list)

    if ($Action -eq 'Check' -or $missing.Count -eq 0) {
        Write-Status $list $missing
        exit 0
    }

    # Work folder writable by SYSTEM and administrators only (never C:\Windows\Temp)
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    $failures = @()
    $restart = $false
    $index = 0
    # One package (INF) can hold several drivers: stage it once
    foreach ($group in ($missing | Group-Object Inf)) {
        $index++
        $names = @($group.Group | ForEach-Object { $_.Name })
        try {
            $inf = Resolve-InfPath $source $group.Name
            if (-not (Test-Path -LiteralPath $inf -PathType Leaf)) { throw "INF not found: $($group.Name)" }

            # Copy the package first, then check what was copied
            $target = Join-Path $work ([string]$index)
            Copy-Item -LiteralPath (Split-Path -Parent $inf) -Destination $target -Recurse -Force
            $localInf = Join-Path $target (Split-Path -Leaf $inf)

            # The catalog signature covers the hashes of the package files; pnputil checks them
            $catalogs = @(Get-PackageCatalog $localInf)
            if ($catalogs.Count -eq 0) { throw "Package refused: no catalog (.cat) next to $($group.Name)" }
            foreach ($cat in $catalogs) {
                $sig = Get-AuthenticodeSignature -LiteralPath $cat
                if ($sig.Status -ne 'Valid') { throw "Package refused: catalog $(Split-Path -Leaf $cat) signature $($sig.Status)" }
            }

            $pnp = Invoke-Native (Get-PnpUtil) @('/add-driver', $localInf)
            if ($pnp.ExitCode -eq 3010) { $restart = $true }
            elseif ($pnp.ExitCode -ne 0) { throw "pnputil exit code $($pnp.ExitCode): $($pnp.Output)" }

            foreach ($name in $names) {
                try { Add-PrinterDriver -Name $name }
                catch { $failures += "$name ($($_.Exception.Message))" }
            }
        }
        catch {
            foreach ($name in $names) { $failures += "$name ($($_.Exception.Message))" }
        }
    }

    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue

    $missing = @(Get-MissingDriver $list)
    if ($missing.Count -eq 0) {
        Write-Status $list $missing
        if ($restart) { exit 3010 }
        exit 0
    }
    $present = $list.Count - $missing.Count
    $detail = if ($failures.Count -gt 0) { ($failures | Select-Object -First 3) -join '; ' } else { 'driver(s) still missing after the update' }
    Write-LiteDeployResult 'error' "$present" "$($list.Count)" "$($missing.Count) of $($list.Count) printer driver(s) missing: $detail"
    exit 1
}
catch {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
