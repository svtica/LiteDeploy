# LiteDeploy-Module
# Label: Adobe Acrobat Reader / Acrobat
# Processes: Acrobat, AcroRd32, AcroCEF, RdrCEF
<#
.SYNOPSIS
    LiteDeploy update module: Adobe Acrobat Reader and Acrobat (MSI installs)
    patched with Adobe's MSP files, from the MAJ-Logiciels tool of Patrick
    Bedard.

.DESCRIPTION
    Each installed product is recognized by its MSI product code (its display
    name is misleading: "Adobe Acrobat (64-bit)" can be Reader 64-bit). The
    latest build comes from the versions winget knows; the patch of the right
    flavor is downloaded from ardownload2.adobe.com, refused unless it is signed
    by Adobe, and applied with msiexec /p. Outlook loads Adobe add-ins and can
    block the patch: it is not closed by this module (no warning to the user
    yet), the failure then says so. See docs/modules.md for the contract.

.PARAMETER Action
    Check or Update.
#>
param(
    [ValidateSet('Check', 'Update')]
    [string] $Action = 'Check'
)
$ErrorActionPreference = 'Stop'

# Flavors: MSI product code and patch file (patch targets verified against the MSP files)
$flavors = @(
    @{ Name = 'Reader32';     Code = '^\{AC76BA86-7AD7-\d{4}-7B44-AC0F074E4100\}$'; Url = 'https://ardownload2.adobe.com/pub/adobe/reader/win/AcrobatDC/{0}/AcroRdrDCUpd{0}.msp' },
    @{ Name = 'ReaderX64';    Code = '^\{AC76BA86-\d{4}-1033-7760-BC15014EA700\}$'; Url = 'https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcroRdrDCx64Upd{0}.msp' },
    @{ Name = 'ReaderX64MUI'; Code = '^\{AC76BA86-1033-FF00-7760-BC15014EA700\}$'; Url = 'https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcroRdrDCx64Upd{0}_MUI.msp' },
    @{ Name = 'AcrobatX64';   Code = '^\{AC76BA86-1033-FFFF-7760-BC15014EA700\}$'; Url = 'https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcrobatDCx64Upd{0}.msp' },
    @{ Name = 'Acrobat32';    Code = '^\{AC76BA86-1033-FFFF-7760-0C0F074E4100\}$'; Url = 'https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcrobatDCUpd{0}.msp' }
)

function Write-LiteDeployResult([string] $Status, [string] $Installed, [string] $Available, [string] $Message) {
    $fields = @('LDW1', 'type=module', "status=$([uri]::EscapeDataString($Status))")
    if ($Installed) { $fields += "installed=$([uri]::EscapeDataString($Installed))" }
    if ($Available) { $fields += "available=$([uri]::EscapeDataString($Available))" }
    if ($Message)   { $fields += "message=$([uri]::EscapeDataString($Message))" }
    Write-Output ($fields -join "`t")
}

function Get-Winget {
    Resolve-Path "$env:ProgramFiles\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe\winget.exe" -ErrorAction SilentlyContinue |
        Sort-Object Path | Select-Object -Last 1 -ExpandProperty Path
}

# "26.003.22075" -> 2600322075 (build number used in the patch file names)
function ConvertTo-Build([string] $Version) {
    $p = $Version.Split('.')
    if ($p.Count -ne 3) { return $null }
    return [long]('{0}{1}{2}' -f $p[0], $p[1].PadLeft(3, '0'), $p[2].PadLeft(5, '0'))
}

function Test-Url([string] $Url) {
    try {
        $req = [Net.HttpWebRequest]::Create($Url)
        $req.Method = 'HEAD'
        $req.Timeout = 30000
        $resp = $req.GetResponse()
        $ok = ([int]$resp.StatusCode -eq 200)
        $resp.Close()
        return $ok
    } catch { return $false }
}

