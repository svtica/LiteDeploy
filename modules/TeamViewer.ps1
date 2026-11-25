# LiteDeploy-Module
# Label: TeamViewer
# Processes: TeamViewer, TeamViewer_Desktop
# Install: yes
# Instances: yes
# Parameter: File Path of the TeamViewer MSI under the shared cache, e.g. TeamViewer/TeamViewer_Host-15.83.7.msi
# Parameter: Edition default=Host Host or Full: the TeamViewer product of the MSI
# Parameter: DetectName default= DisplayName of the Uninstall entry, wildcards allowed (empty: TeamViewer Host for Host, TeamViewer for Full)
# Parameter: Version default= Version expected once installed (empty: ProductVersion of the MSI)
# Parameter: Publisher default="TeamViewer Germany GmbH" Organisation (O=) of the signer of the MSI (several: separated by ;)
# Parameter: CustomConfigId default= ID of the custom module of the Management Console (CUSTOMCONFIGID, then TeamViewer.exe customize)
# Parameter: SettingsFile default= Path of a .tvopt settings file under the shared cache (SETTINGSFILE)
# Parameter: MsiOptions default= Extra MSI properties from a fixed list, e.g. ENABLEOUTLOOKPLUGIN=false REMOVE=f.DesktopShortcut
# Parameter: AssignmentId secret default= Assignment ID of the Management Console (password of a stored credential), used on Install
# Parameter: ServiceStartup default=keep Startup type of the TeamViewer service after an install or update: keep, auto, manual or disabled
<#
.SYNOPSIS
    LiteDeploy update module: installs and updates TeamViewer (Host or full
    client) from its MSI in the shared installer cache, with the options of the
    site (custom module, settings file, account assignment, service startup).

.DESCRIPTION
    Generic: every value of a site comes from the parameters of the instance
    ("module:TeamViewer/<Instance>", values in Package list > Parameters... or
    imported from a catalog, see docs/modules.md). Nothing in this script is
    specific to an organisation. One instance per edition, e.g. TeamViewerHost
    for the computers of the users and TeamViewerFull for the IT team.

    Check: finds the installed version in the Uninstall entries (64 and 32-bit
    views) whose DisplayName matches DetectName, and compares it with the
    Version parameter, else the ProductVersion of the MSI (read from the file
    in the cache, without installing it). No entry -> notinstalled (the
    message names the other edition when it is installed); older -> outdated;
    else uptodate.

    Update (outdated only) and Install (notinstalled only; an installed product
    is left alone): copies the MSI (and the .tvopt of SettingsFile) from the
    cache to %windir%\LiteDeploy\work\TeamViewer, refuses the MSI unless its
    Authenticode signature is Valid and its signer has O=<Publisher>, then runs
        msiexec /i <msi> /qn /norestart [CUSTOMCONFIGID=..] [SETTINGSFILE=..] [MsiOptions]
    (a minor upgrade also gets REINSTALL=ALL REINSTALLMODE=vomus). Exit code 0
    success; 3010 and 1641 success, restart needed; anything else error, with
    the MSI log kept. The product must then be found with the expected version.
    Install refuses a computer where the other edition is installed: remove it
    first. Update refuses a product installed by the EXE setup (its Uninstall
    entry is not an MSI product code, often a 32-bit installation in
    Program Files (x86)): the module does not replace an EXE installation with
    the MSI; uninstall it first, then Install.

    CustomConfigId: given to the MSI (CUSTOMCONFIGID, applied by TeamViewer for
    the licenses that allow it), then, after every install and update,
    "TeamViewer.exe customize --remove --id <id>" removes the customization in
    place (if any) and applies this one, whatever the license did with the MSI
    property: the custom module (look, policy, account and group of the module)
    is applied again on each update.

    Install with AssignmentId: once installed, the TeamViewer service is started
    if needed and "TeamViewer.exe assignment --id <id> --retries=3 --timeout=120"
    assigns the device to the company of the Management Console. The ID is a
    secret parameter: it is never written to the output, and the worker masks
    it in everything the module reports. It is visible on the command line of
    TeamViewer.exe to the local administrators while the assignment runs.

    ServiceStartup, applied after an install or an update (the MSI sets the
    service to automatic): keep (Update: the startup type found before the
    update; Install: the one of the MSI), auto, manual or disabled (the
    service is also stopped). The quick command "Start the TeamViewer service"
    of LiteDeploy starts it again when needed.

    The computers only read the cache. Put the .tvopt in a folder that only
    the computer accounts and the IT administrators can read: an exported
    settings file can hold password data.

