param([string]$LogPath = (Join-Path $env:ProgramData "MAJ-Logiciels\winget-upgrade\winget-upgrade.log"))

. (Join-Path $PSScriptRoot "maj-secure.ps1")   # Assert-Publisher, Get-ExpectedPublishers (copied by the launcher)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Message" | Out-File -FilePath $LogPath -Append -Encoding utf8
}

# Known winget return codes (hex) and their meaning
$codeLabels = @{
    '0x00000000' = 'Upgraded'
    '0x8A15002B' = 'No applicable upgrade (already up to date)'
    '0x8A150014' = 'Package not installed'
    '0x8A15000F' = 'winget sources unavailable'
    '0x80190193' = 'Download forbidden (HTTP 403) - check the web filter / proxy'
}
$okCodes = @('0x00000000', '0x8A15002B')

function Get-Hex([int]$Code) { '0x{0:X8}' -f $Code }

function Get-CodeLabel([int]$Code) {
    $hex = Get-Hex $Code
    if ($codeLabels.ContainsKey($hex)) { return "$hex $($codeLabels[$hex])" }
    return "$hex Unknown code"
}

# Drop winget progress bars, spinners and license boilerplate
function Get-CleanOutput([string]$Text) {
    $Text -split "[\r\n]+" |
        ForEach-Object { $_.Trim() } |
        Where-Object {
            $_ -and
            $_ -notmatch '^[-\\|/]+$' -and
            $_ -notmatch '[\u2588\u2592]' -and
            $_ -notmatch '^This application is licensed' -and
            $_ -notmatch '^Microsoft is not responsible'
        }
}

# Force-close processes; returns $false if they are still running
function Close-Processes([string[]]$Names, [string]$Label) {
    $running = @(Get-Process -Name $Names -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return $true }
    $list = ($running.Name | Sort-Object -Unique) -join ', '
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    if (@(Get-Process -Name $Names -ErrorAction SilentlyContinue).Count -gt 0) {
        Write-Log "$Label : could not close ($list), not upgraded"
        return $false
    }
    Write-Log "$Label : closed running process(es): $list"
    $script:closed++
    foreach ($n in ($running.Name | Sort-Object -Unique)) {
        $fn = Get-Friendly $n
        if (-not $script:closedApps.Contains($fn)) { $script:closedApps.Add($fn) }
    }
    return $true
}

$upgraded = 0; $closed = 0; $failed = 0

