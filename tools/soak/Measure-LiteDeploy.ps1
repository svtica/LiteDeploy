<#
.SYNOPSIS
    Soak test for LiteDeploy: drives LiteDeploy.exe through its UI for a long
    time and samples its resource counters.

.DESCRIPTION
    Copies the LiteDeploy build output to a work folder, puts a PsExec.exe
    stand-in next to it (tools/soak/PsExecStub.cs: no network, no service),
    starts LiteDeploy and drives it with UI Automation, the way an
    administrator uses it during a working day:
      - fills the computer list (Computers > Add, then "Use List");
      - every cycle: "Start Deploy" on all the computers (one PsExec per
        computer, output parsed into the process report);
      - every 3rd cycle: Computers > Ping, wait for the end, close;
      - every 5th cycle: edit the computer list again ("Use List" and the
        confirmation), check all computers again.
    Message boxes are answered automatically (Yes, else OK) and counted.

    Samples every SampleSeconds: Private Bytes, Working Set, kernel handles,
    GDI objects, USER objects (GetGuiResources), threads, CPU (% of one core)
    and the .NET GC heap. Writes one CSV row per sample and a Markdown
    summary (value after warm-up, value at the end, growth per hour after
    warm-up) to SummaryPath.

.EXAMPLE
    .\Measure-LiteDeploy.ps1 -ExePath ..\..\bin\Release\LiteDeploy.exe -DurationMinutes 180
