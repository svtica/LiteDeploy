# LiteDeploy-Module
# Label: PowerShell Gallery module (machine)
# Processes:
# Install: yes
# Instances: yes
# Parameter: Name default= Name of the module in the PowerShell Gallery, e.g. PSWindowsUpdate (empty: the instance name)
# Parameter: Publisher Organisation (O=) of the Authenticode signer of the module files, e.g. Example Author Ltd (several: separated by ;)
# Parameter: Version default= Version to install, e.g. 2.2.1.5 (empty: the latest stable version; previews are never installed)
# Parameter: Download default=yes yes: query and download from the PowerShell Gallery when the shared cache has no copy; no: the shared cache only (<cache>\PSGallery\<Name>)
# Parameter: ModulesFolder default=WindowsPowerShell WindowsPowerShell: %ProgramFiles%\WindowsPowerShell\Modules (PowerShell 5.1 and 7); PowerShell: %ProgramFiles%\PowerShell\Modules (PowerShell 7 only)
<#
.SYNOPSIS
    LiteDeploy update module: a PowerShell module of the PowerShell Gallery
    installed for the machine, one module per instance.

.DESCRIPTION
    Generic: everything about the module comes from the parameters of the
    instance ("module:PSGalleryModule/<Instance>"). It does not use
    PowerShellGet or the NuGet provider (old in Windows PowerShell 5.1 and
    interactive on first use): it reads the Gallery API and the .nupkg itself.

    Check: the installed versions are the <Version> folders of
    <ModulesFolder>\<Name> that hold <Name>.psd1 (highest wins); none:
    notinstalled. The target version is the Version parameter, else the
    latest stable version of the Gallery (FindPackagesById, IsPrerelease
    false, listed), else (Download=no) the highest <name>.<version>.nupkg of
    <cache>\PSGallery\<Name>. Older: outdated; else uptodate.

    Update (outdated only) and Install (notinstalled): takes
    <name>.<version>.nupkg from <cache>\PSGallery\<Name>, else (Download=yes)
    downloads it from the Gallery over HTTPS and refuses it unless its SHA512
    is the PackageHash the Gallery gives (a downloaded copy is put in the cache
    for the other computers, best effort, one writer at a time). The package
    is extracted to %windir%\LiteDeploy\work\PSGalleryModule\<instance>
    (never C:\Windows\Temp; an entry that leaves the folder is refused), and
    refused unless:
      - <Name>.psd1 is at its root with ModuleVersion = the target version and
        no Prerelease tag;
      - every code file (.ps1, .psm1, .psd1, .ps1xml, .cdxml, .dll, .exe) has a
        Valid Authenticode signature whose signer subject has O=<Publisher>,
        or the module has a catalog <Name>.cat with such a signature and
        Test-FileCatalog says Valid: an unsigned module, a changed file or
        another publisher is always refused;
      - every module of its RequiredModules is already installed for the
        machine (dependencies are not installed: add an instance for each).
    The files then go to <ModulesFolder>\<Name>\<ModuleVersion> through a
    .partial folder; older versions stay side by side (PowerShell loads the
    highest; a session that runs keeps the version it loaded). No process is
    closed. It checks again: the target version must be found.

.PARAMETER Action
    Check, Update or Install.

.EXAMPLE
    powershell.exe -NoProfile -File PSGalleryModule.ps1 -Action Check
    Reports the installed and target versions of the module of the instance
    (run as SYSTEM by LiteDeploy.Worker.exe, with LITEDEPLOY_INSTANCE and the
    LITEDEPLOY_PARAM_ variables set).