.PARAMETER Action
    Check, Update or Install.

.EXAMPLE
    powershell.exe -NoProfile -File TeamViewer.ps1 -Action Check
    Reports the installed and expected versions of the edition of the instance
    (run as SYSTEM by LiteDeploy.Worker.exe, with LITEDEPLOY_CACHE,
    LITEDEPLOY_INSTANCE and the LITEDEPLOY_PARAM_ variables set).
#>
param(
    [ValidateSet('Check', 'Update', 'Install')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
$sep = [IO.Path]::DirectorySeparatorChar
$serviceName = 'TeamViewer'
# MSI properties MsiOptions may set (documented by TeamViewer for the MSI), with the values allowed
$allowedMsiOptions = @{
    'ENABLEOUTLOOKPLUGIN'           = '^(true|false)$'
    'INSTALLSECURITYKEYREDIRECTION' = '^(0|1)$'
    'ADDLOCAL'                      = '^ALL$'
    'REMOVE'                        = '^f\.DesktopShortcut$'
}
# Meaning of the exit codes of "TeamViewer.exe customize" (TeamViewer knowledge base)
$customizeErrors = @{
    1 = 'invalid command line arguments'; 500 = 'internal error'; 501 = 'access denied'
    502 = 'download of the configuration timed out'; 503 = 'configuration made for another module'
    504 = 'restart of the GUI failed'; 505 = 'customization failed'; 506 = 'removal of the customization failed'
}
# Meaning of the exit codes of "TeamViewer.exe assignment" (TeamViewer knowledge base)
$assignmentErrors = @{
    1 = 'invalid command line arguments'; 2 = 'signature verification error'; 3 = 'product not installed'
    400 = 'invalid argument or assignment ID'; 401 = 'service not running'; 402 = 'service of an incompatible version'
    403 = 'not online'; 404 = 'another assignment is running'; 405 = 'timeout'; 406 = 'failed'
    407 = 'access denied'; 408 = 'denied by policy'
}

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

# Full path of a file in the cache; throws for a path that leaves the cache
function Resolve-CacheFile([string] $Relative, [string] $ParameterName) {
    if (-not $env:LITEDEPLOY_CACHE) { throw 'No shared installer cache: set one in App > Options > Software updates' }
    $rel = $Relative.Trim().Replace('/', [string]$sep).TrimStart($sep)
    if (-not $rel -or $rel.Contains(':') -or ($rel.Split($sep) -contains '..') -or [IO.Path]::IsPathRooted($rel)) {
        throw "Invalid $ParameterName parameter (a path under the cache is expected): $Relative"
    }
    $root = [IO.Path]::GetFullPath($env:LITEDEPLOY_CACHE).TrimEnd($sep) + $sep
    $full = [IO.Path]::GetFullPath((Join-Path $root $rel))
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid $ParameterName parameter (outside the cache): $Relative" }
    return $full
}

# "15.83.7", "v15.83 build 7" -> [version]; $null when there is no number
function ConvertTo-Version([string] $Text) {
    if (-not $Text) { return $null }
    $m = [regex]::Match($Text, '\d+(\.\d+){0,3}')
    if (-not $m.Success) { return $null }
    # Missing parts are 0: 15.83.7 and 15.83.7.0 are the same version ([version] would rank the first lower)
    $parts = @($m.Value.Split('.'))
    while ($parts.Count -lt 4) { $parts += '0' }
    try { return [version]($parts -join '.') } catch { return $null }
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
                            KeyName         = $name
                            DisplayName     = [string]$key.GetValue('DisplayName')
                            DisplayVersion  = [string]$key.GetValue('DisplayVersion')
                            InstallLocation = [string]$key.GetValue('InstallLocation')
                            Is32Bit         = ($view -eq [Microsoft.Win32.RegistryView]::Registry32)
                        }
                    } finally { $key.Close() }
                }
            } finally { $root.Close() }
        } finally { $base.Close() }
    }
}

