# Settings and shared helpers for every MAJ-Logiciels script on the admin VM (dot-sourced at the top of each one).
#
# Two settings files, both optional (built-in defaults otherwise), both part of the site profile (see profil.ps1):
#   <this folder>\config.json                   admin VM: share path, local folder, deployment account, AD filter...
#   <DeployRoot>\Winget-upgrade\package.json    package: optional modules (Adobe Reader, 1Password), messages...
#                                                also read by the launcher and the worker on the workstations
# Tracked software = <DeployRoot>\Winget-upgrade\packages.txt (winget) + enabled modules.

# --- Admin VM settings (config.json) ---
$majConfigFile = if ($majConfigOverride) { $majConfigOverride } else { Join-Path $PSScriptRoot "config.json" }   # override: profil.ps1, Installer.ps1
$majConfig = $null
if (Test-Path $majConfigFile) {
    try { $majConfig = Get-Content $majConfigFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Write-Warning "config.json could not be read ($($_.Exception.Message)): defaults used" }
}
function Get-MajSetting([string]$Name, $Default) {
    if ($majConfig -and $null -ne $majConfig.$Name -and "$($majConfig.$Name)" -ne '') { $majConfig.$Name } else { $Default }
}
$deployRoot    = [string](Get-MajSetting 'DeployRoot'    '\\server\deploy$')   # share holding the package, the logs and the installers
$majBase       = [string](Get-MajSetting 'MajBase'       'C:\MAJ-Logiciels')    # local folder on the admin VM: agent files, reports
$deployAccount = [string](Get-MajSetting 'DeployAccount' 'DOMAIN\deploy')       # account the package runs as on the workstations
$orgName       = [string](Get-MajSetting 'OrgName'       '')                    # shown in the panel title
$adFilter      = [string](Get-MajSetting 'AdFilter'      '(&(objectCategory=computer)(operatingSystem=Windows 1*)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))')   # workstations to manage (LDAP)
$keepReports   = [int](Get-MajSetting 'KeepReports'      10)    # dated report files kept, per type
$staleDays     = [int](Get-MajSetting 'StaleDays'        90)    # no logon for this many days: computer shown as inactive
# How the package is started on a workstation by the panel and the agent:
#   System        (default) one-time task as SYSTEM: no password leaves the VM; the computer account reads the
#                 share (Domain Computers: read on Winget-upgrade, modify on the log and module cache folders)
#   DeployAccount one-time task as the deployment account (password stored by the task on each workstation)
$launchAs      = [string](Get-MajSetting 'LaunchAs'         'System')
if ($launchAs -notin 'System', 'DeployAccount') { Write-Warning "config.json: LaunchAs '$launchAs' unknown, System used"; $launchAs = 'System' }

# --- Package settings (package.json on the share) ---
$packageDir  = "$deployRoot\Winget-upgrade"
$pkgFile     = "$packageDir\packages.txt"
$packageFile = "$packageDir\package.json"
$majPackage = $null
if (Test-Path $packageFile) {
    try { $majPackage = Get-Content $packageFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Write-Warning "package.json could not be read ($($_.Exception.Message)): optional modules disabled" }
}
$useAdobe       = [bool]($majPackage -and $majPackage.Modules -and $majPackage.Modules.AdobeReader)
$useOnePassword = [bool]($majPackage -and $majPackage.Modules -and $majPackage.Modules.OnePassword)
$sender         = if ($majPackage -and $majPackage.Messages -and $majPackage.Messages.Sender) { [string]$majPackage.Messages.Sender } else { "Le service des TI" }   # signs messages to users
$senderLower    = $sender.Substring(0, 1).ToLower() + $sender.Substring(1)
$opMsiFile      = "$deployRoot\1Password\1PasswordSetup-latest.msi"
$adobeCacheFile = "$deployRoot\Adobe\adobe-latest.json"

# packages.txt: winget Id | processes to close (comma-separated) | label | DisplayName regex (group 1 = version, optional)
function Read-PackageList([string]$Path = $pkgFile) {
    foreach ($line in @(Get-Content $Path -Encoding UTF8 -ErrorAction SilentlyContinue)) {
        $line = $line.Trim()
        if (-not $line -or $line.StartsWith('#')) { continue }
        $p = @($line -split '\|' | ForEach-Object { $_.Trim() })
        $id = $p[0]
        [pscustomobject]@{
            Id        = $id
            Processes = @(if ($p.Count -gt 1) { $p[1] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ } })
            Label     = $(if ($p.Count -gt 2 -and $p[2]) { $p[2] } else { $id })
            Detect    = $(if ($p.Count -gt 3 -and $p[3]) { $p[3] } else { $null })
        }
    }
}

# Every tracked software: Label (column in the panel), Detect (regex on the uninstall DisplayName), Id (winget) and Kind
function Get-TrackedSoftware {
    $list = @(Read-PackageList | ForEach-Object { [pscustomobject]@{ Label = $_.Label; Detect = $_.Detect; Id = $_.Id; Kind = 'winget' } })
    if ($useAdobe)       { $list += [pscustomobject]@{ Label = 'Adobe';     Detect = '^Adobe Acrobat'; Id = $null; Kind = 'adobe' } }
    if ($useOnePassword) { $list += [pscustomobject]@{ Label = '1Password'; Detect = '^1Password$';   Id = $null; Kind = '1password' } }
    $list
}

function Get-MsiProperty([string]$Path, [string]$Property) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db   = $installer.GetType().InvokeMember("OpenDatabase", "InvokeMethod", $null, $installer, @($Path, 0))
    $view = $db.GetType().InvokeMember("OpenView", "InvokeMethod", $null, $db, @("SELECT Value FROM Property WHERE Property='$Property'"))
    [void]$view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null)
    $rec  = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
    $value = if ($rec) { $rec.GetType().InvokeMember("StringData", "GetProperty", $null, $rec, 1) } else { $null }
    [void]$view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null)
    foreach ($o in $rec, $view, $db, $installer) { if ($o) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($o) } }
    return [string]$value
}

# Adobe build number (2600322075) -> version (26.003.22075)
function ConvertTo-AdobeVersion([string]$Build) {
    if ($Build.Length -lt 9) { return $Build }
    "{0}.{1}.{2}" -f $Build.Substring(0, $Build.Length - 8), $Build.Substring($Build.Length - 8, 3), $Build.Substring($Build.Length - 5)
}

# Dates of the newest installers the enabled modules put on the share (a computer deployed before is "to do")
function Get-ModuleMarks {
    if ($useAdobe) {
        $msp = Get-ChildItem "$deployRoot\Adobe", "$deployRoot\Adobe-x64" -Filter "*.msp" -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($msp) { [pscustomobject]@{ What = "Adobe $($msp.Name)"; When = $msp.LastWriteTime } }
    }
    if ($useOnePassword) {
        $op = Get-Item $opMsiFile -ErrorAction SilentlyContinue
        if ($op) { [pscustomobject]@{ What = "1Password MSI"; When = $op.LastWriteTime } }
    }
}

# Latest known version of the enabled modules, by label
function Get-ModuleLatest {
    $r = @{}
    if ($useAdobe) {
        try { $ab = [string](Get-Content $adobeCacheFile -Raw -ErrorAction Stop | ConvertFrom-Json).ReaderX64; if ($ab.Length -ge 9) { $r['Adobe'] = ConvertTo-AdobeVersion $ab } } catch { }
    }
    if ($useOnePassword -and (Test-Path $opMsiFile)) {
        try { $r['1Password'] = Get-MsiProperty $opMsiFile 'ProductVersion' } catch { }
    }
    $r
}