#>
param(
    [Parameter(Mandatory = $true)] [string] $ExePath,
    [int]    $DurationMinutes = 180,
    [int]    $SampleSeconds   = 30,
    [int]    $WarmupMinutes   = 10,
    # One deployment to every computer of the list every CycleSeconds
    [int]    $CycleSeconds    = 20,
    [string] $Label           = 'run',
    [string] $OutCsv          = "soak-$Label.csv",
    [string] $SummaryPath     = "soak-$Label.md"
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
$uiaRefs = @(
    [System.Windows.Automation.AutomationElement].Assembly.Location,
    [System.Windows.Automation.ControlType].Assembly.Location,
    [System.Windows.Threading.Dispatcher].Assembly.Location
)

Add-Type -ReferencedAssemblies $uiaRefs -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

public static class SoakUi
{
    [DllImport("user32.dll")]
    public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    // Real mouse click in the middle of an element. Used for menu items: invoking a menu item
    // through accessibility while its drop-down is open leaves the target UI unresponsive to
    // UI Automation when the item opens a modal window.
    public static bool ClickCenter(AutomationElement e)
    {
        try
        {
            System.Windows.Rect r = e.Current.BoundingRectangle;
            if (r.IsEmpty || r.Width <= 0) { LastError = "empty bounding rectangle"; return false; }
            SetCursorPos((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
            Thread.Sleep(100);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);   // left down
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);   // left up
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public static void Activate(AutomationElement w)
    {
        try { SetForegroundWindow(new IntPtr(w.Current.NativeWindowHandle)); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    // Window by its native handle: does not depend on the UI Automation tree, which can
    // stop listing the main window after another top-level window of the process is minimized.
    public static AutomationElement FromHandle(int handle)
    {
        try { return handle == 0 ? null : AutomationElement.FromHandle(new IntPtr(handle)); }
        catch (Exception e) { LastError = e.Message; return null; }
    }

    // Titles of the windows of the process, for diagnostics
    public static string Titles(int pid)
    {
        List<string> names = new List<string>();
        foreach (AutomationElement w in Windows(pid))
        {
            try { names.Add("'" + w.Current.Name + "'"); } catch { }
        }
        return string.Join(", ", names.ToArray());
    }

    public static void Minimize(AutomationElement w)
    {
        try { ((WindowPattern)w.GetCurrentPattern(WindowPattern.Pattern)).SetWindowVisualState(WindowVisualState.Minimized); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public static string LastError = "";
    static int pending = 0;
    public static int Pending { get { return pending; } }

    static Condition ByPid(int pid)
    {
        return new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
    }

    // Windows of the process: top-level ones and the windows they own
    // (UI Automation shows an owned window as a child of its owner).
    public static List<AutomationElement> Windows(int pid)
    {
        List<AutomationElement> list = new List<AutomationElement>();
        Condition win = new AndCondition(ByPid(pid),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        try
        {
            foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, win))
                AddWindows(top, win, list, 0);
        }
        catch (Exception e) { LastError = e.Message; }
        return list;
    }

    static void AddWindows(AutomationElement w, Condition win, List<AutomationElement> list, int depth)
    {
        list.Add(w);
        if (depth >= 3) return;
        foreach (AutomationElement child in w.FindAll(TreeScope.Children, win))
            AddWindows(child, win, list, depth + 1);
    }

    public static AutomationElement FindWindow(int pid, string title)
    {
        foreach (AutomationElement w in Windows(pid))
        {
            try { if (w.Current.Name == title) return w; }
            catch { }
        }
        return null;
    }

    // Window whose title is the given text or starts with it followed by " - "
    // (the computer list adds the Active Directory state to its title)
    public static AutomationElement FindWindowPrefix(int pid, string title)
    {
        foreach (AutomationElement w in Windows(pid))
        {
            try
            {
                string name = w.Current.Name;
                if (name == title || name.StartsWith(title + " - ", StringComparison.Ordinal)) return w;
            }
            catch { }
        }
        return null;
    }

    public static AutomationElement FindById(AutomationElement scope, string automationId)
    {
        if (scope == null) return null;
        try
        {
            return scope.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        }
        catch (Exception e) { LastError = e.Message; return null; }
    }

    // Menu item by its text, in any window or menu of the process
    public static AutomationElement FindMenuItem(int pid, string name)
    {
        Condition cond = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.NameProperty, name));
        try
        {
            foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, ByPid(pid)))
            {
                AutomationElement item = top.FindFirst(TreeScope.Descendants, cond);
                if (item != null) return item;
            }
        }
        catch (Exception e) { LastError = e.Message; }
        return null;
    }

    public static bool IsEnabled(AutomationElement e)
    {
        try { return e != null && e.Current.IsEnabled; }
        catch { return false; }
    }

    public static bool SetValue(AutomationElement e, string value)
    {
        try
        {
            ((ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // Clicks run on a worker thread: a click that opens a modal window only
    // returns once that window is closed.
    public static void InvokeAsync(AutomationElement e)
    {
        Run(delegate { ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); });
    }

    public static void ExpandAsync(AutomationElement e)
    {
        Run(delegate
        {
            object p;
            if (e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out p))
                ((ExpandCollapsePattern)p).Expand();
            else
                ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        });
    }

    public static void CloseAsync(AutomationElement w)
    {
        Run(delegate { ((WindowPattern)w.GetCurrentPattern(WindowPattern.Pattern)).Close(); });
    }

    static void Run(ThreadStart action)
    {
        Interlocked.Increment(ref pending);
        Thread t = new Thread(delegate()
        {
            try { action(); }
            catch (Exception ex) { LastError = ex.Message; }
            finally { Interlocked.Decrement(ref pending); }
        });
        t.IsBackground = true;
        t.Start();
    }

    // Answers every message box of the process (Yes, else OK, else Cancel)
    // and returns their text.
    public static string[] DismissMessageBoxes(int pid)
    {
        List<string> texts = new List<string>();
        foreach (AutomationElement w in Windows(pid))
        {
            try
            {
                if (w.Current.ClassName != "#32770") continue;
                StringBuilder sb = new StringBuilder(w.Current.Name + ": ");
                foreach (AutomationElement t in w.FindAll(TreeScope.Descendants,
                             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
                    sb.Append(t.Current.Name).Append(' ');
                texts.Add(sb.ToString().Trim());

                AutomationElement button = FindById(w, "6");   // IDYES
                if (button == null) button = FindById(w, "1"); // IDOK
                if (button == null) button = FindById(w, "2"); // IDCANCEL
                if (button != null) InvokeAsync(button);
            }
            catch (Exception e) { LastError = e.Message; }
        }
        return texts.ToArray();
    }
}
'@

# ---- Work folder: build output + PsExec stand-in --------------------------
$exe = (Resolve-Path $ExePath).Path
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }
$work = Join-Path $tempRoot "litedeploy-soak-$Label"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item (Join-Path (Split-Path $exe -Parent) '*') -Destination $work -Recurse -Force
# Never start from a cached Active Directory list of another environment
Remove-Item (Join-Path $work 'adcache.xml') -ErrorAction SilentlyContinue

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$stubSource = Join-Path $PSScriptRoot 'PsExecStub.cs'
& $csc /nologo /target:exe "/out:$(Join-Path $work 'PsExec.exe')" $stubSource
if ($LASTEXITCODE -ne 0) { throw "Could not compile the PsExec stand-in" }

$hosts = @('127.0.0.1', 'localhost') +
         (1..11 | ForEach-Object { 'SOAK-PC{0:D2}' -f $_ }) +
         (1..3  | ForEach-Object { 'SOAK-ERR{0:D2}' -f $_ })

$workExe = Join-Path $work (Split-Path $exe -Leaf)
# Windows are found by their English titles and menu names: English interface whatever the runner language
$env:LITEDEPLOY_LANG = 'en'
# The soak drives "Start Deploy": the standalone deployments (hidden by default)
# must be shown. Older revisions ignore the setting. An existing settings file is kept.
$settingsFile = Join-Path $env:LOCALAPPDATA 'LiteDeploy\settings.xml'
if (-not (Test-Path $settingsFile)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $settingsFile) | Out-Null
    Set-Content -Path $settingsFile -Encoding UTF8 -Value '<?xml version="1.0" encoding="utf-8"?><UserSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"><ShowStandaloneDeployments>true</ShowStandaloneDeployments></UserSettings>'
}
Write-Host "Starting $workExe (cwd $work)"
$proc = Start-Process -FilePath $workExe -WorkingDirectory $work -PassThru
$procId = $proc.Id

# ---- Sampling ---------------------------------------------------------------
$gcCounter = $null
$rows = New-Object System.Collections.Generic.List[object]
$sw = [Diagnostics.Stopwatch]::StartNew()
$script:nextSample = 0.0
$script:lastCpu = 0.0
$script:lastCpuAt = 0.0
$script:crashed = $false
$stats = @{ Deployments = 0; DeployTimeouts = 0; PingRuns = 0; ListEdits = 0; ActionFailures = 0; Dialogs = 0 }
$dialogTexts = New-Object System.Collections.Generic.List[string]

function Test-Alive {
    $proc.Refresh()
    if ($proc.HasExited) {
        if (-not $script:crashed) {
            Write-Host "::error::LiteDeploy exited after $([math]::Round($sw.Elapsed.TotalMinutes,1)) min (exit code $($proc.ExitCode))"
        }
        $script:crashed = $true
        return $false
    }
    return $true
}

function Invoke-SampleIfDue {
    $t = $sw.Elapsed.TotalSeconds
    if ($t -lt $script:nextSample) { return }
    if (-not (Test-Alive)) { return }
    $cpuNow = $proc.TotalProcessorTime.TotalSeconds
    $cpuPercent = [math]::Round(100 * ($cpuNow - $script:lastCpu) / [math]::Max(0.001, $t - $script:lastCpuAt), 2)
    $script:lastCpu = $cpuNow; $script:lastCpuAt = $t
    $gc = $null
    if ($gcCounter) {
        try { $gc = (Get-Counter -Counter $gcCounter -ErrorAction Stop).CounterSamples[0].CookedValue } catch { $gc = $null }
    }
    $row = [pscustomobject]@{
        ElapsedMin   = [math]::Round($sw.Elapsed.TotalMinutes, 2)
        PrivateMB    = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
        WorkingSetMB = [math]::Round($proc.WorkingSet64 / 1MB, 1)
        Handles      = $proc.HandleCount
        GdiObjects   = [SoakUi]::GetGuiResources($proc.Handle, 0)
        UserObjects  = [SoakUi]::GetGuiResources($proc.Handle, 1)
        Threads      = $proc.Threads.Count
        CpuPercent   = $cpuPercent
        GcHeapMB     = if ($null -ne $gc) { [math]::Round($gc / 1MB, 1) } else { '' }
        Deployments  = $stats.Deployments
    }
    $rows.Add($row)
    Write-Host ("{0,7:N1} min  priv {1,7:N1} MB  ws {2,7:N1} MB  handles {3,6}  gdi {4,5}  user {5,5}  thr {6,4}  cpu {7,6:N2} %  gc {8}  deploys {9}" -f `
        $row.ElapsedMin, $row.PrivateMB, $row.WorkingSetMB, $row.Handles, $row.GdiObjects, $row.UserObjects, $row.Threads, $row.CpuPercent, $row.GcHeapMB, $row.Deployments)
    $script:nextSample = $t + $SampleSeconds
}

function Clear-MessageBoxes {
    $texts = [SoakUi]::DismissMessageBoxes($procId)
    foreach ($text in $texts) {
        $stats.Dialogs++
        if ($dialogTexts.Count -lt 20) { $dialogTexts.Add($text) }
        Write-Host "Message box answered: $text"
    }
}

# Waits until $Condition returns true, sampling and answering message boxes
# meanwhile. Returns $false on timeout or when LiteDeploy has exited.
function Wait-Until([scriptblock] $Condition, [double] $TimeoutSeconds, [switch] $KeepMessageBoxes) {
    $limit = $sw.Elapsed.TotalSeconds + $TimeoutSeconds
    while ($sw.Elapsed.TotalSeconds -lt $limit) {
        if (-not (Test-Alive)) { return $false }
        Invoke-SampleIfDue
        if (-not $KeepMessageBoxes) { Clear-MessageBoxes }
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Write-ActionFailure([string] $what) {
    $stats.ActionFailures++
    Write-Host "::warning::Soak action failed: $what $([SoakUi]::LastError)"
}

# ---- Start-up ------------------------------------------------------------------
$startWatch = [Diagnostics.Stopwatch]::StartNew()
$script:main = $null
# The main window exists while the splash screen (a modal dialog shown from its Load event) is
# still open: wait until the splash is gone and the main window accepts input.
if (-not (Wait-Until {
        $script:main = [SoakUi]::FindWindow($procId, 'Lite Deploy')
        $null -ne $script:main -and $null -eq [SoakUi]::FindWindow($procId, 'SplashForm') -and [SoakUi]::IsEnabled($script:main)
    } 120)) {
    if (-not $proc.HasExited) { Stop-Process -Id $procId -Force }
    throw "LiteDeploy main window did not appear (exited: $($proc.HasExited)). Message boxes: $($dialogTexts -join ' | ')"
}
$startupSeconds = [math]::Round($startWatch.Elapsed.TotalSeconds, 1)
Write-Host "Main window after $startupSeconds s"

try {
    $instance = [IO.Path]::GetFileNameWithoutExtension($exe)
    $gcCounter = "\.NET CLR Memory($instance)\# Bytes in all Heaps"
    Get-Counter -Counter $gcCounter -ErrorAction Stop | Out-Null
} catch {
    Write-Host "GC heap counter unavailable: $($_.Exception.Message)"
    $gcCounter = $null
}
$script:lastCpu = $proc.TotalProcessorTime.TotalSeconds
$script:lastCpuAt = $sw.Elapsed.TotalSeconds

$script:mainHandle = $script:main.Current.NativeWindowHandle
function Get-Main {
    $w = [SoakUi]::FromHandle($script:mainHandle)
    if (-not $w) { $w = [SoakUi]::FindWindow($procId, 'Lite Deploy') }
    return $w
}

function Open-ComputerList {
    $button = [SoakUi]::FindById((Get-Main), 'ListEditButton')
    if (-not $button) { Write-ActionFailure 'Computers button not found'; return $null }
    [SoakUi]::InvokeAsync($button)
    $script:designer = $null
    if (-not (Wait-Until { $script:designer = [SoakUi]::FindWindowPrefix($procId, 'Lite Deploy - Computers'); $null -ne $script:designer } 20)) {
        Write-ActionFailure 'computer list window did not open'
        return $null
    }
    return $script:designer
}

function Use-ComputerList($designer) {
    $use = [SoakUi]::FindById($designer, 'Use')
    if (-not $use) { Write-ActionFailure '"Use List" button not found'; return $false }
    [SoakUi]::InvokeAsync($use)
    # The confirmation ("This will clear the Selected Computers List") is answered Yes
    if (-not (Wait-Until { $null -eq [SoakUi]::FindWindowPrefix($procId, 'Lite Deploy - Computers') } 20)) {
        Write-ActionFailure 'computer list window did not close'
        return $false
    }
    $checkAll = [SoakUi]::FindById((Get-Main), 'checkAll')
    if (-not $checkAll) { Write-ActionFailure '"Check All" button not found'; return $false }
    [SoakUi]::InvokeAsync($checkAll)
    [void](Wait-Until { [SoakUi]::Pending -eq 0 } 10)
    return $true
}

# Fill the computer list
$designer = Open-ComputerList
if (-not $designer) { $designer = Open-ComputerList }   # one retry: the first click can come too early
if (-not $designer) { throw "Could not open the computer list (windows: $([SoakUi]::Titles($procId)))" }
$manualPC = [SoakUi]::FindById($designer, 'manualPC')
$manAdd = [SoakUi]::FindById($designer, 'manAdd')
if (-not $manualPC -or -not $manAdd) { throw "Computer list controls not found" }
foreach ($h in $hosts) {
    if (-not [SoakUi]::SetValue($manualPC, $h)) { Write-ActionFailure "set computer name $h"; continue }
    [SoakUi]::InvokeAsync($manAdd)
    [void](Wait-Until { [SoakUi]::Pending -eq 0 } 10)
}
if (-not (Use-ComputerList $designer)) { throw "Could not fill the computer list" }

$arguments = [SoakUi]::FindById((Get-Main), 'arguments')
if (-not $arguments -or -not [SoakUi]::SetValue($arguments, 'cmd /c echo soak-%computername%')) {
    throw "Could not set the command to deploy"
}
Write-Host "Computer list filled with $($hosts.Count) computers"

# ---- Load cycles -------------------------------------------------------------
function Start-Deployment {
    $start = [SoakUi]::FindById((Get-Main), 'startDeploy')
    if (-not $start) { Write-ActionFailure '"Start Deploy" button not found'; return }
    [SoakUi]::InvokeAsync($start)
    # The button is disabled while the deployment runs
    [void](Wait-Until { -not [SoakUi]::IsEnabled($start) } 5)
    if (Wait-Until { [SoakUi]::IsEnabled($start) } 180) {
        $stats.Deployments++
    } else {
        $stats.DeployTimeouts++
        Write-ActionFailure 'deployment did not finish within 180 s'
    }
}

function Start-PingAll {
    # The process reports window opens over the main window: keep the menu bar visible
    $reports = [SoakUi]::FindWindow($procId, 'Lite Deploy - Process Reports')
    if ($reports) { [SoakUi]::Minimize($reports) }
    $main = Get-Main
    if (-not $main) { Write-ActionFailure "main window not found (windows: $([SoakUi]::Titles($procId)))"; return }
    [SoakUi]::Activate($main)
    Start-Sleep -Milliseconds 300
    $menu = [SoakUi]::FindMenuItem($procId, 'Computers')
    if (-not $menu) { Write-ActionFailure 'Computers menu not found'; return }
    # Mouse clicks, like a user: see SoakUi.ClickCenter
    if (-not [SoakUi]::ClickCenter($menu)) { Write-ActionFailure 'click on the Computers menu'; return }
    $script:pingItem = $null
    if (-not (Wait-Until { $script:pingItem = [SoakUi]::FindMenuItem($procId, 'Ping'); $null -ne $script:pingItem } 10)) {
        Write-ActionFailure 'Computers > Ping menu item not found'
        return
    }
    if (-not [SoakUi]::ClickCenter($script:pingItem)) { Write-ActionFailure 'click on Computers > Ping'; return }
    $script:pingWindow = $null
    if (-not (Wait-Until { $script:pingWindow = [SoakUi]::FindWindow($procId, 'Lite Deploy - Ping'); $null -ne $script:pingWindow } 20)) {
        Write-ActionFailure 'ping window did not open'
        return
    }
    $done = [SoakUi]::FindById($script:pingWindow, 'done')
    if (-not (Wait-Until { [SoakUi]::IsEnabled($done) } 180)) { Write-ActionFailure 'ping did not finish within 180 s' }
    [SoakUi]::CloseAsync($script:pingWindow)
    if (Wait-Until { $null -eq [SoakUi]::FindWindow($procId, 'Lite Deploy - Ping') } 20) {
        $stats.PingRuns++
    } else {
        Write-ActionFailure 'ping window did not close'
    }
}

function Edit-ComputerList {
    $d = Open-ComputerList
    if ($d -and (Use-ComputerList $d)) { $stats.ListEdits++ }
}

$cycle = 0
$idleCycles = 0
$script:stalled = $false
while ($sw.Elapsed.TotalMinutes -lt $DurationMinutes -and -not $script:crashed) {
    $cycleStart = $sw.Elapsed.TotalSeconds
    $cycle++

    $before = $stats.Deployments
    Start-Deployment
    # A run where the load stopped measures nothing: fail it instead of writing a misleading summary
    if ($stats.Deployments -eq $before) { $idleCycles++ } else { $idleCycles = 0 }
    if ($idleCycles -ge 3) {
        Write-Host "::error::No deployment completed in 3 cycles in a row: the UI of LiteDeploy (or the UI automation) stopped responding"
        $script:stalled = $true
        break
    }
    if ($cycle % 3 -eq 0) { Start-PingAll }
    if ($cycle % 5 -eq 0) { Edit-ComputerList }

    [void](Wait-Until { $sw.Elapsed.TotalSeconds -ge $cycleStart + $CycleSeconds -or $sw.Elapsed.TotalMinutes -ge $DurationMinutes } ($CycleSeconds + 1))
}
Invoke-SampleIfDue

if (-not $proc.HasExited) { Stop-Process -Id $procId -Force }
$rows | Export-Csv -Path $OutCsv -NoTypeInformation -Encoding UTF8

# ---- Summary -------------------------------------------------------------------
function Get-Slope($xs, $ys) {
    $n = $xs.Count
    if ($n -lt 2) { return 0 }
    $mx = ($xs | Measure-Object -Average).Average
    $my = ($ys | Measure-Object -Average).Average
    $num = 0.0; $den = 0.0
    for ($i = 0; $i -lt $n; $i++) {
        $num += ($xs[$i] - $mx) * ($ys[$i] - $my)
        $den += ($xs[$i] - $mx) * ($xs[$i] - $mx)
    }
    if ($den -eq 0) { return 0 }
    return $num / $den
}

$metrics = 'PrivateMB', 'WorkingSetMB', 'Handles', 'GdiObjects', 'UserObjects', 'Threads', 'GcHeapMB', 'CpuPercent'
$steady = @($rows | Where-Object { $_.ElapsedMin -ge $WarmupMinutes })
$short = $steady | Select-Object -First 1
$last = $rows | Select-Object -Last 1

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("### LiteDeploy soak - $Label")
[void]$md.AppendLine()
[void]$md.AppendLine("Duration: $([math]::Round($last.ElapsedMin,1)) min" + $(if ($script:crashed) { ' - **LiteDeploy exited before the end**' } elseif ($script:stalled) { ' - **load stopped: UI not responding, run not usable**' } else { '' }))
[void]$md.AppendLine("Load: $($hosts.Count) computers, $($stats.Deployments) deployments ($($stats.DeployTimeouts) timed out), $($stats.PingRuns) ping runs, $($stats.ListEdits) list edits, $($stats.Dialogs) message boxes answered, $($stats.ActionFailures) failed UI actions.")
if ($steady.Count -gt 0) {
    [void]$md.AppendLine("Main window shown after $startupSeconds s. Average CPU after warm-up: $([math]::Round((@($steady | Measure-Object CpuPercent -Average).Average), 2)) % of one core.")
}
[void]$md.AppendLine()
[void]$md.AppendLine("| Counter | At $WarmupMinutes min (short run) | At end | Growth per hour (after warm-up) |")
[void]$md.AppendLine("|---|---:|---:|---:|")
foreach ($m in $metrics) {
    $pts = @($steady | Where-Object { $_.$m -ne '' -and $null -ne $_.$m })
    if ($pts.Count -lt 2) { continue }
    $slope = Get-Slope ([double[]]($pts | ForEach-Object { $_.ElapsedMin })) ([double[]]($pts | ForEach-Object { [double]$_.$m }))
    [void]$md.AppendLine(("| {0} | {1} | {2} | {3:+0.0;-0.0;0} |" -f $m, $short.$m, $last.$m, ($slope * 60)))
}
if ($dialogTexts.Count -gt 0) {
    [void]$md.AppendLine()
    [void]$md.AppendLine("Message boxes (first $($dialogTexts.Count)):")
    foreach ($text in ($dialogTexts | Select-Object -Unique)) { [void]$md.AppendLine("- $text") }
}
$summary = $md.ToString()
Set-Content -Path $SummaryPath -Value $summary -Encoding UTF8
Write-Host $summary
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary }
if ($script:crashed) { exit 2 }
if ($script:stalled) { exit 4 }
if ($stats.Deployments -eq 0) { Write-Host "::error::No deployment completed: the load did not run."; exit 3 }