# Installed entry whose DisplayName matches the pattern (highest version when several); $null: not installed
function Get-Installed([string] $Pattern) {
    $entries = @(Get-UninstallEntries | Where-Object { $_.DisplayName -and ($_.DisplayName -like $Pattern) })
    if ($entries.Count -eq 0) { return $null }
    return ($entries | Sort-Object { ConvertTo-Version $_.DisplayVersion } -Descending | Select-Object -First 1)
}

# Organisations of the Publisher parameter, separated by ; (a name in double quotes keeps a ;)
function Get-PublisherNames([string] $Publisher) {
    $names = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Text.StringBuilder
    $quoted = $false
    foreach ($c in (([string]$Publisher) + ';').ToCharArray()) {
        if ($c -eq [char]'"') { $quoted = -not $quoted }
        if ($c -eq [char]';' -and -not $quoted) {
            $name = $current.ToString().Trim().Trim('"').Trim()
            if ($name -and -not $names.Contains($name)) { $names.Add($name) }
            $null = $current.Clear()
        }
        else { $null = $current.Append($c) }
    }
    if ($current.Length -gt 0) {
        $name = $current.ToString().TrimEnd(';').Trim().Trim('"').Trim()
        if ($name -and -not $names.Contains($name)) { $names.Add($name) }
    }
    return $names.ToArray()
}

# The O= value of a certificate subject, without its quotes ('' when there is none)
function Get-SubjectOrganisation([string] $Subject) {
    $m = [regex]::Match([string]$Subject, '(?:^|,)\s*O=(?:"(?<q>(?:[^"]|"")*)"|(?<v>[^,]*))')
    if (-not $m.Success) { return '' }
    if ($m.Groups['q'].Success) { return $m.Groups['q'].Value.Replace('""', '"').Trim() }
    return $m.Groups['v'].Value.Trim()
}

# '' when the file has a valid signature of one of the organisations of Publisher, else why it is refused
function Get-SignatureRefusal([string] $Path, [string] $Publisher) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return "no valid signature ($($sig.Status))" }
    $actual = Get-SubjectOrganisation $sig.SignerCertificate.Subject
    foreach ($name in @(Get-PublisherNames $Publisher)) {
        if ($actual -ceq $name) { return '' }
    }
    if (-not $actual) { $actual = $sig.SignerCertificate.Subject }
    return "signed by $actual instead of $(@(Get-PublisherNames $Publisher) -join '; ')"
}

# MsiOptions checked against the fixed list: "NAME=value NAME=value" -> same text; throws otherwise
function Get-MsiOptions([string] $Text) {
    $parts = @()
    foreach ($token in ($Text -split '\s+')) {
        if (-not $token) { continue }
        $m = [regex]::Match($token, '^([A-Za-z]+)=(.+)$')
        if (-not $m.Success) { throw "Invalid MsiOptions entry (NAME=value expected): $token" }
        $name = $m.Groups[1].Value.ToUpperInvariant()
        if (-not $allowedMsiOptions.ContainsKey($name)) { throw "MsiOptions: $name is not in the list of allowed properties ($($allowedMsiOptions.Keys -join ', '))" }
        if ($m.Groups[2].Value -notmatch $allowedMsiOptions[$name]) { throw "MsiOptions: invalid value for $name" }
        $parts += "$name=$($m.Groups[2].Value)"
    }
    return ($parts -join ' ')
}

