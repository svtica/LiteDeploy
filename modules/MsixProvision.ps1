# LiteDeploy-Module
# Label: MSIX package (provisioned)
# Processes:
# Parameter: PackageName Package identity name of the MSIX (Name of its manifest), e.g. Contoso.ExampleApp
# Parameter: Folder Folder of the package under the shared installer cache, e.g. ExampleApp
# Parameter: File default= Package file name in the folder (default: the newest .msix or .msixbundle of that package)
# Parameter: Dependencies default= Dependency file names in the folder, separated by ; (default: the .appx and .msix files of a Dependencies subfolder)
# Parameter: License default= License XML file name in the folder (default: none, -SkipLicense)
# Parameter: Features default= Windows optional features to enable first, separated by ; (e.g. VirtualMachinePlatform)
# Parameter: Publisher default= Expected organisation (O=) of the package signer, several separated by ; (default: the Publisher of the package manifest)
# Parameter: WingetId default= winget package Id of the product, for the web version check of LiteDeploy (not used on the computers)
# Install: yes
# Instances: yes
<#
.SYNOPSIS
    LiteDeploy update module: provisions an MSIX package (and its dependency
    packages) for all users from the shared installer cache.

.DESCRIPTION
    Generic module: the package comes from the shared installer cache
    (LITEDEPLOY_CACHE), folder <cache>\<Folder>, and the parameters of the
    header (LITEDEPLOY_PARAM_<Name>) say which one.

    Check: version provisioned for all users (Get-AppxProvisionedPackage
    -Online, DisplayName = PackageName) against the version of the package of
    the cache, read from its AppxManifest.xml or AppxBundleManifest.xml (only
    that entry of the package is read, nothing is extracted; for a bundle, the
    newest application package it holds). Older -> outdated, same or newer ->
    uptodate, not provisioned -> notinstalled. No cache, no folder or no
    package of that name -> error.

    Update (provisioned package outdated) and Install (package not
    provisioned): copies the package, its dependencies and its license to
    %windir%\LiteDeploy\work\MsixProvision\<PackageName> (a folder only SYSTEM
    and administrators can write to, never C:\Windows\Temp), refuses every
    package file unless it has a valid Authenticode signature whose subject is
    the Publisher of its manifest (and, for the main package, whose
    organisation is one of those of the Publisher parameter when it is set),
    enables the
    optional features of the Features parameter, then runs
    Add-AppxProvisionedPackage -Online -Regions all with the dependencies and
    the license (else -SkipLicense). The work folder is removed and the result
    is reported like Check. A feature that needs a restart: exit code 3010.
    Update on a computer where the package is not provisioned changes nothing
    (notinstalled): only Install provisions it there.

    Parameters (LITEDEPLOY_PARAM_<Name>):
        PackageName   required, package identity name, e.g. Contoso.ExampleApp
        Folder        required, folder under the cache, e.g. ExampleApp
        File          package file name in Folder; default: the newest .msix
                      or .msixbundle of Folder whose identity is PackageName
        Dependencies  file names in Folder, separated by ";"; default: the
                      .appx and .msix files of Folder\Dependencies, for the
                      architectures this computer runs
        License       license XML file name in Folder; default: -SkipLicense
        Features      optional features to enable first, separated by ";"
        Publisher     expected organisation (O=) of the signer of the package;
                      several separated by ";" (any one is accepted, e.g.
                      the old and the new name of a vendor that renamed
                      itself)
        WingetId      winget package Id, read by LiteDeploy only (Tools > Web
                      versions...); ignored by this script
    See docs/modules.md for the contract and the cache layout.

.PARAMETER Action
    Check, Update or Install.

.EXAMPLE
    powershell.exe -NoProfile -File MsixProvision.ps1 -Action Check
    Reports the provisioned and the cache versions of the package (run as
    SYSTEM by LiteDeploy.Worker.exe, with LITEDEPLOY_CACHE and the
    LITEDEPLOY_PARAM_ variables set).
