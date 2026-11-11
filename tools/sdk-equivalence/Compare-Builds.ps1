<#
.SYNOPSIS
    Compares two builds of LiteDeploy (former project format vs SDK-style
    project) and fails when they are not equivalent.

.DESCRIPTION
    TEMPORARY (lot 3, .github/workflows/sdk-equivalence.yml): removed before
    the pull request is merged.

    Compares:
      - the C# compiler arguments (from the MSBuild logs), paths normalized;
      - in LiteDeploy.exe (System.Reflection.Metadata, nothing is loaded):
        types, fields, methods with their signature and IL, embedded
        resources (name and content), assembly references, assembly
        attributes, PE type (machine, PE32/PE32+, subsystem, flags, CLR
        flags, metadata version) and the Win32 resources section;
      - the output files (names; content of every file but the exe and pdb).
    Requires PowerShell 7 (pwsh).
#>
param(
    [Parameter(Mandatory = $true)] [string] $OldRoot,
    [Parameter(Mandatory = $true)] [string] $NewRoot,
    [Parameter(Mandatory = $true)] [string] $OldLog,
    [Parameter(Mandatory = $true)] [string] $NewLog,
    [Parameter(Mandatory = $true)] [string] $OutputDir,
    [string] $ReportPath = 'equivalence.md'
)
$ErrorActionPreference = 'Stop'

function Split-CommandLine([string] $line) {
    $tokens = New-Object System.Collections.Generic.List[string]
    $sb = New-Object System.Text.StringBuilder
    $inQuotes = $false
    foreach ($ch in $line.ToCharArray()) {
        if ($ch -eq '"') { $inQuotes = -not $inQuotes; continue }
        if ([char]::IsWhiteSpace($ch) -and -not $inQuotes) {
            if ($sb.Length -gt 0) { $tokens.Add($sb.ToString()); [void]$sb.Clear() }
            continue
        }
        [void]$sb.Append($ch)
    }
    if ($sb.Length -gt 0) { $tokens.Add($sb.ToString()) }
    return $tokens
}

function Get-CompilerArgs([string] $logPath, [string] $root) {
    $line = Get-Content $logPath | Where-Object { $_ -match '(csc\.exe|csc\.dll)"?\s+/noconfig' -and $_ -match 'LiteDeploy\.exe' } | Select-Object -Last 1
    if (-not $line) { throw "No csc command line found in $logPath" }
    $cmd = $line.Substring($line.IndexOf('/noconfig'))
    $rootFull = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    # Options that do not change the compiled assembly, accepted on purpose (listed in the report):
    #  /deterministic+  reproducible builds (MVID and timestamp derived from the inputs)
    #  /analyzerconfig, /sourcelink: editorconfig for analyzers, source link file for the PDB
    #  /checked- /unsafe- /warnaserror- /debug- /warn:4 /sdkpath: compiler defaults written explicitly
    #  /warnaserror+:NU1605: NuGet restore warning
    $accepted = '^/(deterministic\+|analyzerconfig:|sourcelink:|checked-|unsafe-|warnaserror-$|debug-$|warn:4$|sdkpath:|warnaserror\+:NU1605)'
    $result = foreach ($t in (Split-CommandLine $cmd)) {
        if ($t -match $accepted) { $script:acceptedArgs.Add($t); continue }
        # /nowarn: compared as a set (both formats add 1701;1702, the SDK once more)
        if ($t -match '^/nowarn:(.+)$') { '/nowarn:' + ((($Matches[1] -split '[,;]') | Sort-Object -Unique) -join ','); continue }
        $n = $t.Replace($rootFull, '<root>\')
        # References: compared by file name (reference assemblies may come from
        # the installed targeting pack or from the NuGet reference package)
        if ($n -match '^/reference:(.+)$') { $n = '/reference:' + [IO.Path]::GetFileName($Matches[1]) }
        $n
    }
    return @($result | Sort-Object -Unique)
}

function Get-Hex([byte[]] $bytes) {
    if (-not $bytes) { return '' }
    return [Convert]::ToHexString($bytes)
}

function Get-Sha([byte[]] $bytes) {
    if (-not $bytes) { return 'empty' }
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).Substring(0, 16)
}

