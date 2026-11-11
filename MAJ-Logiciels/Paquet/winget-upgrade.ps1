# Launcher - runs under the PDQ deploy account, which has access to deploy$.
# 1. Keeps the Adobe patch cache on deploy$ up to date (one download for the whole fleet).
# 2. Copies the Adobe patch this machine needs.
# 3. Runs the worker as SYSTEM (winget fails under the deploy account: 0x8A15000F).
# Runs as SYSTEM (default: the computer account reads the share) or as the deployment account.

. (Join-Path $PSScriptRoot "maj-secure.ps1")   # Initialize-SecureFolder, Assert-Publisher

# Work folder: SYSTEM runs what is copied here, so only SYSTEM and Administrators may write to it
# (not C:\Windows\Temp, where any user can create the folder first and swap the files).
$workRoot       = $env:ProgramData
$workRelative   = "MAJ-Logiciels\winget-upgrade"
$workDir        = Join-Path $workRoot $workRelative
$worker         = Join-Path $workDir "winget-worker.ps1"
$log            = Join-Path $workDir "winget-upgrade.log"
$adobeList      = Join-Path $workDir "adobe-patches.txt"
$task           = "Winget-Upgrade-PDQ"
$timeoutMinutes = 45

# Adobe patch cache on deploy$
$deployRoot  = Split-Path $PSScriptRoot -Parent   # this script lives in <deploy share>\Winget-upgrade

# Package settings (optional): package.json next to this script. Optional modules are off without it.
$pkgCfg = $null
try { $pkgCfg = Get-Content (Join-Path $PSScriptRoot "package.json") -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json } catch { }
$useAdobe       = [bool]($pkgCfg -and $pkgCfg.Modules -and $pkgCfg.Modules.AdobeReader)
$useOnePassword = [bool]($pkgCfg -and $pkgCfg.Modules -and $pkgCfg.Modules.OnePassword)
if ($pkgCfg -and $pkgCfg.TimeoutMinutes) { $timeoutMinutes = [int]$pkgCfg.TimeoutMinutes }
# Expected publishers of the installers downloaded for the fleet (package.json can override them)
$publishers      = Get-ExpectedPublishers $pkgCfg
$adobePublishers = $publishers.Adobe
$opPublishers    = $publishers.OnePassword
$cacheFile   = "$deployRoot\Adobe\adobe-latest.json"
$lockFile    = "$deployRoot\Adobe\adobe-download.lock"
$cacheHours  = 12
# Each flavor is identified by its MSI product code (the display name is misleading:
# "Adobe Acrobat (64-bit)" can be Reader 64-bit). Patch targets verified against the MSP files.
$flavors = @(
    @{
        Name           = "Reader32"
        ProductCode    = '^\{AC76BA86-7AD7-\d{4}-7B44-AC0F074E4100\}$'
        Folder         = "$deployRoot\Adobe"
        OldFolder      = "$deployRoot\Adobe\OLD"
        UpdateSetupIni = $true
        Url            = "https://ardownload2.adobe.com/pub/adobe/reader/win/AcrobatDC/{0}/AcroRdrDCUpd{0}.msp"
        File           = "AcroRdrDCUpd{0}.msp"
    },
    @{
        Name           = "ReaderX64"
        ProductCode    = '^\{AC76BA86-\d{4}-1033-7760-BC15014EA700\}$'
        Folder         = "$deployRoot\Adobe-x64"
        OldFolder      = "$deployRoot\Adobe-x64\OLD"
        UpdateSetupIni = $false
        Url            = "https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcroRdrDCx64Upd{0}.msp"
        File           = "AcroRdrDCx64Upd{0}.msp"
    },
    @{
        Name           = "ReaderX64MUI"
        ProductCode    = '^\{AC76BA86-1033-FF00-7760-BC15014EA700\}$'
        Folder         = "$deployRoot\Adobe-x64"
        OldFolder      = "$deployRoot\Adobe-x64\OLD"
        UpdateSetupIni = $false
        Url            = "https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcroRdrDCx64Upd{0}_MUI.msp"
        File           = "AcroRdrDCx64Upd{0}_MUI.msp"
    },
    @{
        Name           = "AcrobatX64"
        ProductCode    = '^\{AC76BA86-1033-FFFF-7760-BC15014EA700\}$'
        Folder         = "$deployRoot\Adobe-x64"
        OldFolder      = "$deployRoot\Adobe-x64\OLD"
        UpdateSetupIni = $false
        Url            = "https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcrobatDCx64Upd{0}.msp"
        File           = "AcrobatDCx64Upd{0}.msp"
    },
    @{
        Name           = "Acrobat32"
        ProductCode    = '^\{AC76BA86-1033-FFFF-7760-0C0F074E4100\}$'
        Folder         = "$deployRoot\Adobe-x64"
        OldFolder      = "$deployRoot\Adobe-x64\OLD"
        UpdateSetupIni = $false
        Url            = "https://ardownload2.adobe.com/pub/adobe/acrobat/win/AcrobatDC/{0}/AcrobatDCUpd{0}.msp"
        File           = "AcrobatDCUpd{0}.msp"
    }
)