#>
param(
    [ValidateSet('Check', 'Update', 'Install')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'

# Parameters of the header (empty when an older LiteDeploy runs the module: checked below)
$packageName  = ([string]$env:LITEDEPLOY_PARAM_PackageName).Trim()
$folderName   = ([string]$env:LITEDEPLOY_PARAM_Folder).Trim()
$fileName     = ([string]$env:LITEDEPLOY_PARAM_File).Trim()
$dependencies = ([string]$env:LITEDEPLOY_PARAM_Dependencies).Trim()
$licenseName  = ([string]$env:LITEDEPLOY_PARAM_License).Trim()
$features     = ([string]$env:LITEDEPLOY_PARAM_Features).Trim()
$publisher    = ([string]$env:LITEDEPLOY_PARAM_Publisher).Trim()

function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

# Values of a ;-separated parameter, trimmed, without empty ones
function Split-List([string] $Text) {
    return @($Text -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# A file name of the package folder: a plain name, no folder, no wildcard
function Assert-FileName([string] $Name, [string] $What) {
    if ($Name -ne [IO.Path]::GetFileName($Name) -or $Name -match '[\\/:*?"<>|]' -or $Name -eq '.' -or $Name -eq '..') {
        throw "$What must be a file name of the package folder, without folder: '$Name'"
    }
}

# <cache>\<Folder>; throws when no cache is configured or the folder leaves the cache or does not exist
function Get-SourceFolder {
    if (-not $env:LITEDEPLOY_CACHE) {
        throw 'No shared installer cache configured (App > Options > Software updates): the package is read from <cache>\<Folder>'
    }
    if ([IO.Path]::IsPathRooted($folderName)) { throw "Folder must be relative to the shared installer cache: '$folderName'" }
    $root = [IO.Path]::GetFullPath($env:LITEDEPLOY_CACHE).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath((Join-Path $env:LITEDEPLOY_CACHE $folderName)).TrimEnd('\')
    if (-not ($full + '\').StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or ($full + '\') -eq $root) {
        throw "Folder must be a folder below the shared installer cache: '$folderName'"
    }
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Package folder not found or not readable: $full" }
    return $full
}

# Identity of a package (.msix, .appx, bundle), read from its manifest without extracting it:
# Name, Publisher, Version (for a bundle: the newest application package), Architecture
function Get-PackageIdentity([string] $Path) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $bundle = $false
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { $entry = $zip.GetEntry('AppxMetadata/AppxBundleManifest.xml'); $bundle = $true }
        if (-not $entry) { throw "No package manifest in $(Split-Path -Leaf $Path)" }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }

    $identity = $manifest.DocumentElement.Identity
    if (-not $identity -or -not $identity.Name -or -not $identity.Version) { throw "No identity in the manifest of $(Split-Path -Leaf $Path)" }
    $version = [string]$identity.Version
    $arch = [string]$identity.ProcessorArchitecture
    if ($bundle) {
        # The provisioned version is the one of the application package, not of the bundle
        $apps = @($manifest.DocumentElement.Packages.Package | Where-Object { $_.Type -eq 'application' -and $_.Version })
        if ($apps.Count -gt 0) {
            $version = ($apps | ForEach-Object { [version]$_.Version } | Sort-Object -Descending | Select-Object -First 1).ToString()
        }
        $arch = 'neutral'
    }
    if (-not $arch) { $arch = 'neutral' }
    return [pscustomobject]@{
        Path = $Path; Name = [string]$identity.Name; Publisher = [string]$identity.Publisher
        Version = $version; Architecture = $arch.ToLowerInvariant(); Bundle = $bundle
    }
}

# Main package of the folder: the File parameter, else the newest .msix or .msixbundle named PackageName
function Get-MainPackage([string] $Source) {
    if ($fileName) {
        Assert-FileName $fileName 'File'
        $path = Join-Path $Source $fileName
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Package not found: $path" }
        $package = Get-PackageIdentity $path
        if ($package.Name -ne $packageName) { throw "$fileName is the package $($package.Name), not $packageName" }
        return $package
    }
    $candidates = @(Get-ChildItem -LiteralPath $Source -File | Where-Object { $_.Extension -eq '.msix' -or $_.Extension -eq '.msixbundle' } |
        ForEach-Object { try { Get-PackageIdentity $_.FullName } catch { $null } } |
        Where-Object { $_ -and $_.Name -eq $packageName })
    if ($candidates.Count -eq 0) { throw "No .msix or .msixbundle of the package $packageName in $Source" }
    return ($candidates | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1)
}

# Architectures of packages this computer runs (Windows architecture, even from a 32-bit PowerShell)
function Get-SupportedArchitecture {
    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    switch ($arch) {
        'AMD64' { return @('neutral', 'x64', 'x86') }
        'ARM64' { return @('neutral', 'arm64', 'arm', 'x64', 'x86') }
        default { return @('neutral', 'x86') }
    }
}

# Dependency packages: the Dependencies parameter (files of the folder), else the .appx and .msix
# files of the Dependencies subfolder for the architectures of this computer
function Get-DependencyPackage([string] $Source) {
    $names = Split-List $dependencies
    if ($names.Count -gt 0) {
        return @($names | ForEach-Object {
            Assert-FileName $_ 'Dependencies'
            $path = Join-Path $Source $_
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Dependency package not found: $path" }
            Get-PackageIdentity $path
        })
    }
    $sub = Join-Path $Source 'Dependencies'
    if (-not (Test-Path -LiteralPath $sub -PathType Container)) { return @() }
    $supported = Get-SupportedArchitecture
    return @(Get-ChildItem -LiteralPath $sub -File | Where-Object { $_.Extension -eq '.appx' -or $_.Extension -eq '.msix' } |
        ForEach-Object { Get-PackageIdentity $_.FullName } | Where-Object { $supported -contains $_.Architecture })
}

# Newest version provisioned for all users ($null: not provisioned)
function Get-ProvisionedVersion {
    $packages = @(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq $packageName })
    if ($packages.Count -eq 0) { return $null }
    return ($packages | ForEach-Object { [version]$_.Version } | Sort-Object -Descending | Select-Object -First 1).ToString()
}

# Distinguished name in one canonical form, to compare a manifest Publisher with a certificate subject
function Format-DistinguishedName([string] $Name) {
    $dn = New-Object Security.Cryptography.X509Certificates.X500DistinguishedName($Name)
    return $dn.Decode([Security.Cryptography.X509Certificates.X500DistinguishedNameFlags]::None)
}

# Organisations of the Publisher parameter: separated by ; (a vendor that renamed itself signs with
# its new name), trimmed, without empty ones; a name in double quotes keeps a ; it holds
function Get-PublisherNames([string] $Publisher) {
    $names = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Text.StringBuilder
    $quoted = $false
    foreach ($c in (([string]$Publisher) + ';').ToCharArray()) {
        if ($c -eq [char]'"') { $quoted = -not $quoted }
        if ($c -eq [char]';' -and -not $quoted) {
            $name = $current.ToString().Trim()
            if ($name.Length -ge 2 -and $name.StartsWith('"') -and $name.EndsWith('"')) { $name = $name.Substring(1, $name.Length - 2).Trim() }
            if ($name -and -not $names.Contains($name)) { $names.Add($name) }
            $null = $current.Clear()
        }
        else { $null = $current.Append($c) }
    }
    if ($current.Length -gt 0) {
        # Unbalanced quote: the rest is one name
        $name = $current.ToString().TrimEnd(';').Trim().Trim('"').Trim()
        if ($name -and -not $names.Contains($name)) { $names.Add($name) }
    }
    return $names.ToArray()
}

# O=<one of the organisations of Publisher> in a certificate subject (quoted when it has a comma)
function Test-SubjectOrganisation([string] $Subject, [string] $Publisher) {
    foreach ($name in @(Get-PublisherNames $Publisher)) {
        $escaped = [regex]::Escape($name)
        if ($Subject -match "(^|,\s*)O=(`"?)$escaped\2\s*(,|$)") { return $true }
    }
    return $false
}

# The O= value of a certificate subject, without its quotes ('' when there is none)
function Get-SubjectOrganisation([string] $Subject) {
    $m = [regex]::Match([string]$Subject, '(?:^|,)\s*O=(?:"(?<q>(?:[^"]|"")*)"|(?<v>[^,]*))')
    if (-not $m.Success) { return '' }
    if ($m.Groups['q'].Success) { return $m.Groups['q'].Value.Replace('""', '"').Trim() }
    return $m.Groups['v'].Value.Trim()
}

# Throws unless the package file has a valid Authenticode signature whose subject is the Publisher
# of its manifest and, when given, whose organisation (O=) is one of those of $Organisation
function Assert-PackageSignature($Package, [string] $Organisation) {
    $leaf = Split-Path -Leaf $Package.Path
    $sig = Get-AuthenticodeSignature -LiteralPath $Package.Path
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { throw "Package refused: $leaf signature $($sig.Status)" }
    $subject = $sig.SignerCertificate.Subject
    if ((Format-DistinguishedName $subject) -ne (Format-DistinguishedName $Package.Publisher)) {
        throw "Package refused: $leaf is signed by '$subject', not by the Publisher of its manifest '$($Package.Publisher)'"
    }
    if (@(Get-PublisherNames $Organisation).Count -gt 0 -and -not (Test-SubjectOrganisation $subject $Organisation)) {
        $actual = Get-SubjectOrganisation $subject
        if (-not $actual) { $actual = $subject }
        $expected = @(Get-PublisherNames $Organisation) -join '; '
        throw "Package refused: $leaf is signed by $actual instead of $expected; add it to Publisher (separated by ;) if this vendor renamed itself (signer '$subject')"
    }
}

# Enables the optional features that are not enabled yet; returns $true when Windows asks for a restart
function Enable-Feature([string[]] $Names) {
    $restart = $false
    foreach ($name in $Names) {
        if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw "Invalid optional feature name: '$name'" }
        $feature = Get-WindowsOptionalFeature -Online -FeatureName $name
        if (-not $feature) { throw "Optional feature not found: $name" }
        if ($feature.State -eq 'Enabled') { continue }
        if ($feature.State -eq 'EnablePending') { $restart = $true; continue }
        $result = Enable-WindowsOptionalFeature -Online -FeatureName $name -All -NoRestart
        if ($result.RestartNeeded) { $restart = $true }
    }
    return $restart
}

$work = Join-Path $env:windir "LiteDeploy\work\MsixProvision\$packageName"
$cleanWork = $false
try {
    if (-not $packageName -or -not $folderName) {
        Write-LiteDeployResult 'error' $null $null 'Parameters missing: PackageName and Folder (Package list > Parameters...)'
        exit 1
    }
    if ($packageName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw "Invalid package name: '$packageName'" }

    $source = Get-SourceFolder
    $package = Get-MainPackage $source
    $available = $package.Version
    $installed = Get-ProvisionedVersion

    if ($Action -eq 'Check') {
        if (-not $installed) { Write-LiteDeployResult 'notinstalled' $null $available; exit 0 }
        if ([version]$installed -lt [version]$available) { Write-LiteDeployResult 'outdated' $installed $available; exit 0 }
        Write-LiteDeployResult 'uptodate' $installed
        exit 0
    }
    if (-not $installed -and $Action -ne 'Install') {
        Write-LiteDeployResult 'notinstalled' $null $available 'Not provisioned: only Install provisions it'
        exit 0
    }
    if ($installed -and [version]$installed -ge [version]$available) {
        Write-LiteDeployResult 'uptodate' $installed
        exit 0
    }

    $dependencyPackages = @(Get-DependencyPackage $source)
    $license = $null
    if ($licenseName) {
        Assert-FileName $licenseName 'License'
        $license = Join-Path $source $licenseName
        if (-not (Test-Path -LiteralPath $license -PathType Leaf)) { throw "License file not found: $license" }
    }

    # Work folder writable by SYSTEM and administrators only (never C:\Windows\Temp)
    $cleanWork = $true
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $depWork = Join-Path $work 'Dependencies'
    New-Item -ItemType Directory -Force -Path $depWork | Out-Null

    # Copy first, then check what was copied (the identity is read again from the copy)
    $localPackage = Join-Path $work (Split-Path -Leaf $package.Path)
    Copy-Item -LiteralPath $package.Path -Destination $localPackage -Force
    $localMain = Get-PackageIdentity $localPackage
    if ($localMain.Name -ne $packageName -or $localMain.Version -ne $available) { throw "The package changed in the cache while it was copied: $(Split-Path -Leaf $package.Path)" }
    Assert-PackageSignature $localMain $publisher

    $localDependencies = @()
    foreach ($dep in $dependencyPackages) {
        $target = Join-Path $depWork (Split-Path -Leaf $dep.Path)
        Copy-Item -LiteralPath $dep.Path -Destination $target -Force
        $localDep = Get-PackageIdentity $target
        Assert-PackageSignature $localDep $null
        $localDependencies += $target
    }

    $localLicense = $null
    if ($license) {
        $localLicense = Join-Path $work (Split-Path -Leaf $license)
        Copy-Item -LiteralPath $license -Destination $localLicense -Force
        try { [xml](Get-Content -LiteralPath $localLicense -Raw) | Out-Null }
        catch { throw "License refused: $licenseName is not an XML file" }
    }

    $restart = Enable-Feature (Split-List $features)

    $arguments = @{ Online = $true; PackagePath = $localPackage; Regions = 'all' }
    if ($localDependencies.Count -gt 0) { $arguments.DependencyPackagePath = $localDependencies }
    if ($localLicense) { $arguments.LicensePath = $localLicense } else { $arguments.SkipLicense = $true }
    $result = Add-AppxProvisionedPackage @arguments
    if ($result -and $result.RestartNeeded) { $restart = $true }

    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    $cleanWork = $false

    $now = Get-ProvisionedVersion
    if ($now -and [version]$now -ge [version]$available) {
        $note = "Provisioned $now"
        if ($restart) { $note += '; restart needed to finish enabling the optional features' }
        Write-LiteDeployResult 'uptodate' $now $null $note
        if ($restart) { exit 3010 }
        exit 0
    }
    Write-LiteDeployResult 'error' $now $available "Add-AppxProvisionedPackage finished but $packageName $available is not provisioned"
    exit 1
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
finally {
    if ($cleanWork) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