#>
param(
    [ValidateSet('Check', 'Update', 'Install')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
# No progress bar: Invoke-WebRequest of Windows PowerShell 5.1 downloads much slower with one
$ProgressPreference = 'SilentlyContinue'
$galleryApi = 'https://www.powershellgallery.com/api/v2'
# Files that run code: each must be signed by the publisher (or covered by a signed catalog)
$codeExtensions = @('.ps1', '.psm1', '.psd1', '.ps1xml', '.cdxml', '.dll', '.exe')

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

# Module name of the Gallery: letters, digits, . - _ (no path, no wildcard)
function Test-ModuleName([string] $Name) {
    return ([string]$Name -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$') -and ($Name -notmatch '\.\.')
}

# Stable version text: 1.2, 1.2.3 or 1.2.3.4 -> [version] with missing parts zero; $null otherwise (previews included)
function ConvertTo-ModuleVersion([string] $Text) {
    $t = ([string]$Text).Trim()
    if ($t -notmatch '^\d+(\.\d+){1,3}$') { return $null }
    $parts = @($t.Split('.') | ForEach-Object { [int]$_ })
    while ($parts.Count -lt 4) { $parts += 0 }
    return New-Object Version($parts[0], $parts[1], $parts[2], $parts[3])
}

# Folder of the modules for the machine: WindowsPowerShell or PowerShell (under Program Files)
function Get-ModulesRoot([string] $Choice, [string] $ProgramFiles) {
    switch (([string]$Choice).ToLowerInvariant()) {
        ''                  { return (Join-Path $ProgramFiles 'WindowsPowerShell\Modules') }
        'windowspowershell' { return (Join-Path $ProgramFiles 'WindowsPowerShell\Modules') }
        'powershell'        { return (Join-Path $ProgramFiles 'PowerShell\Modules') }
    }
    throw "ModulesFolder must be WindowsPowerShell or PowerShell: $Choice"
}

# Installed versions of a module in a modules folder: the <Version> folders that hold <Name>.psd1, highest first
function Get-InstalledVersions([string] $Root, [string] $Name) {
    $dir = Join-Path $Root $Name
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { return @() }
    $found = @()
    foreach ($d in Get-ChildItem -LiteralPath $dir -Directory -ErrorAction SilentlyContinue) {
        $v = ConvertTo-ModuleVersion $d.Name
        if ($v -and (Test-Path -LiteralPath (Join-Path $d.FullName "$Name.psd1") -PathType Leaf)) {
            $found += [pscustomobject]@{ Text = $d.Name; Version = $v }
        }
    }
    return @($found | Sort-Object Version -Descending)
}

# Is a module installed for the machine (Windows PowerShell, PowerShell 7 or Windows folders), at MinVersion at least?
function Test-MachineModule([string] $Name, [string] $MinVersion) {
    $min = ConvertTo-ModuleVersion $MinVersion
    $roots = @((Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules'), (Join-Path $env:ProgramFiles 'PowerShell\Modules'),
        (Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\Modules'))
    foreach ($root in $roots) {
        $dir = Join-Path $root $Name
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { continue }
        if (-not $min) { return $true }
        foreach ($v in Get-InstalledVersions $root $Name) { if ($v.Version -ge $min) { return $true } }
        $flat = Join-Path $dir "$Name.psd1"
        if (Test-Path -LiteralPath $flat -PathType Leaf) {
            $data = Import-PowerShellDataFile -LiteralPath $flat
            $have = ConvertTo-ModuleVersion ([string]$data.ModuleVersion)
            if ($have -and $have -ge $min) { return $true }
        }
    }
    return $false
}

# Entries of a FindPackagesById answer (Atom XML): Version, Prerelease, Listed, Hash, HashAlgorithm, Url
function Read-GalleryFeed([string] $Xml) {
    # No DTD, no external resource
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create((New-Object System.IO.StringReader($Xml)), $settings)
    $doc = New-Object System.Xml.XmlDocument
    try { $doc.Load($reader) } finally { $reader.Close() }
    $entries = @()
    foreach ($entry in $doc.SelectNodes("//*[local-name()='entry']")) {
        $props = $entry.SelectSingleNode("*[local-name()='properties']")
        if (-not $props) { continue }
        $get = { param($n) $node = $props.SelectSingleNode("*[local-name()='$n']"); if ($node) { $node.InnerText } else { '' } }
        $content = $entry.SelectSingleNode("*[local-name()='content']")
        $published = & $get 'Published'
        $entries += [pscustomobject]@{
            Version       = [string](& $get 'Version')
            Prerelease    = ((& $get 'IsPrerelease') -eq 'true')
            # An unlisted version has the publication date 1900-01-01
            Listed        = -not ([string]$published).StartsWith('1900-')
            Hash          = [string](& $get 'PackageHash')
            HashAlgorithm = [string](& $get 'PackageHashAlgorithm')
            Url           = if ($content) { [string]$content.GetAttribute('src') } else { '' }
        }
    }
    $next = $doc.SelectSingleNode("/*[local-name()='feed']/*[local-name()='link'][@rel='next']")
    return [pscustomobject]@{ Entries = $entries; Next = if ($next) { $next.GetAttribute('href') } else { '' } }
}

# The release to install among feed entries: the Version asked, else the highest stable listed one; $null when none
function Select-GalleryRelease($Entries, [string] $Version) {
    $stable = @($Entries | Where-Object { -not $_.Prerelease -and (ConvertTo-ModuleVersion $_.Version) })
    if ($Version) {
        $want = ConvertTo-ModuleVersion $Version
        return ($stable | Where-Object { (ConvertTo-ModuleVersion $_.Version) -eq $want } | Select-Object -First 1)
    }
    return ($stable | Where-Object { $_.Listed } | Sort-Object { ConvertTo-ModuleVersion $_.Version } -Descending | Select-Object -First 1)
}

# An HTTPS address of the PowerShell Gallery
function Test-GalleryUrl([string] $Url) {
    try { $u = [uri]$Url } catch { return $false }
    return $u.IsAbsoluteUri -and $u.Scheme -eq 'https' -and ($u.Host -eq 'www.powershellgallery.com' -or $u.Host.EndsWith('.powershellgallery.com'))
}

# The release of the Gallery (all pages of FindPackagesById, 20 at most)
function Get-GalleryRelease([string] $Name, [string] $Version) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $url = "$galleryApi/FindPackagesById()?id='$Name'"
    $all = @()
    for ($page = 0; $url -and $page -lt 20; $page++) {
        if (-not (Test-GalleryUrl $url)) { throw "Unexpected address in the Gallery answer: $url" }
        $feed = Read-GalleryFeed ((Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 60).Content)
        $all += $feed.Entries
        $url = $feed.Next
    }
    if ($all.Count -eq 0) { throw "Module $Name not found in the PowerShell Gallery" }
    $release = Select-GalleryRelease $all $Version
    if (-not $release) {
        if ($Version) { throw "Version $Version of $Name not found in the PowerShell Gallery (stable versions only)" }
        throw "No stable listed version of $Name in the PowerShell Gallery"
    }
    return $release
}

# Highest <name>.<version>.nupkg of a folder (stable versions): version text, '' when there is none
function Get-CacheVersion([string] $Folder, [string] $Name) {
    if (-not $Folder -or -not (Test-Path -LiteralPath $Folder -PathType Container)) { return '' }
    $best = $null; $bestText = ''
    foreach ($f in Get-ChildItem -LiteralPath $Folder -Filter '*.nupkg' -File -ErrorAction SilentlyContinue) {
        $prefix = "$Name."
        if (-not $f.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $text = $f.Name.Substring($prefix.Length, $f.Name.Length - $prefix.Length - '.nupkg'.Length)
        $v = ConvertTo-ModuleVersion $text
        if ($v -and (-not $best -or $v -gt $best)) { $best = $v; $bestText = $text }
    }
    return $bestText
}

# Relative path of a package entry, unescaped; '' for the files of the package format (not part of the module)
function Get-EntryPath([string] $FullName) {
    $path = [uri]::UnescapeDataString(([string]$FullName).Replace('\', '/'))
    if ($path.EndsWith('/')) { return '' }
    if ($path -eq '[Content_Types].xml' -or $path.StartsWith('_rels/') -or $path.StartsWith('package/')) { return '' }
    if ($path -notmatch '/' -and $path.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)) { return '' }
    return $path
}

# Extracts the module files of a .nupkg to an empty folder; throws for an entry that leaves it
function Expand-Nupkg([string] $Package, [string] $Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        if ($zip.Entries.Count -gt 20000) { throw 'Package refused: too many files' }
        foreach ($entry in $zip.Entries) {
            $rel = Get-EntryPath $entry.FullName
            if (-not $rel) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $root $rel.Replace('/', '\')))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Package refused: the entry $rel leaves the folder" }
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $zip.Dispose() }
}

# Organisations of the Publisher parameter: separated by ;, trimmed, without empty ones
function Get-PublisherNames([string] $Publisher) {
    return @(([string]$Publisher -split ';') | ForEach-Object { $_.Trim().Trim('"').Trim() } | Where-Object { $_ } | Select-Object -Unique)
}

# O=<one of the organisations of Publisher> in a certificate subject (quoted when it has a comma)
function Test-SubjectOrganisation([string] $Subject, [string] $Publisher) {
    foreach ($name in @(Get-PublisherNames $Publisher)) {
        $escaped = [regex]::Escape($name)
        if ($Subject -match "(^|,\s*)O=(`"?)$escaped\2\s*(,|$)") { return $true }
    }
    return $false
}

# '' when the file has a valid signature of one of the organisations of Publisher, else why it is refused
function Get-SignatureRefusal([string] $Path, [string] $Publisher) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return "no valid signature ($($sig.Status))" }
    if (-not (Test-SubjectOrganisation $sig.SignerCertificate.Subject $Publisher)) { return "signed by $($sig.SignerCertificate.Subject)" }
    return ''
}

# '' when the extracted module is signed by the publisher (catalog or each code file), else why it is refused
function Get-ModuleRefusal([string] $Folder, [string] $Name, [string] $Publisher) {
    $catalog = Join-Path $Folder "$Name.cat"
    if (Test-Path -LiteralPath $catalog -PathType Leaf) {
        $why = Get-SignatureRefusal $catalog $Publisher
        if ($why) { return "catalog $Name.cat: $why" }
        # The catalog does not list itself (as PowerShellGet checks it)
        $result = Test-FileCatalog -Path $Folder -CatalogFilePath $catalog -FilesToSkip "$Name.cat"
        if ([string]$result -ne 'Valid') { return "catalog $Name.cat: the files do not match it ($result)" }
        return ''
    }
    $base = [IO.Path]::GetFullPath($Folder).TrimEnd('\') + '\'
    $code = @(Get-ChildItem -LiteralPath $Folder -Recurse -File | Where-Object { $codeExtensions -contains $_.Extension.ToLowerInvariant() })
    foreach ($file in $code) {
        $why = Get-SignatureRefusal $file.FullName $Publisher
        $rel = if ($file.FullName.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { $file.FullName.Substring($base.Length) } else { $file.Name }
        if ($why) { return "$($rel): $why" }
    }
    return ''
}

# Name and minimum version of each RequiredModules item of a manifest
function Get-RequiredModules($Manifest) {
    $list = @()
    foreach ($item in @($Manifest.RequiredModules)) {
        if (-not $item) { continue }
        if ($item -is [string]) { $list += [pscustomobject]@{ Name = $item; Version = '' } }
        elseif ($item -is [hashtable]) {
            $v = if ($item.ModuleVersion) { [string]$item.ModuleVersion } elseif ($item.RequiredVersion) { [string]$item.RequiredVersion } else { '' }
            $list += [pscustomobject]@{ Name = [string]$item.ModuleName; Version = $v }
        }
    }
    return $list
}

function Get-Sha512([string] $Path) {
    $sha = [Security.Cryptography.SHA512]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
}

# Dot-sourced by the tests: the functions only
if ($MyInvocation.InvocationName -eq '.') { return }

$instance = if ($env:LITEDEPLOY_INSTANCE) { $env:LITEDEPLOY_INSTANCE } else { 'PSGalleryModule' }
$work = Join-Path $env:windir "LiteDeploy\work\PSGalleryModule\$instance"
$package = $null
try {
    $name = Get-Param 'Name'
    if (-not $name) { $name = $instance }
    if (-not (Test-ModuleName $name)) { throw "Invalid module name: $name" }
    $publisher = Get-Param 'Publisher'
    if (@(Get-PublisherNames $publisher).Count -eq 0) { throw 'Parameter Publisher missing: the signer of the module must be named' }
    $pinned = Get-Param 'Version'
    if ($pinned -and -not (ConvertTo-ModuleVersion $pinned)) { throw "Invalid Version (stable x.y[.z[.w]] expected): $pinned" }
    $download = Get-YesNo 'Download' $true
    $root = Get-ModulesRoot (Get-Param 'ModulesFolder') $env:ProgramFiles
    $cacheDir = if ($env:LITEDEPLOY_CACHE) { Join-Path $env:LITEDEPLOY_CACHE "PSGallery\$name" } else { $null }

    $release = $null
    if ($download) {
        $release = Get-GalleryRelease $name $pinned
        $target = $release.Version
    }
    elseif ($pinned) { $target = $pinned }
    else {
        if (-not $cacheDir) { throw 'Download is no and no shared installer cache is set (App > Options > Software updates)' }
        $target = Get-CacheVersion $cacheDir $name
        if (-not $target) { throw "No $name.<version>.nupkg in the shared cache (<cache>\PSGallery\$name)" }
    }
    $targetVersion = ConvertTo-ModuleVersion $target

    $versions = @(Get-InstalledVersions $root $name)
    $installed = if ($versions.Count -gt 0) { $versions[0] } else { $null }
    $installedText = if ($installed) { $installed.Text } else { $null }
    $outdated = $installed -and ($installed.Version -lt $targetVersion)

    if ($Action -eq 'Check' -or ($installed -and -not $outdated) -or ($Action -eq 'Update' -and -not $installed)) {
        if (-not $installed) { Write-LiteDeployResult 'notinstalled' $null $target "$name not installed in $root" }
        elseif ($outdated) { Write-LiteDeployResult 'outdated' $installedText $target }
        else { Write-LiteDeployResult 'uptodate' $installedText }
        exit 0
    }

    # Update of an outdated module, or Install: package, checks, copy
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $fileName = "$name.$target.nupkg".ToLowerInvariant()
    $package = Join-Path $work $fileName
    $cached = if ($cacheDir) { Join-Path $cacheDir $fileName } else { $null }
    $fromCache = $cached -and (Test-Path -LiteralPath $cached -PathType Leaf)
    if ($fromCache) { Copy-Item -LiteralPath $cached -Destination $package -Force }
    elseif (-not $download) { throw "$fileName is not in the shared cache (<cache>\PSGallery\$name) and Download is no" }
    else {
        if (-not (Test-GalleryUrl $release.Url)) { throw "Unexpected download address for $name $target`: $($release.Url)" }
        if ($release.HashAlgorithm -ne 'SHA512' -or -not $release.Hash) { throw "The Gallery gives no SHA512 for $name $target" }
        Invoke-WebRequest -Uri $release.Url -OutFile $package -UseBasicParsing -TimeoutSec 600
    }
    # The hash of the Gallery, whenever it is known (also for a copy of the cache)
    if ($release -and $release.HashAlgorithm -eq 'SHA512' -and $release.Hash -and (Get-Sha512 $package) -ne $release.Hash) {
        throw "Package refused ($fileName): its SHA512 is not the one of the PowerShell Gallery"
    }

    $staging = Join-Path $work 'module'
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Expand-Nupkg $package $staging
    $psd1 = Join-Path $staging "$name.psd1"
    if (-not (Test-Path -LiteralPath $psd1 -PathType Leaf)) { throw "Package refused ($fileName): no $name.psd1 at its root" }
    $refusal = Get-ModuleRefusal $staging $name $publisher
    if ($refusal) { throw "Module refused ($name $target): $refusal" }
    $manifest = Import-PowerShellDataFile -LiteralPath $psd1
    $moduleVersion = [string]$manifest.ModuleVersion
    if ((ConvertTo-ModuleVersion $moduleVersion) -ne $targetVersion) { throw "Package refused ($fileName): ModuleVersion $moduleVersion instead of $target" }
    if ($manifest.PrivateData -and $manifest.PrivateData.PSData -and $manifest.PrivateData.PSData.Prerelease) { throw "Package refused ($fileName): preview version" }
    $missing = @(Get-RequiredModules $manifest | Where-Object { -not (Test-MachineModule $_.Name $_.Version) } | ForEach-Object { ($_.Name + ' ' + $_.Version).Trim() })
    if ($missing.Count -gt 0) { throw "$name $target needs $($missing -join ', '): add an instance for each first" }

    # Downloaded and verified: a copy for the other computers (best effort, one writer at a time)
    if (-not $fromCache -and $cacheDir) {
        $lock = $null
        try {
            New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
            $lock = [IO.File]::Open((Join-Path $cacheDir '.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
            if (-not (Test-Path -LiteralPath $cached)) {
                Copy-Item -LiteralPath $package -Destination "$cached.partial" -Force
                Move-Item -LiteralPath "$cached.partial" -Destination $cached -Force
            }
        } catch {
            # Read-only cache or another computer writing: the cache stays as it is
        } finally {
            if ($lock) { $lock.Close() }
        }
    }

    $dest = Join-Path (Join-Path $root $name) $moduleVersion
    $partial = "$dest.partial"
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath $staging -Destination $partial -Recurse -Force
    Move-Item -LiteralPath $partial -Destination $dest

    $now = @(Get-InstalledVersions $root $name)
    if ($now.Count -eq 0 -or $now[0].Version -lt $targetVersion) {
        Write-LiteDeployResult 'error' $installedText $target "$name $target copied to $dest but not found as installed"
        exit 1
    }
    $verb = if ($installed) { 'Updated' } else { 'Installed' }
    Write-LiteDeployResult 'uptodate' $now[0].Text $null "$verb $name $($now[0].Text) in $root"
    exit 0
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
finally {
    if ($work -and (Test-Path -LiteralPath $work)) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