# 1Password: per-machine MSI, cached on deploy$ (same folder as the manual PDQ package)
$opFolder    = "$deployRoot\1Password"
$opOldFolder = "$deployRoot\1Password\old"
$opMsiName   = "1PasswordSetup-latest.msi"
$opUrl       = "https://downloads.1password.com/win/1PasswordSetup-latest.msi"
$opStamp     = "$deployRoot\1Password\last-modified.txt"
$opList      = Join-Path $workDir "onepassword.txt"

# Log copies on deploy$ (outside the package folder, so PDQ never copies them to the computers)
$logShare    = "$deployRoot\Winget-upgrade-logs"
$logDays     = 90
# Free space needed on C: before copying a patch (patch + Windows Installer cache + extraction)
$minFreeGbAdobe       = 3
$minFreeGbOnePassword = 1

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Message" | Out-File -FilePath $log -Append -Encoding utf8
}

# "26.003.22075" -> 2600322075 (Adobe build number used in patch file names)
function ConvertTo-AdobeBuild([string]$Version) {
    $p = $Version.Split('.')
    if ($p.Count -ne 3) { return $null }
    return [long]("{0}{1}{2}" -f $p[0], $p[1].PadLeft(3, '0'), $p[2].PadLeft(5, '0'))
}

function Test-Url([string]$Url) {
    try {
        $req = [Net.HttpWebRequest]::Create($Url)
        $req.Method  = "HEAD"
        $req.Timeout = 30000
        $resp = $req.GetResponse()
        $ok = ([int]$resp.StatusCode -eq 200)
        $resp.Close()
        return $ok
    } catch {
        return $false
    }
}

# Adobe products installed on this machine, matched by MSI product code
function Get-InstalledAdobe {
    $keys = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
            "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    $found = @()
    foreach ($e in (Get-ItemProperty $keys -ErrorAction SilentlyContinue)) {
        foreach ($f in $flavors) {
            if ($e.PSChildName -match $f.ProductCode) {
                # Leftover uninstall keys (no name, no version) are not real installs: patching them gives 1642
                if (-not $e.DisplayVersion) {
                    Write-Log "Adobe $($f.Name) $($e.PSChildName): orphan registry entry (no version), skipped"
                    continue
                }
                $found += [pscustomobject]@{ Flavor = $f.Name; Name = $e.DisplayName; Version = $e.DisplayVersion; Code = $e.PSChildName }
            }
        }
    }
    return $found
}

# Latest published build per flavor: versions from the winget-pkgs repo,
# keeping the highest one whose patch actually exists on Adobe's server
function Get-LatestBuilds {
    $api = "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/a/Adobe/Acrobat/Reader/64-bit"
    $versions = Invoke-RestMethod -Uri $api |
        Where-Object { $_.type -eq "dir" -and $_.name -match '^\d+\.\d+\.\d+$' } |
        ForEach-Object { $_.name } |
        Sort-Object { [version]$_ } -Descending |
        Select-Object -First 5
    $builds = @{}
    foreach ($f in $flavors) {
        foreach ($v in $versions) {
            $b = ConvertTo-AdobeBuild $v
            if (Test-Url ($f.Url -f $b)) { $builds[$f.Name] = $b; break }
        }
    }
    return $builds
}