# What the user will be told about (friendly names)
$updatedNames  = New-Object System.Collections.Generic.List[string]
$closedApps    = New-Object System.Collections.Generic.List[string]
$restartNeeded = $false
# Package settings (copied by the launcher): user messages. Labels of winget packages come from packages.txt.
$pkgCfg = $null
try { $pkgCfg = Get-Content (Join-Path $PSScriptRoot "package.json") -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json } catch { }
$sender = if ($pkgCfg -and $pkgCfg.Messages -and $pkgCfg.Messages.Sender) { [string]$pkgCfg.Messages.Sender } else { "Le service des TI" }
$senderLower = $sender.Substring(0, 1).ToLower() + $sender.Substring(1)
$javaWarning = if ($pkgCfg -and $pkgCfg.Messages -and $pkgCfg.Messages.JavaWarning) { [string]$pkgCfg.Messages.JavaWarning } else { "Les applications Java ouvertes seront fermées dans {0} minutes : enregistrez votre travail." }
$javaAppName = if ($pkgCfg -and $pkgCfg.Messages -and $pkgCfg.Messages.JavaAppName) { [string]$pkgCfg.Messages.JavaAppName } else { "Applications Java" }   # "closed" list in the final message
$publishers  = Get-ExpectedPublishers $pkgCfg
$friendly = @{
    'OUTLOOK'   = 'Outlook'
    'Acrobat'   = 'Adobe Acrobat'; 'AcroRd32' = 'Adobe Acrobat'
    'AcroCEF'   = 'Adobe Acrobat'; 'RdrCEF'   = 'Adobe Acrobat'
    '1Password' = '1Password'; '1Password-BrowserSupport' = '1Password'
}
# packages.txt: Id | processes | label - the label names the package and its processes in user messages
foreach ($line in @(Get-Content (Join-Path $PSScriptRoot "packages.txt") -ErrorAction SilentlyContinue)) {
    $p = @($line.Trim() -split '\|' | ForEach-Object { $_.Trim() })
    if (-not $p[0] -or $p[0].StartsWith('#') -or $p.Count -lt 3 -or -not $p[2]) { continue }
    $friendly[$p[0]] = $p[2]
    foreach ($pr in @($p[1] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) { $friendly[$pr] = $p[2] }
}
function Get-Friendly([string]$Name) { if ($friendly.ContainsKey($Name)) { $friendly[$Name] } else { $Name } }

# Messages to the logged-on user go through msg.exe (no scheduled task needed).
# This file is saved as UTF-8 with BOM so Windows PowerShell 5.1 reads the French text correctly.
# A logged-on user (console or RDP) has an explorer.exe; Win32_ComputerSystem.UserName only sees the console
$userLoggedOn = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'").Count -gt 0
$warnSeconds  = 120

# msg.exe rejects messages longer than 255 characters (tested)
function Send-UserMessage([string]$Text, [int]$Seconds = 0) {
    if (-not $userLoggedOn) { return }
    if ($Text.Length -gt 255) { $Text = $Text.Substring(0, 252) + "..." }
    $msgArgs = @('*')
    if ($Seconds -gt 0) { $msgArgs += "/TIME:$Seconds" }
    $msgArgs += $Text
    & "$env:windir\System32\msg.exe" @msgArgs 2>$null
}

# --- winget packages ---
$winget = Resolve-Path "C:\Program Files\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe\winget.exe" -ErrorAction SilentlyContinue |
    Sort-Object Path | Select-Object -Last 1 -ExpandProperty Path
$listFile = Join-Path $PSScriptRoot "packages.txt"

if (-not $winget) {
    Write-Log "winget.exe not found, winget packages skipped"
    $failed++
} elseif (-not (Test-Path $listFile)) {
    Write-Log "packages.txt not found, winget packages skipped"
    $failed++
} else {
    $packages = foreach ($line in Get-Content $listFile) {
        $line = $line.Trim()
        if (-not $line -or $line.StartsWith('#')) { continue }
        $parts = @($line -split '\|')   # Id | processes | label | detect (label and detect: panel only)
        $procs = @()
        if ($parts.Count -gt 1) {
            $procs = @($parts[1] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        }
        [pscustomobject]@{ Id = $parts[0].Trim(); Processes = $procs }
    }

    Write-Log "Running as $([Security.Principal.WindowsIdentity]::GetCurrent().Name), $(@($packages).Count) winget package(s) in list"

    foreach ($pkg in $packages) {
        $id = $pkg.Id

        # Installed, and is an upgrade available? (tested: an installed, up-to-date package returns 0 with no row)
        $listOut  = & $winget list --id $id -e --source winget --upgrade-available --accept-source-agreements --disable-interactivity 2>&1 | Out-String
        $listCode = $LASTEXITCODE
        if ((Get-Hex $listCode) -eq '0x8A150014') {
            Write-Log "$id : not installed, nothing to do"
            continue
        }
        if ($listCode -ne 0) {
            Write-Log "$id : check failed - $(Get-CodeLabel $listCode)"
            $failed++
            continue
        }
        if ($listOut -notmatch [regex]::Escape($id)) {
            Write-Log "$id : already up to date, nothing closed"
            continue
        }
        # Java in use from the system installation (e.g. a local service started at logon): winget removes the old
        # version first and fails if it is running, leaving the computer without Java. So: warn, stop it, upgrade,
        # then ask the user to restart (the service starts again at logon). Javas bundled inside applications are ignored.
        $javaStopped = $false
        if ($id -eq 'Oracle.JavaRuntimeEnvironment') {
            $sysJavaFilter = '\\Oracle\\Java\\|\\Program Files( \(x86\))?\\Java\\'
            $javaInUse = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue |
                Where-Object { $_.ExecutablePath -match $sysJavaFilter })
            if ($javaInUse.Count -gt 0) {
                if ($userLoggedOn) {
                    Send-UserMessage ("$sender doit mettre à jour Java.`r`n" +
                        ($javaWarning -f ($warnSeconds / 60)) + "`r`n" +
                        "Un redémarrage de l'ordinateur sera demandé à la fin.") $warnSeconds
                    Write-Log "$id : user warned, waiting $warnSeconds s before stopping Java"
                    Start-Sleep -Seconds $warnSeconds
                }
                $javaInUse = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue |
                    Where-Object { $_.ExecutablePath -match $sysJavaFilter })
                $javaInUse | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                Start-Sleep -Seconds 3
                $still = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue |
                    Where-Object { $_.ExecutablePath -match $sysJavaFilter })
                if ($still.Count -gt 0) {
                    Write-Log "$id : could not stop Java, not upgraded"
                    $failed++
                    continue
                }
                Write-Log "$id : Java stopped before upgrade ($(($javaInUse | ForEach-Object { $_.ExecutablePath } | Sort-Object -Unique) -join ', '))"
                $javaStopped = $true
                if (-not $closedApps.Contains($javaAppName)) { $closedApps.Add($javaAppName) }
            }
        }
        # Close the application if it is in use (forced: SYSTEM cannot ask it to close gracefully)
        if ($pkg.Processes.Count -gt 0) {
            if (-not (Close-Processes $pkg.Processes $id)) { $failed++; continue }
        }

        $out = & $winget upgrade --id $id -e --source winget --silent `
            --accept-package-agreements --accept-source-agreements --disable-interactivity 2>&1 | Out-String
        $code = $LASTEXITCODE

        Write-Log "$id : $(Get-CodeLabel $code)"
        Get-CleanOutput $out | ForEach-Object { Write-Log "    $_" }

        if ($okCodes -contains (Get-Hex $code)) {
            if ($code -eq 0) { $upgraded++; $updatedNames.Add((Get-Friendly $id)) }
        } else {
            $failed++
        }
        # Java: make sure the system Java still works (applications started through Oracle's java8path / PATH)
        if ($id -eq 'Oracle.JavaRuntimeEnvironment') {
            $shim = "C:\Program Files (x86)\Common Files\Oracle\Java\java8path\java.exe"
            $javaVer = if (Test-Path $shim) { (& $shim -version 2>&1 | Select-Object -First 1 | Out-String).Trim() } else { "" }
            if ($javaVer -match 'version') {
                Write-Log "$id : Java check OK ($javaVer)"
            } else {
                Write-Log "$id : JAVA CHECK FAILED - java8path missing or not working, reinstall Java before the user needs it"
                $failed++
            }
            if ($javaStopped) { $restartNeeded = $true }
        }
    }
}

# --- Adobe patches prepared by the launcher ---
$adobeList = Join-Path $PSScriptRoot "adobe-patches.txt"
if (Test-Path $adobeList) {
    # Outlook loads Adobe's PDFMaker / SendAsLink add-ins and blocks the patch while open
    $adobeProcesses = "Acrobat", "AcroRd32", "AcroCEF", "RdrCEF", "OUTLOOK"

    # Warn the user once, then give them time to save and close the apps themselves
    $openApps = @(Get-Process -Name $adobeProcesses -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Friendly $_.Name } | Sort-Object -Unique)
    if ($openApps.Count -gt 0 -and $userLoggedOn) {
        $appText = $openApps -join " et "
        $verb    = if ($openApps.Count -gt 1) { "seront fermés" } else { "sera fermé" }
        $pron    = if ($openApps.Count -gt 1) { "les" } else { "le" }
        Send-UserMessage ("$sender doit mettre à jour Adobe Acrobat Reader.`r`n" +
            "$appText $verb dans $($warnSeconds / 60) minutes : enregistrez votre travail.`r`n" +
            "Vous pouvez aussi $pron fermer vous-même dès maintenant.") $warnSeconds
        Write-Log "Adobe : user warned, waiting up to $warnSeconds s ($($openApps -join ', ') open)"
        $deadline = (Get-Date).AddSeconds($warnSeconds)
        while ((Get-Date) -lt $deadline -and @(Get-Process -Name $adobeProcesses -ErrorAction SilentlyContinue).Count -gt 0) {
            Start-Sleep -Seconds 5
        }
    }

    foreach ($line in Get-Content $adobeList) {
        if (-not $line.Trim()) { continue }
        $flavor, $build, $file = $line -split '\|'
        $msp = Join-Path $PSScriptRoot $file
        if (-not (Test-Path $msp)) {
            Write-Log "Adobe $flavor : patch file missing ($file)"
            $failed++
            continue
        }
        # Signed by Adobe? (checked on the share already; checked again here, on the copy SYSTEM installs)
        try { [void](Assert-Publisher $msp $publishers.Adobe) }
        catch { Write-Log "Adobe $flavor : signature check failed - $($_.Exception.Message)"; $failed++; Remove-Item -Path $msp -Force -ErrorAction SilentlyContinue; continue }
        if (-not (Close-Processes $adobeProcesses "Adobe $flavor")) { $failed++; continue }

        $mspLog = Join-Path $PSScriptRoot "adobe-$flavor.log"
        $proc = Start-Process msiexec.exe -ArgumentList "/p `"$msp`" /qn /norestart /l*v `"$mspLog`"" -Wait -PassThru
        $code = $proc.ExitCode
        $label = switch ($code) {
            0       { "Patched" }
            3010    { "Patched, restart required" }
            1642    { "Patch not applicable to the installed product" }
            1618    { "Another installation in progress" }
            1603    { "Fatal error during installation" }
            default { "Unknown msiexec code" }
        }
        Write-Log "Adobe $flavor : msiexec $code $label (build $build)"
        if ($code -eq 0 -or $code -eq 3010) {
            $upgraded++
            if (-not $updatedNames.Contains('Adobe Acrobat Reader')) { $updatedNames.Add('Adobe Acrobat Reader') }
            if ($code -eq 3010) { $restartNeeded = $true }
        } else {
            $failed++
            Write-Log "    details: $mspLog"
        }
        Remove-Item -Path $msp -Force -ErrorAction SilentlyContinue
    }
}