# Installed Adobe products, by MSI product code (orphan entries without version skipped)
function Get-InstalledAdobe {
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    foreach ($e in (Get-ItemProperty $keys -ErrorAction SilentlyContinue)) {
        foreach ($f in $flavors) {
            if ($e.PSChildName -match $f.Code -and $e.DisplayVersion) {
                [pscustomobject]@{ Flavor = $f; Version = [string]$e.DisplayVersion; Code = $e.PSChildName }
            }
        }
    }
}

# Latest build that has a patch for this flavor, among the 5 newest versions known to winget
function Get-LatestBuild($Flavor) {
    $winget = Get-Winget
    if (-not $winget) { throw 'winget.exe not found' }
    $out = & $winget show --id Adobe.Acrobat.Reader.64-bit -e --source winget --versions --accept-source-agreements --disable-interactivity 2>&1 | Out-String
    $versions = [regex]::Matches($out, '(?m)^\s*(\d+\.\d+\.\d+)\s*$') | ForEach-Object { $_.Groups[1].Value } |
        Sort-Object { [version]$_ } -Descending | Select-Object -First 5
    foreach ($v in $versions) {
        $b = ConvertTo-Build $v
        if ($b -and (Test-Url ($Flavor.Url -f $b))) { return $b }
    }
    return $null
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $installed = @(Get-InstalledAdobe)
    if ($installed.Count -eq 0) { Write-LiteDeployResult 'notinstalled'; exit 0 }

    # One line for the computer: the first product that needs a patch, else the first one
    $plan = foreach ($i in $installed) {
        $latest = Get-LatestBuild $i.Flavor
        [pscustomobject]@{ Item = $i; Latest = $latest; Outdated = ($latest -and (ConvertTo-Build $i.Version) -lt $latest) }
    }
    $versions = ($installed | ForEach-Object { "$($_.Flavor.Name) $($_.Version)" }) -join ', '
    $todo = @($plan | Where-Object { $_.Outdated })
    if (-not ($plan | Where-Object { $_.Latest })) { Write-LiteDeployResult 'error' $versions $null 'No Adobe patch found online'; exit 1 }

    if ($Action -eq 'Check' -or $todo.Count -eq 0) {
        if ($todo.Count -gt 0) { Write-LiteDeployResult 'outdated' $versions (($todo | ForEach-Object { "build $($_.Latest)" }) -join ', ') }
        else { Write-LiteDeployResult 'uptodate' $versions }
        exit 0
    }

    $work = Join-Path $env:windir 'LiteDeploy\work'
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $restart = $false
    foreach ($t in $todo) {
        $url = $t.Item.Flavor.Url -f $t.Latest
        $msp = Join-Path $work (Split-Path $url -Leaf)
        $log = Join-Path $work "adobe-$($t.Item.Flavor.Name).log"
        Invoke-WebRequest -Uri $url -OutFile $msp -UseBasicParsing
        $sig = Get-AuthenticodeSignature $msp
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Adobe') {
            Remove-Item $msp -Force
            Write-LiteDeployResult 'error' $versions $null "Patch refused: signature $($sig.Status)"
            exit 1
        }
        $p = Start-Process msiexec.exe -ArgumentList "/p `"$msp`" /qn /norestart /l*v `"$log`"" -Wait -PassThru
        Remove-Item $msp -Force -ErrorAction SilentlyContinue
        if ($p.ExitCode -eq 3010) { $restart = $true }
        elseif ($p.ExitCode -ne 0) {
            $hint = if (Get-Process OUTLOOK -ErrorAction SilentlyContinue) { ' (Outlook is open and loads Adobe add-ins: retry when it is closed)' } else { '' }
            Write-LiteDeployResult 'error' $versions $null "msiexec $($p.ExitCode) for $($t.Item.Flavor.Name)$hint (log: $log)"
            exit 1
        }
    }

    $now = (@(Get-InstalledAdobe) | ForEach-Object { "$($_.Flavor.Name) $($_.Version)" }) -join ', '
    Write-LiteDeployResult 'uptodate' $now $null 'Patched'
    if ($restart) { exit 3010 }
    exit 0
}
catch {
    Write-LiteDeployResult 'error' $null $null $_.Exception.Message
    exit 1
}