function Get-TypeRefName($md, $handle) {
    switch ($handle.Kind) {
        'TypeReference' {
            $tr = $md.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$handle)
            return $md.GetString($tr.Namespace) + '.' + $md.GetString($tr.Name)
        }
        'TypeDefinition' {
            $td = $md.GetTypeDefinition([System.Reflection.Metadata.TypeDefinitionHandle]$handle)
            return $md.GetString($td.Namespace) + '.' + $md.GetString($td.Name)
        }
        default { return $handle.Kind.ToString() }
    }
}

function Get-AssemblyFacts([string] $path) {
    $facts = New-Object System.Collections.Generic.List[string]
    $fileBytes = [IO.File]::ReadAllBytes($path)
    $stream = [IO.File]::OpenRead($path)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $h = $pe.PEHeaders

        $facts.Add("pe: machine=$($h.CoffHeader.Machine) magic=$($h.PEHeader.Magic) subsystem=$($h.PEHeader.Subsystem) characteristics=$($h.CoffHeader.Characteristics) dll=$($h.PEHeader.DllCharacteristics) cor=$($h.CorHeader.Flags) metadata=$($md.MetadataVersion)")

        $asm = $md.GetAssemblyDefinition()
        $facts.Add("assembly: $($md.GetString($asm.Name)) $($asm.Version) culture=$($md.GetString($asm.Culture)) flags=$($asm.Flags)")
        foreach ($ca in $asm.GetCustomAttributes()) {
            $a = $md.GetCustomAttribute($ca)
            $ctor = $a.Constructor
            $type = if ($ctor.Kind -eq 'MemberReference') {
                Get-TypeRefName $md ($md.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$ctor).Parent)
            } else {
                Get-TypeRefName $md ($md.GetMethodDefinition([System.Reflection.Metadata.MethodDefinitionHandle]$ctor).GetDeclaringType())
            }
            $facts.Add("attribute: $type $(Get-Hex $md.GetBlobBytes($a.Value))")
        }

        foreach ($r in $md.AssemblyReferences) {
            $ar = $md.GetAssemblyReference($r)
            $facts.Add("reference: $($md.GetString($ar.Name)) $($ar.Version)")
        }

        foreach ($t in $md.TypeDefinitions) {
            $td = $md.GetTypeDefinition($t)
            $tn = $md.GetString($td.Namespace) + '.' + $md.GetString($td.Name)
            $facts.Add("type: $tn attributes=$($td.Attributes)")
            foreach ($f in $td.GetFields()) {
                $fd = $md.GetFieldDefinition($f)
                $facts.Add("field: $tn::$($md.GetString($fd.Name)) $($fd.Attributes) $(Get-Hex $md.GetBlobBytes($fd.Signature))")
            }
            foreach ($m in $td.GetMethods()) {
                $mdef = $md.GetMethodDefinition($m)
                $il = ''
                if ($mdef.RelativeVirtualAddress -ne 0) {
                    $body = [System.Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe, $mdef.RelativeVirtualAddress)   # extension method
                    $il = Get-Sha ([byte[]]$body.GetILBytes())
                }
                $facts.Add("method: $tn::$($md.GetString($mdef.Name)) $($mdef.Attributes) sig=$(Get-Hex $md.GetBlobBytes($mdef.Signature)) il=$il")
            }
        }

        # Embedded resources: read from the file, at the file offset of the CLR resources directory
        $resDir = $h.CorHeader.ResourcesDirectory
        $resStart = 0
        if ($resDir.Size -gt 0 -and -not $h.TryGetDirectoryOffset($resDir, [ref]$resStart)) { throw "resources directory not found in $path" }
        foreach ($r in $md.ManifestResources) {
            $res = $md.GetManifestResource($r)
            $off = $resStart + [int]$res.Offset
            $len = [BitConverter]::ToInt32($fileBytes, $off)
            $data = New-Object byte[] $len
            [Array]::Copy($fileBytes, $off + 4, $data, 0, $len)
            $facts.Add("resource: $($md.GetString($res.Name)) $($res.Attributes) length=$len sha=$(Get-Sha $data)")
        }

        foreach ($s in $h.SectionHeaders) {
            if ($s.Name -eq '.rsrc') {
                $size = [Math]::Min($s.VirtualSize, $s.SizeOfRawData)
                $data = New-Object byte[] $size
                [Array]::Copy($fileBytes, $s.PointerToRawData, $data, 0, $size)
                $facts.Add("win32 resources: length=$($s.VirtualSize) sha=$(Get-Sha $data)")
            }
        }
    }
    finally {
        $pe.Dispose()
        $stream.Dispose()
    }
    return $facts
}

