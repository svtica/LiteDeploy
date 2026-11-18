# LiteDeploy-Module
# Label: Microsoft Teams (provisioned for all users)
# Processes: ms-teams
<#
.SYNOPSIS
    LiteDeploy update module: new Microsoft Teams provisioned for all users
    with Microsoft's teamsbootstrapper.exe.

.DESCRIPTION
    Check: version of the provisioned package MSTeams
    (Get-AppxProvisionedPackage -Online). When the shared installer cache
    (LITEDEPLOY_CACHE) holds an offline package <cache>\Teams\MSTeams-<arch>.msix
    (arch: x64, x86 or arm64), its version is read from the AppxManifest.xml of
    the package (only that entry is read, nothing is extracted) and compared:
    older provisioned version -> outdated, else uptodate. Without an offline
    package there is nothing to compare with (Teams updates itself):
    uptodate. No provisioned package -> notinstalled.

    Update: teamsbootstrapper.exe from <cache>\Teams when a copy is there, else
    downloaded over HTTPS from Microsoft's documented link, to
    %windir%\LiteDeploy\work (never C:\Windows\Temp). It is refused unless it
    has a valid Authenticode signature of Microsoft Corporation. With an offline
    package, the package is copied next to it, its signature checked the same
    way, and the bootstrapper runs "-p -o <msix>"; else "-p" (it downloads the
    latest package itself). The JSON printed by the bootstrapper must say
    success. The processes of the header are closed by LiteDeploy before
    Update. See docs/modules.md for the contract and the cache layout.

.PARAMETER Action
    Check or Update.

.EXAMPLE
    powershell.exe -NoProfile -File TeamsBootstrapper.ps1 -Action Check
    Reports the provisioned Teams version (run as SYSTEM by
    LiteDeploy.Worker.exe, with LITEDEPLOY_CACHE set when a cache is configured).
#>
param(
    [ValidateSet('Check', 'Update')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'
# Download link of teamsbootstrapper.exe given by Microsoft ("Bulk deploy the Microsoft Teams client")
$bootstrapperUrl = 'https://go.microsoft.com/fwlink/?linkid=2243204&clcid=0x409'
$signerPattern = 'O=Microsoft Corporation(,|$)'

function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

# Version of the provisioned MSTeams package ($null: not provisioned)
function Get-ProvisionedVersion {
    $packages = @(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq 'MSTeams' })
    if ($packages.Count -eq 0) { return $null }
    return ($packages | ForEach-Object { [version]$_.Version } | Sort-Object -Descending | Select-Object -First 1).ToString()
}

# x64, x86 or arm64: architecture of Windows, even from a 32-bit PowerShell
function Get-OsArchitecture {
    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    switch ($arch) {
        'AMD64' { return 'x64' }
        'ARM64' { return 'arm64' }
        default { return 'x86' }
    }
}

function Get-CacheFolder {
    if ($env:LITEDEPLOY_CACHE) { return (Join-Path $env:LITEDEPLOY_CACHE 'Teams') }
    return $null
}

# Offline package of the cache for this architecture ($null: none)
function Get-CachedMsix {
    $folder = Get-CacheFolder
    if (-not $folder) { return $null }
    $msix = Join-Path $folder "MSTeams-$(Get-OsArchitecture).msix"
    if (Test-Path -LiteralPath $msix -PathType Leaf) { return $msix }
    return $null
}

# Version of an MSIX (or MSIX bundle), read from its manifest without extracting the package
function Get-MsixVersion([string] $Path) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { $entry = $zip.GetEntry('AppxMetadata/AppxBundleManifest.xml') }
        if (-not $entry) { throw "No manifest in $Path" }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $version = [string]$manifest.DocumentElement.Identity.Version
        if (-not $version) { throw "No version in the manifest of $Path" }
        return $version
    }
    finally { $zip.Dispose() }
}

function Test-MicrosoftSignature([string] $Path) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    return ($sig.Status -eq 'Valid' -and $sig.SignerCertificate.Subject -match $signerPattern)
}

