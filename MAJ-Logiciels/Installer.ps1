# Installs or updates MAJ-Logiciels from this folder (run on the admin VM, as the deployment account).
#   - code of the package  (Paquet\*.ps1, *.cmd)        -> <DeployRoot>\Winget-upgrade
#   - code of the tools    (Outils\*.ps1, example)      -> <MajBase>\Outils
#   - packages.txt and package.json are only seeded when absent: the site's versions are never replaced
#   - with -Profil <folder>: then imports that site profile (Outils\profil.ps1 -Import)
# Settings files (config.json, package.json, packages.txt, catalog.json, lists) are never overwritten by an update.
#
#   .\Installer.ps1 -Profil D:\profils\monsite          first install, or update + reapply the profile
#   .\Installer.ps1                                        update the code only (paths from <MajBase>\Outils\config.json)
#   .\Installer.ps1 -MajBase D:\MAJ -DryRun                show what would be copied
param(
    [string]$Profil,
    [string]$MajBase = "C:\MAJ-Logiciels",
    [switch]$DryRun
)

$repo = $PSScriptRoot
# Paths: the profile's config.json, else the installed one
$majConfigOverride = $null
foreach ($c in @($(if ($Profil) { Join-Path $Profil "config.json" }), (Join-Path $MajBase "Outils\config.json"))) {
    if ($c -and (Test-Path $c)) { $majConfigOverride = $c; break }
}
if (-not $majConfigOverride) { Write-Error "No config.json (profile or $MajBase\Outils): copy Outils\config.example.json into a profile folder, adjust it, and use -Profil."; exit 1 }
. (Join-Path $repo "Outils\config.ps1")
Write-Host "Settings   : $majConfigOverride" -ForegroundColor DarkGray
Write-Host "DeployRoot : $deployRoot" -ForegroundColor DarkGray
Write-Host "MajBase    : $majBase" -ForegroundColor DarkGray
if (-not (Test-Path $deployRoot)) { Write-Error "Share not reachable: $deployRoot"; exit 1 }

function Install-File([string]$From, [string]$ToDir, [switch]$OnlyIfAbsent) {
    $to = Join-Path $ToDir (Split-Path $From -Leaf)
    if ($OnlyIfAbsent -and (Test-Path $to)) { Write-Host "  kept    $to" -ForegroundColor DarkGray; return }
    if ((Test-Path $to) -and (Get-FileHash $From).Hash -eq (Get-FileHash $to).Hash) { Write-Host "  same    $to" -ForegroundColor DarkGray; return }
    if ($DryRun) { Write-Host "  [dry run] $to"; return }
    New-Item -Path $ToDir -ItemType Directory -Force | Out-Null
    Copy-Item $From $to -Force
    Write-Host "  copied  $to" -ForegroundColor Green
}

Write-Host "Package -> $packageDir" -ForegroundColor Cyan
Get-ChildItem "$repo\Paquet" -File -Include *.ps1, *.cmd -Recurse | ForEach-Object { Install-File $_.FullName $packageDir }
foreach ($f in "packages.txt", "package.json") { Install-File "$repo\Paquet\$f" $packageDir -OnlyIfAbsent }
New-Item -Path "$deployRoot\Winget-upgrade-logs" -ItemType Directory -Force -WhatIf:$DryRun | Out-Null

Write-Host "Tools -> $majBase\Outils" -ForegroundColor Cyan
Get-ChildItem "$repo\Outils" -File -Include *.ps1, config.example.json -Recurse | ForEach-Object { Install-File $_.FullName "$majBase\Outils" }

if ($Profil) {
    Write-Host "Profile" -ForegroundColor Cyan
    $args2 = @{ Import = $Profil }; if ($DryRun) { $args2.DryRun = $true }
    & (Join-Path $(if ($DryRun) { "$repo\Outils" } else { "$majBase\Outils" }) "profil.ps1") @args2
}
Write-Host "Done. Panel: powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$majBase\Outils\outils-maj.ps1`"" -ForegroundColor Green