# Exclusive lock on deploy$ so only one machine downloads at a time
function Lock-AdobeCache {
    $deadline = (Get-Date).AddMinutes(30)
    while ((Get-Date) -lt $deadline) {
        try {
            return [IO.File]::Open($lockFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        } catch {
            Start-Sleep -Seconds 10
        }
    }
    return $null
}

# Download to a local file (no SMB share in the loop), logging progress every 100 MB
function Get-WebFile([string]$Url, [string]$Path) {
    $req = [Net.HttpWebRequest]::Create($Url)
    $req.Timeout          = 60000
    $req.ReadWriteTimeout = 120000
    $resp = $req.GetResponse()
    try {
        $in  = $resp.GetResponseStream()
        $out = [IO.File]::Create($Path)
        try {
            $buf   = New-Object byte[] 1048576
            $total = 0
            $next  = 100MB
            while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) {
                $out.Write($buf, 0, $n)
                $total += $n
                if ($total -ge $next) {
                    Write-Log "    downloaded $([math]::Round($total / 1MB)) MB of $([math]::Round($resp.ContentLength / 1MB)) MB"
                    $next += 100MB
                }
            }
        } finally {
            $out.Close()
        }
        if ($resp.ContentLength -gt 0 -and $total -ne $resp.ContentLength) {
            throw "incomplete download: $total of $($resp.ContentLength) bytes"
        }
    } finally {
        $resp.Close()
    }
}

# Make sure the patch is on deploy$; download it once if missing
function Update-AdobeShare($Flavor, [long]$Build) {
    $fileName = $Flavor.File -f $Build
    $target   = Join-Path $Flavor.Folder $fileName
    if (Test-Path $target) { return $target }

    New-Item -Path $Flavor.Folder, $Flavor.OldFolder -ItemType Directory -Force | Out-Null
    $localTmp = Join-Path $workDir "$fileName.download"
    $shareTmp = "$target.partial"
    Remove-Item $localTmp, $shareTmp -Force -ErrorAction SilentlyContinue

    # 1. Download locally, 2. copy the complete file to deploy$, 3. rename
    Write-Log "Adobe $($Flavor.Name): downloading $fileName"
    Get-WebFile ($Flavor.Url -f $Build) $localTmp
    # Never publish to the whole fleet an installer that is not signed by Adobe
    try { $signer = Assert-Publisher $localTmp $adobePublishers }
    catch { Remove-Item $localTmp -Force -ErrorAction SilentlyContinue; throw }
    Write-Log "Adobe $($Flavor.Name): download complete, signed by $signer, copying to $($Flavor.Folder)"
    Copy-Item -Path $localTmp -Destination $shareTmp -Force
    Remove-Item $localTmp -Force -ErrorAction SilentlyContinue

    # Retire previous patches of this flavor, as done manually before
    $namePattern = '^' + ([regex]::Escape(($Flavor.File -f 'BUILD'))).Replace('BUILD', '\d+') + '$'
    Get-ChildItem -Path $Flavor.Folder -Filter "*.msp" -File |
        Where-Object { $_.Name -match $namePattern -and $_.Name -ne $fileName } |
        Move-Item -Destination $Flavor.OldFolder -Force
    Move-Item -Path $shareTmp -Destination $target -Force

    if ($Flavor.UpdateSetupIni) {
        $ini = Join-Path $Flavor.Folder "setup.ini"
        if (Test-Path $ini) {
            (Get-Content $ini) -replace '^PATCH=.*$', "PATCH=$fileName" | Set-Content -Path $ini -Encoding ASCII
            Write-Log "Adobe $($Flavor.Name): setup.ini now points to $fileName"
        }
    }
    Write-Log "Adobe $($Flavor.Name): $fileName ready on deploy$"
    return $target
}

# Last-Modified header of a URL (HEAD request, nothing downloaded)
function Get-LastModified([string]$Url) {
    $req = [Net.HttpWebRequest]::Create($Url)
    $req.Method  = "HEAD"
    $req.Timeout = 30000
    $resp = $req.GetResponse()
    try { return $resp.Headers["Last-Modified"] } finally { $resp.Close() }
}

