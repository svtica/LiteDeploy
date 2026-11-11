# Pester 5 tests of MAJ-Logiciels (run by CI under Windows PowerShell 5.1, as an administrator).
# Everything runs on a fake share in a temporary folder with a fictitious site profile; no computer is
# contacted. The launcher test creates C:\ProgramData\MAJ-Logiciels\winget-upgrade (protected folder):
# it only runs when $env:CI is set.
#   Invoke-Pester .\tests -Output Detailed

BeforeAll {
    $script:repo = Join-Path (Split-Path $PSScriptRoot -Parent) 'MAJ-Logiciels'
    $script:t = Join-Path ([IO.Path]::GetTempPath()) ("majtest-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $script:share = Join-Path $t 'share'
    $script:base = Join-Path $t 'base'
    $script:prof = Join-Path $t 'profile'
    New-Item -Path (Join-Path $share 'Winget-upgrade'), $prof -ItemType Directory -Force | Out-Null

    # Fictitious site profile: two winget packages, both optional modules on, custom sender
    @{ DeployRoot = $share; MajBase = $base; DeployAccount = 'TEST\deploy'; OrgName = 'Test' } |
        ConvertTo-Json | Set-Content (Join-Path $prof 'config.json') -Encoding UTF8
    @(
        '# test profile',
        '7zip.7zip | 7zFM, 7zG | 7-Zip | ^7-Zip',
        'Microsoft.DotNet.DesktopRuntime.8 | | .NET 8 | ^Microsoft Windows Desktop Runtime - (8\.[\d\.]+)'
    ) | Set-Content (Join-Path $prof 'packages.txt') -Encoding UTF8
    @{ Modules = @{ AdobeReader = $true; OnePassword = $true }; Messages = @{ Sender = 'Le service test' } } |
        ConvertTo-Json -Depth 3 | Set-Content (Join-Path $prof 'package.json') -Encoding UTF8

    function Invoke-Script([string]$Path, [string[]]$Arguments) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>&1 | Out-String
    }
}