# Runs a program (timeout in minutes); returns its exit code
function Invoke-Program([string] $FilePath, [string] $Arguments, [int] $Minutes) {
    $proc = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    $null = $proc.Handle
    if (-not $proc.WaitForExit($Minutes * 60 * 1000)) {
        try { $proc.Kill() } catch { }
        throw "$(Split-Path -Leaf $FilePath) did not finish within $Minutes minutes"
    }
    return $proc.ExitCode
}

# Runs "TeamViewer.exe customize --remove --id <id>"; '' when it worked, else what failed
function Invoke-Customize($Entry, [string] $Id) {
    Start-TeamViewerService
    $exe = Get-TeamViewerExe $Entry
    $c = Invoke-Program $exe "customize --remove --id $Id" 5
    if ($c -eq 0) { return '' }
    $why = if ($customizeErrors.ContainsKey($c)) { $customizeErrors[$c] } else { 'unknown error' }
    return "customization $Id failed, code $c ($why)"
}

function Test-Outdated($Installed, [string] $Target) {
    $have = ConvertTo-Version $Installed.DisplayVersion
    $want = ConvertTo-Version $Target
    if ($have -and $want) { return $have -lt $want }
    return ($Installed.DisplayVersion -ne $Target)
}

# Startup type of the service ('Automatic', 'Manual', 'Disabled'); $null when it does not exist
function Get-StartupType {
    $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $svc) { return $null }
    return [string]$svc.StartType
}

# Starts the service (setting it to manual first when it is disabled) and waits for it
function Start-TeamViewerService {
    $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $svc) { throw "The $serviceName service is not installed" }
    if ((Get-StartupType) -eq 'Disabled') { Set-Service -Name $serviceName -StartupType Manual }
    if ($svc.Status -ne 'Running') {
        Start-Service -Name $serviceName
        $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
    }
}

# Applies ServiceStartup; $Before: the startup type found before an update ($null on Install)
function Set-ServiceStartup([string] $Wanted, [string] $Before) {
    if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) { return 'service not found' }
    switch ($Wanted) {
        'keep' {
            if ($Before) { Set-Service -Name $serviceName -StartupType $Before }
            $type = Get-StartupType
            if ($type -eq 'Disabled') { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue }
            return "service $type"
        }
        'auto'     { Set-Service -Name $serviceName -StartupType Automatic; Start-TeamViewerService; return 'service automatic' }
        'manual'   { Set-Service -Name $serviceName -StartupType Manual; return 'service manual' }
        'disabled' {
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            Set-Service -Name $serviceName -StartupType Disabled
            return 'service disabled'
        }
    }
}