# Read a property (ProductVersion...) from an MSI file
function Get-MsiProperty([string]$Path, [string]$Property) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db   = $installer.GetType().InvokeMember("OpenDatabase", "InvokeMethod", $null, $installer, @($Path, 0))
    $view = $db.GetType().InvokeMember("OpenView", "InvokeMethod", $null, $db, @("SELECT Value FROM Property WHERE Property='$Property'"))
    [void]$view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null)
    $rec  = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
    $value = if ($rec) { $rec.GetType().InvokeMember("StringData", "GetProperty", $null, $rec, 1) } else { $null }
    [void]$view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null)
    foreach ($o in $rec, $view, $db, $installer) { if ($o) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($o) } }
    return [string]$value
}

# Make sure deploy$ has the latest 1Password MSI; download it once if 1Password published a new one
function Update-OnePasswordShare {
    $target = Join-Path $opFolder $opMsiName
    $online = Get-LastModified $opUrl
    $known  = if (Test-Path $opStamp) { (Get-Content $opStamp -Raw).Trim() } else { "" }
    if ((Test-Path $target) -and $online -eq $known) { return $target }

    New-Item -Path $opFolder, $opOldFolder -ItemType Directory -Force | Out-Null
    $localTmp = Join-Path $workDir "$opMsiName.download"
    $shareTmp = "$target.partial"
    Remove-Item $localTmp, $shareTmp -Force -ErrorAction SilentlyContinue

    Write-Log "1Password: new MSI published ($online), downloading"
    Get-WebFile $opUrl $localTmp
    try { $signer = Assert-Publisher $localTmp $opPublishers }
    catch { Remove-Item $localTmp -Force -ErrorAction SilentlyContinue; throw }
    Write-Log "1Password: download complete, signed by $signer, copying to $opFolder"
    Copy-Item -Path $localTmp -Destination $shareTmp -Force
    Remove-Item $localTmp -Force -ErrorAction SilentlyContinue

    # Keep the previous MSI in old\, as done manually before
    if (Test-Path $target) { Move-Item -Path $target -Destination (Join-Path $opOldFolder $opMsiName) -Force }
    Move-Item -Path $shareTmp -Destination $target -Force
    Set-Content -Path $opStamp -Value $online -Encoding ASCII
    Write-Log "1Password: $opMsiName ready on deploy$ (version $(Get-MsiProperty $target 'ProductVersion'))"
    return $target
}

# Free space on C: in GB
function Get-FreeGb {
    [math]::Round((Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace / 1GB, 1)
}

# Copy this run's log to deploy$: latest per computer + dated history (kept $logDays days)
function Save-LogToShare {
    try {
        $hist = Join-Path $logShare "history"
        New-Item -Path $logShare, $hist -ItemType Directory -Force | Out-Null
        Copy-Item -Path $log -Destination (Join-Path $logShare "$env:COMPUTERNAME.log") -Force
        Copy-Item -Path $log -Destination (Join-Path $hist ("{0}-{1}.log" -f $env:COMPUTERNAME, (Get-Date -Format 'yyyyMMdd-HHmm'))) -Force
        Get-ChildItem -Path $hist -Filter "$env:COMPUTERNAME-*.log" -File |
            Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$logDays) } | Remove-Item -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Output "Could not copy the log to $logShare : $($_.Exception.Message)"
    }
}

# --- Prepare work folder (protected ACL; an untrusted folder already there is moved aside) ---
try {
    $secured = Initialize-SecureFolder $workRoot $workRelative
} catch {
    # No safe local folder: nothing runs. Same final line format as every run, so the panel shows FAILED.
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') Launcher: work folder $workDir could not be secured ($($_.Exception.Message)), nothing done, exit 3"
    try { New-Item -Path $logShare -ItemType Directory -Force | Out-Null; $line | Set-Content -Path (Join-Path $logShare "$env:COMPUTERNAME.log") -Encoding utf8 } catch { }
    Write-Output $line
    exit 3
}
Remove-Item -Path $log, $adobeList, $opList -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $workDir "*.msp"), (Join-Path $workDir "*.msi"), (Join-Path $workDir "*.download") -ErrorAction SilentlyContinue
foreach ($m in $secured.Messages) { Write-Log "Launcher: $m" }
Write-Log "Launcher: running as $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"