AfterAll {
    Remove-Item $script:t -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'Scripts' {
    $all = @(Get-ChildItem (Join-Path (Split-Path $PSScriptRoot -Parent) 'MAJ-Logiciels') -Recurse -Filter *.ps1 | ForEach-Object { @{ File = $_.FullName; Name = $_.Name } })

    It '<Name> parses without error' -TestCases $all {
        param($File)
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($File, [ref]$null, [ref]$errors)
        @($errors).Count | Should -Be 0
    }

    It '<Name> has a UTF-8 BOM when it is not pure ASCII (Windows PowerShell 5.1 reads it as ANSI otherwise)' -TestCases $all {
        param($File)
        $bytes = [IO.File]::ReadAllBytes($File)
        $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        $nonAscii = @($bytes | Select-Object -Skip $(if ($bom) { 3 } else { 0 }) | Where-Object { $_ -gt 127 }).Count
        ($bom -or $nonAscii -eq 0) | Should -BeTrue
    }

    It '<Name> holds no site-specific path (user profile, real server)' -TestCases $all {
        param($File)
        $text = Get-Content $File -Raw
        $text | Should -Not -Match 'C:\\Users\\'
        $text | Should -Not -Match '\\\\(?!server\\|\$|\$Pc|\$pc|\$Name|\$n\\|\?)[A-Za-z][\w-]+\\[\w$]+\$'
    }
}

Describe 'Installer and site profile' {
    It 'installs the package on the share and the tools in MajBase' {
        Invoke-Script (Join-Path $repo 'Installer.ps1') @('-Profil', $prof, '-MajBase', $base) | Out-Null
        foreach ($f in 'winget-upgrade.ps1', 'winget-upgrade.cmd', 'winget-worker.ps1', 'maj-secure.ps1') {
            Join-Path $share "Winget-upgrade\$f" | Should -Exist
        }
        Join-Path $base 'Outils\outils-maj.ps1' | Should -Exist
        Join-Path $base 'Outils\config.json' | Should -Exist
    }

    It 'applies the profile lists and modules' {
        Get-Content (Join-Path $share 'Winget-upgrade\packages.txt') -Raw | Should -Match 'DesktopRuntime'
        (Get-Content (Join-Path $share 'Winget-upgrade\package.json') -Raw | ConvertFrom-Json).Modules.OnePassword | Should -BeTrue
    }

    It 'keeps the site files on a code update without profile' {
        '# local edit' | Add-Content (Join-Path $share 'Winget-upgrade\packages.txt')
        Invoke-Script (Join-Path $repo 'Installer.ps1') @('-MajBase', $base) | Out-Null
        Get-Content (Join-Path $share 'Winget-upgrade\packages.txt') -Raw | Should -Match 'local edit'
    }

    It 'exports a profile and keeps a live list on import unless -Force' {
        $profil = Join-Path $base 'Outils\profil.ps1'
        Invoke-Script $profil @('-Export', (Join-Path $t 'export')) | Out-Null
        @(Get-ChildItem (Join-Path $t 'export')).Count | Should -BeGreaterOrEqual 3

        $exclude = Join-Path $share 'Winget-upgrade-logs\exclude.txt'
        New-Item -Path (Split-Path $exclude) -ItemType Directory -Force | Out-Null
        'LIVE-ENTRY' | Set-Content $exclude
        'FROM-PROFILE' | Set-Content (Join-Path $prof 'exclude.txt')
        Invoke-Script $profil @('-Import', $prof) | Out-Null
        Get-Content $exclude | Should -Contain 'LIVE-ENTRY'
        Invoke-Script $profil @('-Import', $prof, '-Force') | Out-Null
        Get-Content $exclude | Should -Contain 'FROM-PROFILE'
    }
}

Describe 'Settings (config.ps1)' {
    BeforeAll {
        # Dot-sourced in this scope, like the tools do
        $majConfigOverride = Join-Path $base 'Outils\config.json'
        . (Join-Path $base 'Outils\config.ps1')
    }

    It 'reads the share and defaults to SYSTEM launches' {
        $deployRoot | Should -Be $share
        $launchAs | Should -Be 'System'
        $senderLower | Should -Be 'le service test'
    }

    It 'tracks the winget packages and the enabled modules' {
        $tr = @(Get-TrackedSoftware)
        ($tr.Label -join ',') | Should -Be '7-Zip,.NET 8,Adobe,1Password'
    }

    It 'finds the installed version with the DisplayName regex (group 1 when present)' -TestCases @(
        @{ Name = 'Microsoft Windows Desktop Runtime - 8.0.11 (x64)'; Version = '8.0.11.34221'; Label = '.NET 8'; Expected = '8.0.11' }
        @{ Name = '7-Zip 24.08 (x64)'; Version = '24.08'; Label = '7-Zip'; Expected = '24.08' }
        @{ Name = '1Password'; Version = '8.10.50'; Label = '1Password'; Expected = '8.10.50' }
        @{ Name = 'Microsoft Edge'; Version = '1'; Label = $null; Expected = '1' }
    ) {
        param($Name, $Version, $Label, $Expected)
        $k = $null; $v = $Version
        foreach ($r in @(Get-TrackedSoftware | Where-Object { $_.Detect })) {
            if ($Name -match $r.Detect) { $k = $r.Label; if ($Matches[1]) { $v = [string]$Matches[1] }; break }
        }
        $k | Should -Be $Label
        $v | Should -Be $Expected
    }

    It 'disables the modules with the default package.json' {
        Copy-Item (Join-Path $repo 'Paquet\package.json') (Join-Path $share 'Winget-upgrade\package.json') -Force
        $majConfigOverride = Join-Path $base 'Outils\config.json'
        . (Join-Path $base 'Outils\config.ps1')
        @(Get-TrackedSoftware).Count | Should -Be 2
        @(Get-ModuleMarks).Count | Should -Be 0
    }
}

Describe 'Protected work folder and signatures (maj-secure.ps1)' {
    BeforeAll {
        . (Join-Path $repo 'Paquet\maj-secure.ps1')
        $script:root = Join-Path $t 'secure-root'
        New-Item $root -ItemType Directory -Force | Out-Null
    }

    It 'creates every level with SYSTEM and Administrators only' {
        $r = Initialize-SecureFolder $root 'MAJ\work'
        $r.Path | Should -Be (Join-Path $root 'MAJ\work')
        Test-TrustedFolder (Join-Path $root 'MAJ') | Should -BeTrue
        Test-TrustedFolder $r.Path | Should -BeTrue
        @($r.Messages).Count | Should -Be 0
    }

    It 'moves aside a folder created in advance by someone else' {
        $planted = Join-Path $root 'planted'
        New-Item $planted -ItemType Directory | Out-Null   # inherited rights, not protected
        'evil' | Set-Content (Join-Path $planted 'winget-worker.ps1')
        $r = Initialize-SecureFolder $root 'planted'
        Test-TrustedFolder $r.Path | Should -BeTrue
        Join-Path $r.Path 'winget-worker.ps1' | Should -Not -Exist
        @($r.Messages).Count | Should -Be 1
        @(Get-ChildItem $root -Directory -Filter 'planted.untrusted-*').Count | Should -Be 1
    }

    It 'moves aside a junction instead of following it' {
        $target = Join-Path $t 'elsewhere'
        New-Item $target -ItemType Directory -Force | Out-Null
        cmd.exe /c mklink /J "$(Join-Path $root 'junction')" "$target" | Out-Null
        $r = Initialize-SecureFolder $root 'junction'
        Test-TrustedFolder $r.Path | Should -BeTrue
        (Get-Item $r.Path).Attributes -band [IO.FileAttributes]::ReparsePoint | Should -Be 0
    }

    It 'accepts a file signed by the expected publisher' {
        $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        Assert-Publisher $exe @('O=Microsoft Corporation') | Should -Match 'Microsoft'
    }

    It 'refuses another publisher' {
        $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        { Assert-Publisher $exe @('O=Adobe Inc\.') } | Should -Throw '*unexpected publisher*'
    }

    It 'refuses an unsigned file' {
        $f = Join-Path $t 'unsigned.msi'
        'not an installer' | Set-Content $f
        { Assert-Publisher $f @('.*') } | Should -Throw '*not valid*'
    }

    It 'gives the default publishers, overridable in package.json' {
        ((Get-ExpectedPublishers $null).OnePassword -join '|') | Should -Be 'O=AgileBits Inc\.'
        ((Get-ExpectedPublishers ([pscustomobject]@{ Publishers = [pscustomobject]@{ Adobe = @('O=Test') } })).Adobe -join '|') | Should -Be 'O=Test'
    }
}

Describe 'Launcher on a server (CI only)' -Skip:(-not $env:CI) {
    BeforeAll { . (Join-Path $repo 'Paquet\maj-secure.ps1') }

    It 'secures its work folder, does nothing on a server OS and writes its final line to the share' {
        $out = Invoke-Script (Join-Path $share 'Winget-upgrade\winget-upgrade.ps1') @()
        $out | Should -Match 'Nothing done: server OS'
        Test-TrustedFolder (Join-Path $env:ProgramData 'MAJ-Logiciels\winget-upgrade') | Should -BeTrue
        $log = Get-Content (Join-Path $share "Winget-upgrade-logs\$env:COMPUTERNAME.log") -Raw
        $log | Should -Match 'Launcher: server OS .*nothing done'
    }
}