# Text output files that differ: show their content (both sides)
function Show-TextDiff([string] $oldDir, [string] $newDir) {
    foreach ($f in Get-ChildItem $oldDir -File | Where-Object { $_.Extension -in '.config', '.xml', '.msc', '.json' }) {
        $other = Join-Path $newDir $f.Name
        if (-not (Test-Path $other)) { continue }
        if ((Get-FileHash $f.FullName).Hash -eq (Get-FileHash $other).Hash) { continue }
        [void]$report.AppendLine("#### $($f.Name)")
        [void]$report.AppendLine('```diff')
        foreach ($d in (Compare-Object (Get-Content $f.FullName) (Get-Content $other))) {
            [void]$report.AppendLine("$(if ($d.SideIndicator -eq '<=') { '-' } else { '+' }) $($d.InputObject)")
        }
        $oldBytes = [IO.File]::ReadAllBytes($f.FullName); $newBytes = [IO.File]::ReadAllBytes($other)
        [void]$report.AppendLine("(bytes: old $($oldBytes.Length), new $($newBytes.Length); BOM old $($oldBytes[0] -eq 0xEF), new $($newBytes[0] -eq 0xEF))")
        [void]$report.AppendLine('```')
    }
}

function Get-OutputFacts([string] $dir) {
    Get-ChildItem $dir -File -Recurse | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring((Resolve-Path $dir).Path.Length).TrimStart('\')
        if ($_.Extension -in '.exe', '.pdb' -and $_.BaseName -eq 'LiteDeploy') { "file: $rel" }
        else { "file: $rel sha=$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16))" }
    }
}

$report = New-Object System.Text.StringBuilder
$failed = $false
$script:acceptedArgs = New-Object System.Collections.Generic.List[string]

function Compare-Facts([string] $title, $old, $new) {
    $diff = Compare-Object -ReferenceObject @($old) -DifferenceObject @($new)
    [void]$report.AppendLine("### $title")
    [void]$report.AppendLine()
    if (-not $diff) {
        [void]$report.AppendLine("Identical ($(@($old).Count) items).")
    } else {
        $script:failed = $true
        [void]$report.AppendLine('```')
        foreach ($d in $diff) {
            $side = if ($d.SideIndicator -eq '<=') { '- old' } else { '+ new' }
            [void]$report.AppendLine("$side  $($d.InputObject)")
        }
        [void]$report.AppendLine('```')
    }
    [void]$report.AppendLine()
}

[void]$report.AppendLine("## Former project format vs SDK-style project ($OutputDir)")
[void]$report.AppendLine()

Compare-Facts 'Compiler arguments' (Get-CompilerArgs $OldLog $OldRoot) (Get-CompilerArgs $NewLog $NewRoot)
Compare-Facts 'LiteDeploy.exe: types, IL, resources, references, attributes, PE type' `
    (Get-AssemblyFacts (Join-Path $OldRoot "$OutputDir\LiteDeploy.exe")) `
    (Get-AssemblyFacts (Join-Path $NewRoot "$OutputDir\LiteDeploy.exe"))
Compare-Facts 'Output files' (Get-OutputFacts (Join-Path $OldRoot $OutputDir)) (Get-OutputFacts (Join-Path $NewRoot $OutputDir))
Show-TextDiff (Join-Path $OldRoot $OutputDir) (Join-Path $NewRoot $OutputDir)
if ($script:acceptedArgs.Count -gt 0) {
    [void]$report.AppendLine("### Accepted compiler options (no effect on the assembly, see Compare-Builds.ps1)")
    [void]$report.AppendLine()
    [void]$report.AppendLine((($script:acceptedArgs | Sort-Object -Unique | ForEach-Object { $_ -replace '^/(analyzerconfig|sourcelink|sdkpath):.*', '/$1:...' }) | Sort-Object -Unique) -join ' ')
    [void]$report.AppendLine()
}

$text = $report.ToString()
Set-Content -Path $ReportPath -Value $text -Encoding utf8
Write-Host $text
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $text }
if ($failed) { Write-Host "::error::The SDK-style build differs from the former project build ($OutputDir)"; exit 1 }