# --- Safety: never on a server, never on a computer excluded on purpose ---
$os = Get-CimInstance Win32_OperatingSystem
$excludes = @(Get-Content "$logShare\exclude.txt" -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
$skipReason = if ($os.ProductType -ne 1) { "server OS ($($os.Caption))" }
              elseif ($excludes | Where-Object { $env:COMPUTERNAME -like $_ }) { "listed in exclude.txt" }
              else { $null }
if ($skipReason) {
    Write-Log "Launcher: $skipReason, nothing done"
    Write-Output "Nothing done: $skipReason"
    Save-LogToShare
    exit 0
}

# --- Copy the worker and the package list, and check the copies ---
foreach ($file in @("winget-worker.ps1", "maj-secure.ps1", "packages.txt") + @(if (Test-Path (Join-Path $PSScriptRoot "package.json")) { "package.json" })) {
    $src = Join-Path $PSScriptRoot $file
    $dst = Join-Path $workDir $file
    Copy-Item -Path $src -Destination $dst -Force -ErrorAction SilentlyContinue
    $srcHash = (Get-FileHash $src -ErrorAction SilentlyContinue).Hash
    $dstHash = (Get-FileHash $dst -ErrorAction SilentlyContinue).Hash
    if (-not $srcHash -or $srcHash -ne $dstHash) {
        Write-Log "Launcher: copy of $file failed or incomplete (hash mismatch), nothing done"
        Write-Output "Copy of $file failed"
        Save-LogToShare
        exit 1
    }
}

# --- Adobe: cache on deploy$ and copy the patch this machine needs ---
$adobeFailed = 0
$installed = @(if ($useAdobe) { Get-InstalledAdobe })
if (-not $useAdobe) {
    # module disabled in package.json: nothing logged (the panel reads "Adobe" lines as progress)
} elseif ($installed.Count -eq 0) {
    Write-Log "Adobe: not installed, nothing to do"
} elseif ((Get-FreeGb) -lt $minFreeGbAdobe) {
    Write-Log "Adobe: not enough free space on C: ($(Get-FreeGb) GB free, $minFreeGbAdobe GB needed), skipped"
    $adobeFailed++
} else {
    $lock = $null
    try {
        $lock = Lock-AdobeCache
        if (-not $lock) { throw "could not get the download lock on deploy$" }

        $builds = $null
        if ((Test-Path $cacheFile) -and ((Get-Date) - (Get-Item $cacheFile).LastWriteTime).TotalHours -lt $cacheHours) {
            $builds = @{}
            (Get-Content $cacheFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $builds[$_.Name] = [long]$_.Value }
            Write-Log "Adobe: latest builds read from cache"
        }
        $missing = @($installed | Where-Object { -not $builds -or -not $builds.ContainsKey($_.Flavor) })
        if (-not $builds -or $missing.Count -gt 0) {
            $builds = Get-LatestBuilds
            $builds | ConvertTo-Json | Set-Content -Path $cacheFile -Encoding ASCII
            Write-Log "Adobe: latest builds checked online"
        }

        foreach ($i in $installed) {
            $f       = $flavors | Where-Object { $_.Name -eq $i.Flavor }
            $latest  = $builds[$i.Flavor]
            $current = ConvertTo-AdobeBuild $i.Version
            if (-not $latest) {
                Write-Log "Adobe $($i.Flavor): no patch found online"
                $adobeFailed++
                continue
            }
            if ($current -and $current -ge $latest) {
                Write-Log "Adobe $($i.Flavor): $($i.Version) up to date"
                continue
            }
            $sharePath = Update-AdobeShare $f $latest
            Copy-Item -Path $sharePath -Destination $workDir -Force
            "$($i.Flavor)|$latest|$(Split-Path $sharePath -Leaf)" | Out-File -FilePath $adobeList -Append -Encoding ascii
            Write-Log "Adobe $($i.Flavor) $($i.Code): $($i.Version) -> build $latest, patch copied locally"
        }
    } catch {
        Write-Log "Adobe: preparation failed - $($_.Exception.Message)"
        $adobeFailed++
    } finally {
        if ($lock) { $lock.Close() }
    }
}

# --- 1Password: cache on deploy$ and copy the MSI if this machine is behind ---
$opFailed = 0
$opInstalled = if ($useOnePassword) { Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -eq "1Password" -and $_.DisplayVersion } | Select-Object -First 1 }
if (-not $useOnePassword) {
    # module disabled in package.json
} elseif (-not $opInstalled) {
    Write-Log "1Password: not installed (per-machine), nothing to do"
} elseif ((Get-FreeGb) -lt $minFreeGbOnePassword) {
    Write-Log "1Password: not enough free space on C: ($(Get-FreeGb) GB free, $minFreeGbOnePassword GB needed), skipped"
    $opFailed++
} else {
    $lock = $null
    try {
        $lock = Lock-AdobeCache
        if (-not $lock) { throw "could not get the download lock on deploy$" }
        $msiPath = Update-OnePasswordShare
        $msiVersion = Get-MsiProperty $msiPath "ProductVersion"
        if ([version]$opInstalled.DisplayVersion -ge [version]$msiVersion) {
            Write-Log "1Password: $($opInstalled.DisplayVersion) up to date"
        } else {
            Copy-Item -Path $msiPath -Destination $workDir -Force
            "$msiVersion|$($opInstalled.PSChildName)" | Set-Content -Path $opList -Encoding ASCII
            Write-Log "1Password: $($opInstalled.DisplayVersion) -> $msiVersion, MSI copied locally"
        }
    } catch {
        Write-Log "1Password: preparation failed - $($_.Exception.Message)"
        $opFailed++
    } finally {
        if ($lock) { $lock.Close() }
    }
}

