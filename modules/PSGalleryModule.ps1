# LiteDeploy-Module
# Label: PowerShell module of the PowerShell Gallery (all users)
# Processes:
# Install: yes
# Instances: yes
# Parameter: Name default= Name of the module in the PowerShell Gallery (empty: the instance name)
# Parameter: Publisher Signer of the module files: organisation (O=) of the certificate, or its common name (CN=) when it has no O= (several: separated by ;)
# Parameter: Version default= Version to keep installed (empty: the newest .nupkg of the module in the cache folder PSGallery, else the latest stable version of the Gallery)
<#
.SYNOPSIS
    LiteDeploy update module: a PowerShell module of the PowerShell Gallery
    installed for all users, one module per instance, refused unless signed
    by the expected publisher.

.DESCRIPTION
    Generic: the module comes from the parameters of the instance
    ("module:PSGalleryModule/<Instance>" in the package list, values in
    Parameters... or imported from a catalog, see docs/modules.md). Nothing
    in this script is specific to an organisation. PowerShellGet is not used
    (no NuGet provider to bootstrap as SYSTEM, no prompt).

    Check: the versions of the module installed for all users, in
    %ProgramFiles%\WindowsPowerShell\Modules\<Name> and
    %ProgramFiles%\PowerShell\Modules\<Name> (a <version> folder holding
    <Name>.psd1, or <Name>.psd1 directly, read with Import-PowerShellDataFile).
    The copies of Windows (System32) and of the user profiles are not looked
    at and never touched. Target: the Version parameter; else the newest
    <Name>.<version>.nupkg of the folder PSGallery of the shared installer
    cache (LITEDEPLOY_CACHE), when it holds one; else the latest stable
    (not prerelease) version of the Gallery (OData API v2). No version
    installed -> notinstalled; highest one older -> outdated; else uptodate.

    Update (outdated only) and Install (notinstalled only): the .nupkg comes
    from the cache, else from the Gallery (its SHA512 must be the one of the
    Gallery), to %windir%\LiteDeploy\work\PSGalleryModule\<Instance> (never
    C:\Windows\Temp), and is extracted there (an entry whose path leaves the
    folder is refused). The package is refused unless:
      - <Name>.psd1 is at its root, with ModuleVersion = the target;
      - its files are signed by Publisher: either a catalog <Name>.cat with a
        valid signature of Publisher that Test-FileCatalog finds Valid (every
        file listed and unchanged), or a valid signature of Publisher on
        every .ps1, .psm1, .psd1, .ps1xml, .cdxml, .dll and .exe file;
      - every module of RequiredModules is already installed (add it as an
        instance of its own first).
    It is then copied to <root>\<Name>\<version>, root being
    %ProgramFiles%\PowerShell\Modules for a module for PowerShell 7 only
    (CompatiblePSEditions without Desktop), else
    %ProgramFiles%\WindowsPowerShell\Modules (read by Windows PowerShell and
    PowerShell 7). Older versions are kept (PowerShell loads the newest). The
    module checks again. No process is closed: a session that already loaded
    the module keeps the old version until it starts again.

.PARAMETER Action
    Check, Update or Install.

.EXAMPLE
    powershell.exe -NoProfile -File PSGalleryModule.ps1 -Action Check
    Reports the installed and target versions of the module of the instance
    (run as SYSTEM by LiteDeploy.Worker.exe, with LITEDEPLOY_CACHE,
    LITEDEPLOY_INSTANCE and the LITEDEPLOY_PARAM_ variables set).
