# Live view of a winget-upgrade PDQ run on a target machine.
# Run on the VM with the deployment account, while PDQ deploys. Ctrl+C to stop.
param(
    [Parameter(Mandatory)][string]$ComputerName,
    [int]   $IntervalSeconds = 5
)

. (Join-Path $PSScriptRoot "config.ps1")   # site settings: $deployRoot, $majBase, $deployAccount
$errorPattern   = 'preparation failed|check failed|could not|Unknown code|forbidden|not applicable|Fatal|file missing|path missing|summary missing [1-9]|[1-9]\d* failed'
$successPattern = 'Upgraded|Patched|ready on deploy|up to date|download complete'

$Host.UI.RawUI.WindowTitle = "Suivi - $ComputerName"
$session  = New-PSSession -ComputerName $ComputerName -ErrorAction Stop
$shown    = 0
$lastSize = @{}
Write-Host "Watching $ComputerName every $IntervalSeconds s - Ctrl+C to stop" -ForegroundColor Cyan

try {
    while ($true) {
        $remote = Invoke-Command -Session $session -ScriptBlock {
            $dir  = Join-Path $env:ProgramData "MAJ-Logiciels\winget-upgrade"   # protected work folder of the package
            if (-not (Test-Path $dir)) { $dir = "C:\Windows\Temp\winget-upgrade" }       # computers not run since
            $task = "none"
            try { $task = (Get-ScheduledTask -TaskName "Winget-Upgrade-PDQ" -ErrorAction Stop).State.ToString() } catch { }
            [pscustomobject]@{
                Log       = @(Get-Content "$dir\winget-upgrade.log" -Encoding UTF8 -ErrorAction SilentlyContinue)
                Downloads = @(Get-ChildItem $dir -Filter *.download -ErrorAction SilentlyContinue | Select-Object Name, Length)
                Task      = $task
                Msiexec   = @(Get-CimInstance Win32_Process -Filter "Name='msiexec.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -notmatch '\s/V\b' }).Count   # /V = Windows Installer service, not an install
                Winget    = @(Get-Process winget -ErrorAction SilentlyContinue).Count
            }
        }

        # New log lines (the launcher recreates the log at each run)
        if ($remote.Log.Count -lt $shown) {
            $shown = 0
            $lastSize = @{}
            Write-Host "--- new run ---" -ForegroundColor Cyan
        }
        for ($i = $shown; $i -lt $remote.Log.Count; $i++) {
            $line  = $remote.Log[$i]
            $color = if ($line -match 'Launcher: .*exit 0\s*$') { 'Green' } elseif ($line -match 'Launcher: .*exit [1-9]\d*\s*$') { 'Red' }
                      elseif ($line -match $errorPattern) { 'Red' } elseif ($line -match $successPattern) { 'Green' } else { 'Gray' }
            Write-Host $line -ForegroundColor $color
        }
        $shown = $remote.Log.Count

        # Status bar
        $parts = @("SYSTEM task: $($remote.Task)")
        foreach ($d in $remote.Downloads) {
            $rate = 0
            if ($lastSize.ContainsKey($d.Name)) {
                $rate = [math]::Round(($d.Length - $lastSize[$d.Name]) / 1MB / $IntervalSeconds, 1)
            }
            $lastSize[$d.Name] = $d.Length
            $parts += "downloading $($d.Name): $([math]::Round($d.Length / 1MB)) MB at $rate MB/s"
        }
        Get-ChildItem "$deployRoot\Adobe", "$deployRoot\Adobe-x64" -Filter *.partial -ErrorAction SilentlyContinue |
            ForEach-Object { $parts += "copying to deploy`$: $($_.Name) $([math]::Round($_.Length / 1MB)) MB" }
        if ($remote.Winget -gt 0)  { $parts += "winget running" }
        if ($remote.Msiexec -gt 0) { $parts += "msiexec running" }

        Write-Progress -Activity "winget-upgrade on $ComputerName - $(Get-Date -Format 'HH:mm:ss')" -Status ($parts -join " | ")
        Start-Sleep -Seconds $IntervalSeconds
    }
} finally {
    Write-Progress -Activity "winget-upgrade" -Completed
    Remove-PSSession $session
}