# --- Run the worker as SYSTEM ---
$action    = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$worker`" -LogPath `"$log`""
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $task -Action $action -Principal $principal -Force | Out-Null
$start = Get-Date
Start-ScheduledTask -TaskName $task

# Wait until the task has really run and finished: right after Start-ScheduledTask it can still be
# Ready or Queued, and stopping the wait then would remove the task before the worker ran
$seenRunning = $false
do {
    Start-Sleep -Seconds 5
    $state  = (Get-ScheduledTask -TaskName $task).State
    if ($state -eq "Running") { $seenRunning = $true }
    $lastRun = (Get-ScheduledTaskInfo -TaskName $task).LastRunTime
    $hasRun  = $seenRunning -or ($lastRun -and $lastRun -ge $start.AddSeconds(-5))
    $waiting = ($state -eq "Running") -or ($state -eq "Queued") -or (-not $hasRun)
} while ($waiting -and ((Get-Date) - $start).TotalMinutes -lt $timeoutMinutes)

$result = (Get-ScheduledTaskInfo -TaskName $task).LastTaskResult
Unregister-ScheduledTask -TaskName $task -Confirm:$false

if (Test-Path $log) { Get-Content $log -Encoding UTF8 }

if ($state -eq "Running") {
    Write-Log "Launcher: timeout, worker still running after $timeoutMinutes min"
    Write-Output "Timeout: worker still running"
    Save-LogToShare
    exit 2
}

# The worker always ends with a "Summary:" line: without it, it did not run (or not to the end)
$workerMissing = 0
if (-not ((Test-Path $log) -and (Select-String -Path $log -Pattern 'Summary:' -Quiet))) {
    $workerMissing = 1
    Write-Log "Launcher: the worker wrote no summary (task state $state, task result $result) - counted as a failure"
    Write-Output "Worker wrote no summary - counted as a failure"
}

$total = [int]$result + $adobeFailed + $opFailed + $workerMissing
$final = "Launcher: worker result $result, Adobe preparation failures $adobeFailed, 1Password preparation failures $opFailed, worker summary missing $workerMissing, exit $total"
Write-Log $final
Write-Output $final
Save-LogToShare
exit $total