#>
param(
    [ValidateSet('Check', 'Update', 'Install')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
$galleryApi = 'https://www.powershellgallery.com/api/v2'
$sep = [IO.Path]::DirectorySeparatorChar
# Files that hold code: each needs a valid signature of Publisher when there is no catalog
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

# Module name: letters, digits, . - _ (also what goes in the URL of the Gallery)
function Test-ModuleName([string] $Name) {
    return ([string]$Name -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') -and -not $Name.EndsWith('.')
}

# "2.2.1.5" -> [version]; $null for a prerelease ("1.0.0-beta1") or no version
function ConvertTo-ModuleVersion([string] $Text) {
    $t = ([string]$Text).Trim()
    if ($t -notmatch '^\d+(\.\d+){1,3}$') { return $null }
    try { return [version]$t } catch { return $null }
}

# Organisations or names of the Publisher parameter, separated by ; (quotes keep a ;)
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
        $name = $current.ToString().TrimEnd(';').Trim().Trim('"').Trim()
        if ($name -and -not $names.Contains($name)) { $names.Add($name) }
    }
    return $names.ToArray()
}

# Value of an attribute (O, CN) of a certificate subject, without its quotes ('' when absent)
function Get-SubjectAttribute([string] $Subject, [string] $Attribute) {
    $m = [regex]::Match([string]$Subject, '(?:^|,)\s*' + $Attribute + '=(?:"(?<q>(?:[^"]|"")*)"|(?<v>[^,]*))')
    if (-not $m.Success) { return '' }
    if ($m.Groups['q'].Success) { return $m.Groups['q'].Value.Replace('""', '"').Trim() }
    return $m.Groups['v'].Value.Trim()
}

# The signer of a subject: its organisation (O=), else its common name (CN=)
function Get-SubjectSigner([string] $Subject) {
    $o = Get-SubjectAttribute $Subject 'O'
    if ($o) { return $o }
    return (Get-SubjectAttribute $Subject 'CN')
}

# Whether the signer of a subject is one of the names of Publisher (exact, case-sensitive)
function Test-SubjectPublisher([string] $Subject, [string] $Publisher) {
    $signer = Get-SubjectSigner $Subject
    if (-not $signer) { return $false }
    foreach ($name in @(Get-PublisherNames $Publisher)) { if ($signer -ceq $name) { return $true } }
    return $false
}

# '' when the file has a valid signature of Publisher, else why it is refused
function Get-SignatureRefusal([string] $Path, [string] $Publisher) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -ne 'Valid' -or -not $sig.SignerCertificate) { return "no valid signature ($($sig.Status))" }
    if (-not (Test-SubjectPublisher $sig.SignerCertificate.Subject $Publisher)) {
        return "signed by $(Get-SubjectSigner $sig.SignerCertificate.Subject) instead of $(@(Get-PublisherNames $Publisher) -join '; ')"
    }
    return ''
}