# Runs the 1Password MSI and watches its log. The MSI's AI_UnpinFromStartScreen action can hang on the
# user's Start menu host (seen on several workstations, even right after a restart of it).
# Restarting the host while the action hangs unblocks it within ~25 s; Windows relaunches it by itself.
function Invoke-OnePasswordMsi([string]$Arguments, [string]$MsiLog) {
    $proc = Start-Process msiexec.exe -ArgumentList $Arguments -PassThru
    $null = $proc.Handle   # keeps ExitCode readable after exit (Start-Process -PassThru quirk)
    $restarts = 0
    while (-not $proc.WaitForExit(10000)) {
        $last = Get-Content $MsiLog -Tail 1 -ErrorAction SilentlyContinue
        $idle = if (Test-Path $MsiLog) { ((Get-Date) - (Get-Item $MsiLog).LastWriteTime).TotalSeconds } else { 0 }
        if ($last -match 'AI_UnpinFromStartScreen' -and $idle -gt 30 -and $restarts -lt 3) {
            Get-Process StartMenuExperienceHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            $restarts++
            Write-Log "1Password : install stalled on AI_UnpinFromStartScreen, Start menu host restarted ($restarts)"
        }
    }
    return $proc.ExitCode
}

# --- 1Password MSI prepared by the launcher (force-closed first: an open 1Password locks app.asar and stalls msiexec) ---
$opList = Join-Path $PSScriptRoot "onepassword.txt"
if (Test-Path $opList) {
    $opVersion, $opOldCode = (Get-Content $opList -Raw).Trim() -split '\|'
    $opMsi       = Join-Path $PSScriptRoot "1PasswordSetup-latest.msi"
    $opLog       = Join-Path $PSScriptRoot "1password.log"
    $opArgs      = "/i `"$opMsi`" MANAGED_INSTALL=1 MANAGED_UPDATE=1 /qn /norestart /l*v `"$opLog`""
    $opProcesses = "1Password", "1Password-BrowserSupport"

    $opSigned = $true
    try { [void](Assert-Publisher $opMsi $publishers.OnePassword) }
    catch { Write-Log "1Password : signature check failed - $($_.Exception.Message)"; $opSigned = $false }
    if (-not $opSigned) {
        $failed++
    } elseif (-not (Close-Processes $opProcesses "1Password")) {
        $failed++
    } else {
        $code = Invoke-OnePasswordMsi $opArgs $opLog
        if ($code -eq 1638) {
            # Another version is installed and the MSI does not upgrade it in place: remove it, then install
            Write-Log "1Password : 1638 another version installed, uninstalling $opOldCode first"
            $u = (Start-Process msiexec.exe -ArgumentList "/x $opOldCode /qn /norestart" -Wait -PassThru).ExitCode
            Write-Log "1Password : uninstall exit code $u"
            $code = Invoke-OnePasswordMsi $opArgs $opLog
        }
        $label = switch ($code) {
            0       { "Installed" }
            3010    { "Installed, restart required" }
            1638    { "Another version is already installed" }
            1618    { "Another installation in progress" }
            1603    { "Fatal error during installation" }
            default { "Unknown msiexec code" }
        }
        Write-Log "1Password : msiexec $code $label (version $opVersion)"
        if ($code -eq 0 -or $code -eq 3010) {
            $upgraded++
            $updatedNames.Add('1Password')
            if ($code -eq 3010) { $restartNeeded = $true }
        } else {
            $failed++
            Write-Log "    details: $opLog"
        }
    }
    Remove-Item -Path $opMsi -Force -ErrorAction SilentlyContinue
}

# --- Tell the logged-on user what happened (only if something changed) ---
if ($updatedNames.Count -gt 0 -or $closedApps.Count -gt 0) {
    if (-not $userLoggedOn) {
        Write-Log "Notify: no user logged on, no message shown (restart needed: $restartNeeded)"
    } else {
        # Restart line first: msg.exe is limited to 255 characters and the lists may be cut at the end
        $text = "Mise à jour terminée par $senderLower."
        if ($restartNeeded)            { $text += "`r`nRedémarrez votre ordinateur dès que possible." }
        if ($updatedNames.Count -gt 0) { $text += "`r`nMis à jour : " + ($updatedNames -join ", ") + "." }
        if ($closedApps.Count -gt 0)   { $text += "`r`nFermés (à rouvrir au besoin) : " + ($closedApps -join ", ") + "." }
        Send-UserMessage $text
        Write-Log "Notify: message sent (updated: $($updatedNames -join ', '); closed: $($closedApps -join ', '); restart needed: $restartNeeded)"
    }
}

Write-Log "Summary: $upgraded upgraded, $closed app(s) closed, $failed failed"
exit $failed
