<#
.SYNOPSIS
    Authenticode-signs the LiteDeploy binaries of a folder and verifies them.

.DESCRIPTION
    Signs, in -Dir, the files produced by the solution (LiteDeploy.exe, the
    LiteSuite.*.dll libraries and every PowerShell script shipped in the
    package) with the
    newest signtool.exe found in the Windows SDKs, SHA-256 file digest and an
    RFC 3161 timestamp, then checks each signature with "signtool verify /pa".
    Third-party binaries (PsExec.exe, RDPWInst.exe...) are never signed.

    The certificate password is read from an environment variable: it is
    never passed on the command line of the caller nor written to the log.
    Used by .github/workflows/build-and-release.yml.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Dir,
    [Parameter(Mandatory = $true)] [string] $CertPath,
    [string]   $PasswordEnvVar = 'CERTIFICATE_PASSWORD',
    [string]   $TimestampUrl = 'http://timestamp.digicert.com',
    # Paths relative to -Dir; default: LiteDeploy.exe, LiteSuite.*.dll and every *.ps1 below -Dir
    [string[]] $Names
)
$ErrorActionPreference = 'Stop'

function Find-SignTool {
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits", "$env:ProgramFiles\Windows Kits") | Where-Object { $_ -and (Test-Path $_) }
    $candidates = foreach ($root in $roots) {
        Get-ChildItem -Path $root -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' }
    }
    # Newest SDK first: the version is the folder name under bin (10.0.x.y)
    $best = $candidates | Sort-Object {
        $m = [regex]::Match($_.FullName, '\\(\d+\.\d+\.\d+\.\d+)\\')
        if ($m.Success) { [version]$m.Groups[1].Value } else { [version]'0.0' }
    } -Descending | Select-Object -First 1
    if (-not $best) {
        $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($onPath) { return $onPath.Source }
        throw "signtool.exe not found (Windows SDK)"
    }
    return $best.FullName
}

$signtool = Find-SignTool
Write-Host "Using signtool: $signtool"

$password = [Environment]::GetEnvironmentVariable($PasswordEnvVar)

if (-not $Names) {
    $root = (Resolve-Path $Dir).Path
    $Names = @('LiteDeploy.exe') +
        @(Get-ChildItem -Path $root -File -Filter 'LiteSuite.*.dll' | ForEach-Object Name) +
        @(Get-ChildItem -Path $root -Recurse -File -Filter *.ps1 | ForEach-Object { $_.FullName.Substring($root.Length).TrimStart('\') })
}
$files = @($Names | ForEach-Object { Join-Path $Dir $_ } | Where-Object { Test-Path $_ })
if (-not ($files | Where-Object { $_ -like '*LiteDeploy.exe' })) { throw "LiteDeploy.exe not found in $Dir" }

foreach ($file in $files) {
    Write-Host "Signing: $file"
    $signArgs = @('sign', '/f', $CertPath)
    if ($password) { $signArgs += @('/p', $password) }
    $signArgs += @('/tr', $TimestampUrl, '/td', 'sha256', '/fd', 'sha256', $file)
    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) { throw "Failed to sign $file" }

    & $signtool verify /pa $file
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed for $file" }
}
Write-Host "Signed and verified $($files.Count) file(s)."