# Path of a .nupkg entry relative to the module folder: $null for the NuGet files (_rels, package,
# [Content_Types].xml, the .nuspec of the root) and folders; throws for a path that leaves the folder
function Get-PackageEntryPath([string] $EntryName) {
    $name = [uri]::UnescapeDataString(([string]$EntryName).Replace('\', '/'))
    if (-not $name -or $name.EndsWith('/')) { return $null }
    if ($name.StartsWith('/') -or $name.Contains(':') -or (($name.Split('/')) -contains '..')) { throw "Package entry refused (path outside the module): $EntryName" }
    $first = $name.Split('/')[0]
    if ($first -eq '_rels' -or $first -eq 'package' -or $name -eq '[Content_Types].xml') { return $null }
    if (-not $name.Contains('/') -and $name.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)) { return $null }
    return $name.Replace('/', [string]$sep)
}

# Entries of the feed of the Gallery (Atom XML of the OData API v2): Id, Version, IsPrerelease, Hash, HashAlgorithm
function ConvertFrom-GalleryFeed([string] $Xml) {
    $doc = New-Object System.Xml.XmlDocument
    $doc.XmlResolver = $null
    $doc.LoadXml($Xml)
    $ns = New-Object System.Xml.XmlNamespaceManager $doc.NameTable
    $ns.AddNamespace('a', 'http://www.w3.org/2005/Atom')
    $ns.AddNamespace('m', 'http://schemas.microsoft.com/ado/2007/08/dataservices/metadata')
    $ns.AddNamespace('d', 'http://schemas.microsoft.com/ado/2007/08/dataservices')
    $nodes = $doc.SelectNodes('//a:entry/m:properties', $ns)
    if ($doc.DocumentElement.LocalName -eq 'entry') { $nodes = $doc.SelectNodes('/a:entry/m:properties', $ns) }
    foreach ($p in $nodes) {
        $text = { param($n) $x = $p.SelectSingleNode("d:$n", $ns); if ($x) { $x.InnerText } else { '' } }
        [pscustomobject]@{
            Id            = & $text 'Id'
            Version       = & $text 'Version'
            IsPrerelease  = ((& $text 'IsPrerelease') -eq 'true')
            Hash          = & $text 'PackageHash'
            HashAlgorithm = & $text 'PackageHashAlgorithm'
        }
    }
}

# Link to the next page of a feed ('' when none)
function Get-FeedNextLink([string] $Xml) {
    $m = [regex]::Match($Xml, '<link\s+rel="next"\s+href="(?<u>[^"]+)"')
    if ($m.Success) { return [Net.WebUtility]::HtmlDecode($m.Groups['u'].Value) }
    return ''
}

# The stable entry to install: Version when given, else the highest stable version; $null when none
function Select-GalleryEntry($Entries, [version] $Pinned) {
    $stable = @($Entries | Where-Object { -not $_.IsPrerelease -and (ConvertTo-ModuleVersion $_.Version) })
    if ($Pinned) { $stable = @($stable | Where-Object { (ConvertTo-ModuleVersion $_.Version) -eq $Pinned }) }
    if ($stable.Count -eq 0) { return $null }
    return ($stable | Sort-Object { ConvertTo-ModuleVersion $_.Version } -Descending | Select-Object -First 1)
}

# Among file names of the cache, the .nupkg of the module: Version when given, else the newest
function Select-CacheNupkg([string[]] $Names, [string] $Module, [version] $Pinned) {
    $pattern = '^' + [regex]::Escape($Module) + '\.(\d+(\.\d+){1,3})\.nupkg$'
    $found = @()
    foreach ($n in $Names) {
        $m = [regex]::Match([string]$n, $pattern, 'IgnoreCase')
        if ($m.Success) { $found += [pscustomobject]@{ Name = $n; Version = (ConvertTo-ModuleVersion $m.Groups[1].Value) } }
    }
    if ($Pinned) { $found = @($found | Where-Object { $_.Version -eq $Pinned }) }
    if ($found.Count -eq 0) { return $null }
    return ($found | Sort-Object Version -Descending | Select-Object -First 1)
}

# Folders that hold the modules for all users
function Get-ModuleRoots {
    return @((Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules'), (Join-Path $env:ProgramFiles 'PowerShell\Modules'))
}

# Installed versions of the module (all users): Version, Path
function Get-InstalledVersions([string] $Module) {
    foreach ($root in Get-ModuleRoots) {
        $dir = Join-Path $root $Module
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { continue }
        foreach ($sub in @(Get-ChildItem -LiteralPath $dir -Directory -ErrorAction SilentlyContinue)) {
            $v = ConvertTo-ModuleVersion $sub.Name
            if ($v -and (Test-Path -LiteralPath (Join-Path $sub.FullName "$Module.psd1"))) { [pscustomobject]@{ Version = $v; Path = $sub.FullName } }
        }
        $flat = Join-Path $dir "$Module.psd1"
        if (Test-Path -LiteralPath $flat) {
            try {
                $v = ConvertTo-ModuleVersion ([string](Import-PowerShellDataFile -LiteralPath $flat).ModuleVersion)
                if ($v) { [pscustomobject]@{ Version = $v; Path = $dir } }
            } catch { }
        }
    }
}

function Get-Installed([string] $Module) {
    return (@(Get-InstalledVersions $Module) | Sort-Object Version -Descending | Select-Object -First 1)
}

# Names (and minimum versions) of the RequiredModules of a manifest
function Get-RequiredModules($Manifest) {
    foreach ($r in @($Manifest.RequiredModules)) {
        if (-not $r) { continue }
        if ($r -is [string]) { [pscustomobject]@{ Name = $r; Version = $null }; continue }
        $v = if ($r.RequiredVersion) { $r.RequiredVersion } elseif ($r.ModuleVersion) { $r.ModuleVersion } else { $null }
        [pscustomobject]@{ Name = [string]$r.ModuleName; Version = (ConvertTo-ModuleVersion ([string]$v)) }
    }
}

# '' when the files of the extracted module are signed by Publisher, else why it is refused
function Get-ModuleSignatureRefusal([string] $Folder, [string] $Module, [string] $Publisher) {
    $catalog = Join-Path $Folder "$Module.cat"
    if (Test-Path -LiteralPath $catalog) {
        $refusal = Get-SignatureRefusal $catalog $Publisher
        if ($refusal) { return "catalog $Module.cat: $refusal" }
        $result = Test-FileCatalog -Path $Folder -CatalogFilePath $catalog -Detailed
        if ([string]$result.Status -ne 'Valid') { return "catalog $Module.cat: files do not match ($($result.Status))" }
        return ''
    }
    $files = @(Get-ChildItem -LiteralPath $Folder -Recurse -File | Where-Object { $codeExtensions -contains $_.Extension.ToLowerInvariant() })
    if ($files.Count -eq 0) { return 'no file to check' }
    foreach ($f in $files) {
        $refusal = Get-SignatureRefusal $f.FullName $Publisher
        if ($refusal) { return "$($f.FullName.Substring($Folder.Length).TrimStart($sep)): $refusal" }
    }
    return ''
}

# Extracts the module files of a .nupkg to an empty folder (4 GB, 20 000 entries at most)
function Expand-Nupkg([string] $Package, [string] $Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd($sep) + $sep
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        if ($zip.Entries.Count -gt 20000) { throw 'Package refused: too many entries' }
        [long]$total = 0
        foreach ($entry in $zip.Entries) {
            $rel = Get-PackageEntryPath $entry.FullName
            if (-not $rel) { continue }
            $total += $entry.Length
            if ($total -gt 4GB) { throw 'Package refused: larger than 4 GB' }
            $target = [IO.Path]::GetFullPath((Join-Path $root $rel))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Package entry refused (path outside the module): $($entry.FullName)" }
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    }
    finally { $zip.Dispose() }
}

# Target: Version, Source (full path in the cache, else $null), Url and Hash of the Gallery
function Get-Target([string] $Module) {
    $pinnedText = Get-Param 'Version'
    $pinned = $null
    if ($pinnedText) {
        $pinned = ConvertTo-ModuleVersion $pinnedText
        if (-not $pinned) { throw "Invalid Version parameter (a stable version such as 2.1.0 is expected): $pinnedText" }
    }
    if ($env:LITEDEPLOY_CACHE) {
        $folder = Join-Path $env:LITEDEPLOY_CACHE 'PSGallery'
        if (Test-Path -LiteralPath $folder -PathType Container) {
            $names = @(Get-ChildItem -LiteralPath $folder -Filter '*.nupkg' -File | ForEach-Object { $_.Name })
            $hit = Select-CacheNupkg $names $Module $pinned
            if ($hit) { return [pscustomobject]@{ Version = $hit.Version; Source = (Join-Path $folder $hit.Name); Hash = ''; HashAlgorithm = '' } }
        }
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    $entries = @()
    $url = "$galleryApi/FindPackagesById()?id='$Module'"
    for ($page = 0; $url -and $page -lt 20; $page++) {
        if (-not $url.StartsWith("$galleryApi/", [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected link in the feed of the Gallery: $url" }
        $xml = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 120).Content
        $entries += @(ConvertFrom-GalleryFeed $xml)
        $url = Get-FeedNextLink $xml
    }
    $entries = @($entries | Where-Object { $_.Id -eq $Module })
    if ($entries.Count -eq 0) { throw "Module $Module not found in the PowerShell Gallery" }
    $entry = Select-GalleryEntry $entries $pinned
    if (-not $entry) {
        if ($pinned) { throw "Version $pinned of $Module not found in the PowerShell Gallery" }
        throw "No stable version of $Module in the PowerShell Gallery"
    }
    return [pscustomobject]@{ Version = (ConvertTo-ModuleVersion $entry.Version); Source = $null; Hash = $entry.Hash; HashAlgorithm = $entry.HashAlgorithm }
}

# Dot-sourced (unit tests): the functions only
if ($MyInvocation.InvocationName -eq '.') { return }

$ProgressPreference = 'SilentlyContinue'
$work = $null
try {
    $instance = if ($env:LITEDEPLOY_INSTANCE) { $env:LITEDEPLOY_INSTANCE } else { 'PSGalleryModule' }
    $module = Get-Param 'Name'
    if (-not $module) { $module = $instance }
    if (-not (Test-ModuleName $module)) { throw "Invalid module name: $module" }
    $publisher = Get-Param 'Publisher'
    if (@(Get-PublisherNames $publisher).Count -eq 0) { throw 'Parameter Publisher missing: the signer of the module files must be named' }
    $installed = Get-Installed $module
    $installedText = if ($installed) { [string]$installed.Version } else { $null }

    if ($Action -eq 'Install' -and $installed) {
        Write-LiteDeployResult 'uptodate' $installedText $null "$module already installed ($($installed.Path))"
        exit 0
    }
    if ($Action -ne 'Install' -and -not $installed) {
        Write-LiteDeployResult 'notinstalled' $null $null "$module not installed for all users"
        exit 0
    }

    $target = Get-Target $module
    $outdated = $installed -and ($installed.Version -lt $target.Version)
    if ($Action -eq 'Check' -or ($installed -and -not $outdated)) {
        if ($outdated) { Write-LiteDeployResult 'outdated' $installedText ([string]$target.Version) }
        else { Write-LiteDeployResult 'uptodate' $installedText }
        exit 0
    }

    # Update of an outdated module, or Install: package, hash, extraction, signatures, copy
    $work = Join-Path $env:windir "LiteDeploy\work\PSGalleryModule\$instance"
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $package = Join-Path $work "$module.$($target.Version).nupkg"
    if ($target.Source) {
        Copy-Item -LiteralPath $target.Source -Destination $package -Force
    }
    else {
        Invoke-WebRequest -Uri "$galleryApi/package/$module/$($target.Version)" -OutFile $package -UseBasicParsing -TimeoutSec 600
        if ($target.HashAlgorithm -ne 'SHA512' -or -not $target.Hash) { throw "Package of $module refused: no SHA512 in the Gallery ($($target.HashAlgorithm))" }
        $sha = [Security.Cryptography.SHA512]::Create()
        $stream = [IO.File]::OpenRead($package)
        try { $actual = [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
        if ($actual -ne $target.Hash) { throw "Package of $module refused: its SHA512 is not the one of the Gallery" }
    }

    $staging = Join-Path $work 'module'
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Expand-Nupkg $package $staging
    $manifestPath = Join-Path $staging "$module.psd1"
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Package of $module refused: no $module.psd1 at its root" }
    $refusal = Get-ModuleSignatureRefusal $staging $module $publisher
    if ($refusal) { throw "Package of $module $($target.Version) refused: $refusal" }
    $manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
    $manifestVersion = ConvertTo-ModuleVersion ([string]$manifest.ModuleVersion)
    if ($manifestVersion -ne $target.Version) { throw "Package of $module refused: ModuleVersion is $($manifest.ModuleVersion), $($target.Version) expected" }
    $missing = @()
    foreach ($r in @(Get-RequiredModules $manifest)) {
        $have = Get-Installed $r.Name
        if (-not $have -and -not (Get-Module -ListAvailable -Name $r.Name)) { $missing += $r.Name; continue }
        if ($have -and $r.Version -and $have.Version -lt $r.Version) { $missing += "$($r.Name) $($r.Version)" }
    }
    if ($missing.Count -gt 0) { throw "$module $($target.Version) requires $($missing -join ', '): add each one as an instance of its own and install it first" }

    $editions = @($manifest.CompatiblePSEditions)
    $coreOnly = $editions.Count -gt 0 -and -not ($editions -contains 'Desktop')
    $root = if ($coreOnly) { (Get-ModuleRoots)[1] } else { (Get-ModuleRoots)[0] }
    $destination = Join-Path (Join-Path $root $module) ([string]$target.Version)
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $staging '*') -Destination $destination -Recurse -Force

    $now = Get-Installed $module
    if (-not $now -or $now.Version -lt $target.Version) { throw "$module $($target.Version) was copied to $destination but is not found there" }
    $verb = if ($installed) { 'Updated to' } else { 'Installed' }
    Write-LiteDeployResult 'uptodate' ([string]$now.Version) $null "$verb $module $($now.Version) ($destination)"
    exit 0
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
finally {
    if ($work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