# teamsbootstrapper.exe in the work folder, from the cache when its copy is signed by Microsoft,
# else downloaded; throws when the file is not signed by Microsoft
function Get-Bootstrapper([string] $Work) {
    $local = Join-Path $Work 'teamsbootstrapper.exe'
    Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
    $folder = Get-CacheFolder
    if ($folder) {
        $cached = Join-Path $folder 'teamsbootstrapper.exe'
        if (Test-Path -LiteralPath $cached -PathType Leaf) {
            Copy-Item -LiteralPath $cached -Destination $local -Force
            if (Test-MicrosoftSignature $local) { return $local }
            Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
        }
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $bootstrapperUrl -OutFile $local -UseBasicParsing
    if (-not (Test-MicrosoftSignature $local)) {
        Remove-Item -LiteralPath $local -Force -ErrorAction SilentlyContinue
        throw 'Bootstrapper refused: not signed by Microsoft Corporation'
    }
    return $local
}

# Runs the bootstrapper (13 minutes at most, the worker allows 15); returns its exit code and output
function Invoke-Bootstrapper([string] $Exe, [string] $Arguments, [string] $Work) {
    $out = Join-Path $Work 'teamsbootstrapper.out.txt'
    $err = Join-Path $Work 'teamsbootstrapper.err.txt'
    $proc = Start-Process -FilePath $Exe -ArgumentList $Arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError $err
    $null = $proc.Handle
    if (-not $proc.WaitForExit(13 * 60 * 1000)) {
        try { $proc.Kill() } catch { }
        throw 'teamsbootstrapper.exe did not finish within 13 minutes'
    }
    $text = ((Get-Content -LiteralPath $out, $err -Raw -ErrorAction SilentlyContinue) -join "`n").Trim()
    Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ ExitCode = $proc.ExitCode; Output = $text }
}

# The bootstrapper prints a JSON object such as {"success": true} or {"success": false, "errorCode": "0x..."}
function Get-BootstrapperResult([string] $Output) {
    $start = $Output.IndexOf('{')
    $end = $Output.LastIndexOf('}')
    if ($start -lt 0 -or $end -le $start) { return $null }
    try { return ($Output.Substring($start, $end - $start + 1) | ConvertFrom-Json) } catch { return $null }
}

$work = Join-Path $env:windir 'LiteDeploy\work'
$copies = @()
try {
    $installed = Get-ProvisionedVersion
    $msix = Get-CachedMsix
    $available = if ($msix) { Get-MsixVersion $msix } else { $null }

    if ($Action -eq 'Check') {
        if (-not $installed) { Write-LiteDeployResult 'notinstalled' $null $available; exit 0 }
        if ($available -and [version]$installed -lt [version]$available) { Write-LiteDeployResult 'outdated' $installed $available; exit 0 }
        $note = if ($available) { $null } else { 'No offline package in the cache to compare with (Teams updates itself)' }
        Write-LiteDeployResult 'uptodate' $installed $null $note
        exit 0
    }

    if ($installed -and $available -and [version]$installed -ge [version]$available) {
        Write-LiteDeployResult 'uptodate' $installed
        exit 0
    }

    # Work folder writable by SYSTEM and administrators only (never C:\Windows\Temp)
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $exe = Get-Bootstrapper $work
    $copies += $exe
    $arguments = '-p'
    if ($msix) {
        $localMsix = Join-Path $work (Split-Path -Leaf $msix)
        Copy-Item -LiteralPath $msix -Destination $localMsix -Force
        $copies += $localMsix
        if (-not (Test-MicrosoftSignature $localMsix)) { throw "Offline package refused: not signed by Microsoft Corporation ($(Split-Path -Leaf $msix))" }
        $arguments = "-p -o `"$localMsix`""
    }

    $run = Invoke-Bootstrapper $exe $arguments $work
    $result = Get-BootstrapperResult $run.Output
    $now = Get-ProvisionedVersion
    if ($result -and $result.success -eq $true -and $run.ExitCode -eq 0 -and $now) {
        Write-LiteDeployResult 'uptodate' $now $null "Provisioned $now"
        exit 0
    }
    $detail = if ($result -and $result.errorCode) { "error code $($result.errorCode)" } else { "exit code $($run.ExitCode)" }
    $text = $run.Output -replace '\s+', ' '
    if ($text.Length -gt 300) { $text = $text.Substring(0, 300) + '...' }
    Write-LiteDeployResult 'error' $now $available "teamsbootstrapper.exe failed, $($detail): $text"
    exit 1
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
finally {
    foreach ($file in $copies) { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
}