# TeamViewer.exe of the installed product: its installation folder, else Program Files, else Program Files (x86)
# (a 32-bit installation; ProgramW6432 is the 64-bit folder even from a 32-bit PowerShell)
function Get-TeamViewerExe($Entry) {
    $dirs = @()
    if ($Entry.InstallLocation) { $dirs += $Entry.InstallLocation.Trim().Trim('"') }
    foreach ($root in @($env:ProgramW6432, $env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($root) { $dirs += (Join-Path $root 'TeamViewer') }
    }
    foreach ($d in $dirs) {
        $exe = Join-Path $d 'TeamViewer.exe'
        if (Test-Path -LiteralPath $exe -PathType Leaf) { return $exe }
    }
    throw 'TeamViewer.exe not found in the installation folder'
}

$work = Join-Path $env:windir 'LiteDeploy\work\TeamViewer'
$local = $null
$localSettings = $null
try {
    $instance = if ($env:LITEDEPLOY_INSTANCE) { $env:LITEDEPLOY_INSTANCE } else { 'TeamViewer' }
    $file = Get-Param 'File'
    if (-not $file) { throw 'Parameter File missing: path of the TeamViewer MSI under the cache' }
    if ([IO.Path]::GetExtension($file) -ne '.msi') { throw "File must be the MSI of TeamViewer (the EXE setup has no version and no deployment options): $file" }
    $edition = (Get-Param 'Edition')
    if (-not $edition) { $edition = 'Host' }
    if ($edition -notin @('Host', 'Full')) { throw "Edition must be Host or Full: $edition" }
    $detectName = Get-Param 'DetectName'
    if (-not $detectName) { $detectName = if ($edition -eq 'Host') { 'TeamViewer Host' } else { 'TeamViewer' } }
    if (($detectName -replace '[\*\?\[\]\s]', '').Length -lt 3) { throw 'DetectName needs at least 3 characters besides wildcards' }
    $otherName = if ($edition -eq 'Host') { 'TeamViewer' } else { 'TeamViewer Host' }
    $publisher = Get-Param 'Publisher'
    if (@(Get-PublisherNames $publisher).Count -eq 0) { throw 'Parameter Publisher missing: the signer of the MSI must be named' }
    $startup = (Get-Param 'ServiceStartup').ToLowerInvariant()
    if (-not $startup) { $startup = 'keep' }
    if ($startup -notin @('keep', 'auto', 'manual', 'disabled')) { throw "ServiceStartup must be keep, auto, manual or disabled: $startup" }
    $customConfigId = Get-Param 'CustomConfigId'
    if ($customConfigId -and $customConfigId -notmatch '^[A-Za-z0-9]{1,32}$') { throw 'CustomConfigId must be letters and digits only' }
    $msiOptions = Get-MsiOptions (Get-Param 'MsiOptions')

    $source = Resolve-CacheFile $file 'File'
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "MSI not found in the cache: $file" }
    $target = Get-Param 'Version'
    if (-not $target) { $target = Get-MsiProperty $source 'ProductVersion' }
    if (-not $target) { throw "Version of the MSI unknown: set the Version parameter ($file)" }

    $installed = Get-Installed $detectName
    $installedVersion = if ($installed) { $installed.DisplayVersion } else { $null }
    $outdated = $installed -and (Test-Outdated $installed $target)

    if ($Action -eq 'Check' -or ($installed -and -not $outdated) -or ($Action -eq 'Update' -and -not $installed) -or ($Action -eq 'Install' -and $installed)) {
        if (-not $installed) {
            $other = Get-Installed $otherName
            $note = if ($other) { "TeamViewer $edition not installed ($otherName $($other.DisplayVersion) is)" } else { "TeamViewer $edition not installed" }
            Write-LiteDeployResult 'notinstalled' $null $target $note
        }
        else {
            $notes = @()
            if ($Action -eq 'Install') { $notes += 'Already installed' }
            if ($installed.KeyName -notmatch '^\{[0-9A-Fa-f-]{36}\}$') {
                $notes += "installed by the EXE setup$(if ($installed.Is32Bit) { ' (32-bit)' }): uninstall it before the MSI can update it"
            }
            $text = if ($notes.Count -gt 0) { $notes -join '; ' } else { $null }
            if ($outdated) { Write-LiteDeployResult 'outdated' $installedVersion $target $text }
            else { Write-LiteDeployResult 'uptodate' $installedVersion $null $text }
        }
        exit 0
    }

    if (-not $installed) {
        $other = Get-Installed $otherName
        if ($other) { throw "Install of TeamViewer $edition refused: $otherName $($other.DisplayVersion) is installed, remove it first" }
    }
    if ($installed -and $installed.KeyName -notmatch '^\{[0-9A-Fa-f-]{36}\}$') {
        $where = if ($installed.InstallLocation) { " in $($installed.InstallLocation)" } elseif ($installed.Is32Bit) { ' (32-bit)' } else { '' }
        throw "TeamViewer $edition $installedVersion was installed by the EXE setup$where; this module does not replace it with the MSI: uninstall it first, then Install"
    }
    $startupBefore = if ($installed) { Get-StartupType } else { $null }

    # Copy, signature, install
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $local = Join-Path $work (Split-Path -Leaf $source)
    Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $source -Destination $local -Force
    $refusal = Get-SignatureRefusal $local $publisher
    if ($refusal) { throw "MSI refused ($file): $refusal" }
    if (-not (Get-Param 'Version')) { $target = Get-MsiProperty $local 'ProductVersion' }

    $properties = ''
    if ($customConfigId) { $properties += " CUSTOMCONFIGID=$customConfigId" }
    $settingsFile = Get-Param 'SettingsFile'
    if ($settingsFile) {
        if ([IO.Path]::GetExtension($settingsFile) -ne '.tvopt') { throw "SettingsFile must be a .tvopt file: $settingsFile" }
        $settingsSource = Resolve-CacheFile $settingsFile 'SettingsFile'
        if (-not (Test-Path -LiteralPath $settingsSource -PathType Leaf)) { throw "Settings file not found in the cache: $settingsFile" }
        $localSettings = Join-Path $work (Split-Path -Leaf $settingsSource)
        Copy-Item -LiteralPath $settingsSource -Destination $localSettings -Force
        $properties += " SETTINGSFILE=`"$localSettings`""
    }
    if ($msiOptions) { $properties += " $msiOptions" }

    $log = Join-Path $work "$instance.log"
    $msiCode = Get-MsiProperty $local 'ProductCode'
    $minor = if ($installed -and $msiCode -and $installed.KeyName -eq $msiCode) { ' REINSTALL=ALL REINSTALLMODE=vomus' } else { '' }
    $code = Invoke-Program (Join-Path $env:windir 'System32\msiexec.exe') "/i `"$local`" /qn /norestart$properties$minor /l*v `"$log`"" 13
    $restart = ($code -eq 3010 -or $code -eq 1641)
    $now = Get-Installed $detectName
    $nowVersion = if ($now) { $now.DisplayVersion } else { $null }
    if ($code -ne 0 -and -not $restart) {
        Write-LiteDeployResult 'error' $nowVersion $target "MSI of TeamViewer $edition failed, exit code $code (log: $log)"
        exit 1
    }
    if (-not $now) {
        Write-LiteDeployResult 'error' $null $target "MSI finished (exit code $code) but TeamViewer $edition is not found (check DetectName)"
        exit 1
    }
    if (Test-Outdated $now $target) {
        Write-LiteDeployResult 'error' $nowVersion $target "MSI finished (exit code $code) but version $nowVersion is installed, $target expected"
        exit 1
    }
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    $verb = if ($installed) { 'Updated' } else { 'Installed' }
    $notes = @("$verb TeamViewer $edition $nowVersion")

    $failed = $false
    # Custom module: applied again after every install and update (removes the one in place first)
    if ($customConfigId) {
        $customizeFailure = Invoke-Customize $now $customConfigId
        if ($customizeFailure) { $notes += $customizeFailure; $failed = $true }
        else { $notes += "customization $customConfigId applied" }
    }

    # Assignment to the company (Install only: an updated device keeps its assignment)
    $assignmentId = Get-Param 'AssignmentId'
    if ($Action -eq 'Install' -and $assignmentId) {
        Start-TeamViewerService
        $exe = Get-TeamViewerExe $now
        $assignCode = Invoke-Program $exe "assignment --id $assignmentId --retries=3 --timeout=120" 5
        if ($assignCode -eq 0) { $notes += 'assigned' }
        else {
            $why = if ($assignmentErrors.ContainsKey($assignCode)) { $assignmentErrors[$assignCode] } else { 'unknown error' }
            $notes += "assignment failed, code $assignCode ($why)"
            $failed = $true
        }
    }
    $assignmentId = $null

    $notes += (Set-ServiceStartup $startup $startupBefore)
    if ($failed) {
        Write-LiteDeployResult 'error' $nowVersion $null ($notes -join '; ')
        exit 1
    }
    Write-LiteDeployResult 'uptodate' $nowVersion $null ($notes -join '; ')
    if ($restart) { exit 3010 }
    exit 0
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
finally {
    foreach ($f in @($local, $localSettings)) { if ($f) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue } }
}
