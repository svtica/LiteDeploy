# Site profile: every customization of an installation in one folder, to keep it outside the code
# and to put it back after an update or on a new VM.
#
#   profil.ps1 -Export <folder>            copy the current customizations into <folder>
#   profil.ps1 -Import <folder> [-Force]   put them in place; the current files are saved first in
#                                          <MajBase>\Profil-backups\<date> (itself a profile, importable)
#   -DryRun                                show what would be copied, copy nothing
#
# Profile content (every file optional):
#   config.json          -> <this folder>                       admin VM settings (DeployRoot, MajBase, account...)
#   package.json         -> <DeployRoot>\Winget-upgrade         modules, messages to users
#   packages.txt         -> <DeployRoot>\Winget-upgrade         tracked winget packages
#   catalog.json         -> <DeployRoot>\Winget-upgrade         "Installer un logiciel" catalog
#   exclude.txt          -> <DeployRoot>\Winget-upgrade-logs    live lists, edited from the panel:
#   night-only.txt       -> <DeployRoot>\Winget-upgrade-logs      on import, an existing file is kept
#   agent-settings.json  -> <MajBase>                             unless -Force
param(
    [Parameter(Mandatory, ParameterSetName = 'Export')][string]$Export,
    [Parameter(Mandatory, ParameterSetName = 'Import')][string]$Import,
    [Parameter(ParameterSetName = 'Import')][switch]$Force,
    [switch]$DryRun
)

# On import, the paths (DeployRoot, MajBase) come from the profile's config.json when it has one
$majConfigOverride = $null
if ($Import) {
    if (-not (Test-Path $Import -PathType Container)) { Write-Error "Profile folder not found: $Import"; exit 1 }
    $p = Join-Path $Import "config.json"
    if (Test-Path $p) { $majConfigOverride = $p }
}
. (Join-Path $PSScriptRoot "config.ps1")

$liveFiles = "exclude.txt", "night-only.txt", "agent-settings.json"
$map = [ordered]@{
    "config.json"         = $PSScriptRoot
    "package.json"        = $packageDir
    "packages.txt"        = $packageDir
    "catalog.json"        = $packageDir
    "exclude.txt"         = "$deployRoot\Winget-upgrade-logs"
    "night-only.txt"      = "$deployRoot\Winget-upgrade-logs"
    "agent-settings.json" = $majBase
}
Write-Host "DeployRoot : $deployRoot" -ForegroundColor DarkGray
Write-Host "MajBase    : $majBase" -ForegroundColor DarkGray

function Copy-ProfileFile([string]$From, [string]$To) {
    if ($DryRun) { Write-Host "  [dry run] $From -> $To"; return }
    New-Item -Path (Split-Path $To) -ItemType Directory -Force | Out-Null
    Copy-Item -Path $From -Destination $To -Force -ErrorAction Stop
    Write-Host "  $From -> $To"
}

if ($Export) {
    Write-Host "Export to $Export" -ForegroundColor Cyan
    $n = 0
    foreach ($f in $map.Keys) {
        $src = Join-Path $map[$f] $f
        if (Test-Path $src) { Copy-ProfileFile $src (Join-Path $Export $f); $n++ } else { Write-Host "  $f : absent, skipped" -ForegroundColor DarkGray }
    }
    Write-Host "$n file(s) exported. The profile holds site data (computer names, paths): keep it out of public repositories." -ForegroundColor Green
    exit 0
}

# --- Import: save the current files, then copy ---
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $majBase "Profil-backups\$stamp"
Write-Host "Import from $Import (current files saved in $backup)" -ForegroundColor Cyan
$n = 0
foreach ($f in $map.Keys) {
    $src = Join-Path $Import $f
    if (-not (Test-Path $src)) { continue }
    $dst = Join-Path $map[$f] $f
    if ((Test-Path $dst) -and ($liveFiles -contains $f) -and -not $Force) {
        Write-Host "  $f : kept (live list, already present; -Force to replace)" -ForegroundColor Yellow
        continue
    }
    if (Test-Path $dst) {
        if ((Get-FileHash $src).Hash -eq (Get-FileHash $dst).Hash) { Write-Host "  $f : identical" -ForegroundColor DarkGray; continue }
        if (-not $DryRun) { New-Item -Path $backup -ItemType Directory -Force | Out-Null; Copy-Item $dst (Join-Path $backup $f) -Force }
    }
    try { Copy-ProfileFile $src $dst; $n++ } catch { Write-Host "  $f : FAILED - $($_.Exception.Message)" -ForegroundColor Red }
}
Write-Host "$n file(s) imported." -ForegroundColor Green
