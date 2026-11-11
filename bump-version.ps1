<#
.SYNOPSIS
    Bumps the LiteDeploy version, commits, tags and pushes to publish a
    GitHub release.

.DESCRIPTION
    Updates every version location (Properties\AssemblyInfo.cs:
    AssemblyVersion and AssemblyFileVersion; Properties\app.manifest:
    assemblyIdentity), commits "Bump version to X" on main, tags
    v<Major.Minor.Build> and pushes both.

    Locally, pushing the tag triggers the "Build and Release" workflow. The
    "Release" workflow (Actions tab > Release > Run workflow) runs this script
    with -Bump or -Version and then calls the build itself, because a tag
    pushed from a workflow (GITHUB_TOKEN) does not trigger other workflows.

    Guards: clean working tree, branch main, git pull --ff-only, new version
    greater than the current one, tag not existing yet.

.PARAMETER Version
    The new version, 4 parts (e.g. "1.0.2.0").

.PARAMETER Bump
    Instead of -Version: increment the current version (patch = Build part,
    e.g. 1.0.1.0 -> 1.0.2.0; minor = 1.0.1.0 -> 1.1.0.0; major -> 2.0.0.0).

.EXAMPLE
    .\bump-version.ps1 -Version "1.0.2.0"
.EXAMPLE
    .\bump-version.ps1 -Bump patch
#>
[CmdletBinding(DefaultParameterSetName = 'Explicit')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Explicit')]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true, ParameterSetName = 'Bump')]
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump
)

$ErrorActionPreference = 'Stop'

$assemblyInfoFile = Join-Path $PSScriptRoot 'Properties\AssemblyInfo.cs'
$manifestFile     = Join-Path $PSScriptRoot 'Properties\app.manifest'

# Reads a text file and remembers whether it starts with a UTF-8 BOM, so that
# it is written back the same way (Windows PowerShell 5.1 Set-Content would
# write ANSI and damage the accented comments of app.manifest).
function Read-TextFile([string] $path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
    return @{ Text = $text; Bom = $bom }
}

function Write-TextFile([string] $path, [string] $text, [bool] $bom) {
    [IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($bom)))
}

# Refuse to tag a dirty tree or a branch other than main: the tag would publish
# a release that does not match what is on GitHub.
if (git status --porcelain) { throw 'Working tree has uncommitted changes; commit or stash them first.' }
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { throw "Releases are tagged from main (current branch: $branch)." }
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw 'git pull --ff-only failed; resolve it before bumping the version.' }

# Current version (the AssemblyVersion line that is not commented out)
$asmFile = Read-TextFile $assemblyInfoFile
$m = [regex]::Match($asmFile.Text, '(?m)^\[assembly:\s*AssemblyVersion\("(\d+)\.(\d+)\.(\d+)\.(\d+)"\)\]')
if (-not $m.Success) { throw "AssemblyVersion not found in $assemblyInfoFile" }
$current = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value).$($m.Groups[4].Value)"

if ($PSCmdlet.ParameterSetName -eq 'Bump') {
    $major = [int]$m.Groups[1].Value; $minor = [int]$m.Groups[2].Value; $build = [int]$m.Groups[3].Value
    switch ($Bump) {
        'major' { $major++; $minor = 0; $build = 0 }
        'minor' { $minor++; $build = 0 }
        'patch' { $build++ }
    }
    $Version = "$major.$minor.$build.0"
}

# Release tags use 3 parts: v<Major.Minor.Build>
$parts = $Version.Split('.')
$version3 = "$($parts[0]).$($parts[1]).$($parts[2])"
$tag = "v$version3"

if ([version]$Version -le [version]$current) { throw "New version $Version must be greater than the current $current." }
git fetch --tags --quiet origin
if (git tag --list $tag) { throw "Tag $tag already exists (release tags use Major.Minor.Build)." }

Write-Host "Bumping $current -> $Version (tag $tag)"

# AssemblyInfo.cs
$text = $asmFile.Text
$text = $text -replace '(?m)^(\[assembly:\s*AssemblyVersion\(")[^"]+("\)\])', "`${1}$Version`${2}"
$text = $text -replace '(?m)^(\[assembly:\s*AssemblyFileVersion\(")[^"]+("\)\])', "`${1}$Version`${2}"
Write-TextFile $assemblyInfoFile $text $asmFile.Bom

# app.manifest
$manFile = Read-TextFile $manifestFile
$text = $manFile.Text -replace '(<assemblyIdentity\s+version=")[^"]+(")', "`${1}$Version`${2}"
Write-TextFile $manifestFile $text $manFile.Bom

# (build-and-release.yml never stores a version: a workflow token cannot
# push changes to workflow files.)

Write-Host "Version updated to $Version in:"
Write-Host "  - Properties\AssemblyInfo.cs (AssemblyVersion, AssemblyFileVersion)"
Write-Host "  - Properties\app.manifest (assemblyIdentity)"

git add $assemblyInfoFile $manifestFile
git commit -m "Bump version to $Version"
if ($LASTEXITCODE -ne 0) { throw 'git commit failed.' }
git tag $tag
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw 'git push of main failed (branch protection?).' }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push of $tag failed." }

# Expose the result to a calling workflow
if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "version=$Version"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "tag=$tag"
}

Write-Host "`nDone! $tag pushed."
