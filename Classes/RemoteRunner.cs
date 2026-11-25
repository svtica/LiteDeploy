// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Starts LiteDeploy.Worker.exe as SYSTEM on a computer, by one of two
//          methods (App > Options):
//          - WinRM (default, the method of an earlier update script): the
//            worker is copied to %windir%\LiteDeploy (ADMIN$, administrators
//            only), a PowerShell session (WinRM) registers a one-time
//            scheduled task as SYSTEM in \LiteDeploy\, starts it, relays the
//            messages the worker writes to %windir%\LiteDeploy\work, then
//            deletes the task and the file. Leftover tasks of a stopped run are
//            deleted at the next run. No password is stored on the computer.
//          - PsExec (-s -c -f): the worker is copied and started by PsExec.
//          Either way the process returned writes the worker messages on its
//          standard output and ends with the worker's exit code.
//          The administrator's own computer (WinRM method): no WinRM session
//          to itself (loopback, refused or not configured on most
//          workstations). The worker is copied to %windir%\LiteDeploy and
//          the same scheduled task is registered by the local PowerShell,
//          which needs LiteDeploy to run as administrator; otherwise the run
//          fails at once and says so.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using LiteSuite;

namespace LiteDeploy
{
    internal static class RemoteRunner
    {
        public const String WinRm = "WinRM";
        public const String PsExec = "PsExec";

        public static readonly String[] Methods = { WinRm, PsExec };

        public static String Method
        {
            get { return UserSettings.Current.RemoteMethod == PsExec ? PsExec : WinRm; }
        }

        /// <summary>Checks what the method needs; null when everything is there.</summary>
        public static String Prerequisite()
        {
            if (!File.Exists(UpdateRunner.WorkerPath)) return UpdateRunner.WorkerFileName + " not found next to LiteDeploy.exe";
            if (Method == PsExec && PsExecLocator.Path == null) return "PsExec not found";
            return null;
        }

        /// <summary>Starts the worker; the caller reads stdout and stderr and disposes the process.</summary>
        public static Process Start(String computer, String workerArguments, StoredCredential credential, int timeoutMinutes, PsExecOptions psExecOptions)
        {
            if (Method == PsExec)
            {
                ProcessStartInfo psi = new ProcessStartInfo(PsExecLocator.Path, PsExecCommandBuilder.BuildArguments(psExecOptions));
                Redirect(psi, OemEncoding());
                return Process.Start(psi);
            }

            bool local = IsLocalComputer(computer);
            if (local && !IsElevated())
                throw new InvalidOperationException(Localization.F("RemoteRunner.LocalNotElevated",
                    "{0} is this computer: run LiteDeploy as administrator to check or update it (WinRM does not connect to its own computer).", computer.Trim()));
            if (local) CopyWorkerLocally(); else CopyWorker(computer, credential);
            ProcessStartInfo ps = new ProcessStartInfo(PowerShellPath(),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildScript(computer, workerArguments, credential != null && !local, timeoutMinutes, local))));
            Redirect(ps, Encoding.UTF8);
            ps.RedirectStandardInput = true;
            Process p = Process.Start(ps);
            // The credential goes through stdin, never on a command line (not used on this computer)
            if (credential != null && !local)
            {
                p.StandardInput.WriteLine(credential.UserName);
                p.StandardInput.WriteLine(credential.Password);
            }
            p.StandardInput.Close();
            return p;
        }

        static void Redirect(ProcessStartInfo psi, Encoding errorEncoding)
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            // The worker writes UTF-8; PsExec writes its own messages in the OEM code page
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = errorEncoding;
        }

        /// <summary>Code page of console programs (850 in French): PsExec's messages.</summary>
        public static Encoding OemEncoding()
        {
            try { return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch (Exception) { return Encoding.Default; }
        }

        static String PowerShellPath()
        {
            String path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            return File.Exists(path) ? path : "powershell.exe";
        }

        /// <summary>Copies the worker to \\computer\ADMIN$\LiteDeploy, unless the same file is already there.</summary>
        static void CopyWorker(String computer, StoredCredential credential)
        {
            String admin = @"\\" + computer.Trim() + @"\ADMIN$";
            String target = Path.Combine(admin, "LiteDeploy", UpdateRunner.WorkerFileName);
            try
            {
                using (new NetworkShare(admin, credential))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target) && SameContent(UpdateRunner.WorkerPath, target)) return;
                    File.Copy(UpdateRunner.WorkerPath, target, true);
                }
            }
            catch (Exception ex)
            {
                throw new IOException(Localization.F("RemoteRunner.CopyFailed", "Could not copy the worker to {0}: {1}", target, ex.Message), ex);
            }
        }

        /// <summary>
        /// Whether <paramref name="computer"/> names this computer: its NetBIOS name, its name followed by a
        /// domain suffix, its DNS host name, localhost, "." or a loopback address. Unit-tested.
        /// </summary>
        public static bool IsLocalComputer(String computer)
        {
            return IsLocalComputer(computer, Environment.MachineName, SafeHostName());
        }

        internal static bool IsLocalComputer(String computer, String machineName, String dnsHostName)
        {
            String c = (computer ?? "").Trim().TrimStart('\\');
            if (c == ".") return true;
            c = c.TrimEnd('.');
            if (c.Length == 0) return false;
            if (c.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress ip;
            if (IPAddress.TryParse(c, out ip)) return IPAddress.IsLoopback(ip);
            if (!String.IsNullOrEmpty(machineName)
                && (c.Equals(machineName, StringComparison.OrdinalIgnoreCase) || c.StartsWith(machineName + ".", StringComparison.OrdinalIgnoreCase)))
                return true;
            return !String.IsNullOrEmpty(dnsHostName) && c.Equals(dnsHostName.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        }

        static String SafeHostName()
        {
            try { return Dns.GetHostName(); } catch (Exception) { return null; }
        }

        /// <summary>LiteDeploy runs with the administrator token (needed to register the SYSTEM task on this computer).</summary>
        static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        /// <summary>Copies the worker to %windir%\LiteDeploy of this computer (no ADMIN$ loopback), unless the same file is already there.</summary>
        static void CopyWorkerLocally()
        {
            String target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiteDeploy", UpdateRunner.WorkerFileName);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target) && SameContent(UpdateRunner.WorkerPath, target)) return;
                File.Copy(UpdateRunner.WorkerPath, target, true);
            }
            catch (Exception ex)
            {
                throw new IOException(Localization.F("RemoteRunner.CopyFailed", "Could not copy the worker to {0}: {1}", target, ex.Message), ex);
            }
        }

        static bool SameContent(String a, String b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using (SHA256 sha = SHA256.Create())
            using (FileStream fa = File.OpenRead(a))
            using (FileStream fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return sha.ComputeHash(fa).SequenceEqual(sha.ComputeHash(fb));
        }

        /// <summary>A PowerShell single-quoted literal: the value is data, never code.</summary>
        public static String Quote(String value)
        {
            return "'" + (value ?? "").Replace("'", "''").Replace("\u2018", "''").Replace("\u2019", "''") + "'";
        }

        /// <summary>The script run by the local PowerShell (Windows PowerShell 5.1).</summary>
        public static String BuildScript(String computer, String workerArguments, bool readCredential, int timeoutMinutes)
        {
            return BuildScript(computer, workerArguments, readCredential, timeoutMinutes, false);
        }

        /// <summary>The script; <paramref name="local"/>: this computer, the same steps without a WinRM session.</summary>
        public static String BuildScript(String computer, String workerArguments, bool readCredential, int timeoutMinutes, bool local)
        {
            String id = Guid.NewGuid().ToString("N").Substring(0, 12);
            return Script
                .Replace("%COMPUTER%", Quote(computer.Trim()))
                .Replace("%ARGS%", Quote(workerArguments))
                .Replace("%ID%", Quote(id))
                .Replace("%TIMEOUT%", Math.Max(1, timeoutMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("%CRED%", readCredential ? "$true" : "$false")
                .Replace("%LOCAL%", local ? "$true" : "$false");
        }

        // ASCII only. Exit codes: the worker's, or 1001 connection, 1002 start, 1003 timeout.
        const String Script = @"
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$computer = %COMPUTER%; $workerArgs = %ARGS%; $id = %ID%; $timeout = %TIMEOUT%
function Fail($code, $text) { [Console]::Error.WriteLine($text); exit $code }
$cred = $null
if (%CRED%) {
    $u = [Console]::In.ReadLine(); $p = [Console]::In.ReadLine()
    $cred = New-Object System.Management.Automation.PSCredential($u, (ConvertTo-SecureString $p -AsPlainText -Force))
    $p = $null
}
# This computer: the same steps, run by this PowerShell (no WinRM session to itself)
$s = $null; $on = @{}
if (-not %LOCAL%) {
    $opt = New-PSSessionOption -OpenTimeout 15000 -OperationTimeout 120000
    try {
        if ($cred) { $s = New-PSSession -ComputerName $computer -Credential $cred -SessionOption $opt }
        else { $s = New-PSSession -ComputerName $computer -SessionOption $opt }
    } catch { Fail 1001 ('WinRM: ' + $_.Exception.Message) }
    $on = @{ Session = $s }
}
try {
    try {
        Invoke-Command @on -ArgumentList $id, $workerArgs -ScriptBlock {
            param($id, $a)
            $ErrorActionPreference = 'Stop'
            $dir = Join-Path $env:windir 'LiteDeploy'
            $exe = Join-Path $dir 'LiteDeploy.Worker.exe'
            $work = Join-Path $dir 'work'
            # Tasks left by a stopped run
            Get-ScheduledTask -TaskPath '\LiteDeploy\' -ErrorAction SilentlyContinue |
                Where-Object { $_.TaskName -like 'Worker-*' -and $_.State -ne 'Running' } |
                ForEach-Object {
                    Unregister-ScheduledTask -TaskName $_.TaskName -TaskPath '\LiteDeploy\' -Confirm:$false -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath (Join-Path $work ('run-' + $_.TaskName.Substring(7) + '.out')) -Force -ErrorAction SilentlyContinue
                }
            if (-not (Test-Path -LiteralPath $exe)) { throw ""$exe is missing right after the copy: an antivirus probably removed it."" }
            New-Item -ItemType Directory -Force -Path $work | Out-Null
            $out = Join-Path $work ""run-$id.out""
            $action = New-ScheduledTaskAction -Execute $exe -Argument ($a + ' --output ""' + $out + '""') -WorkingDirectory $dir
            $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 4)
            Register-ScheduledTask -TaskName ""Worker-$id"" -TaskPath '\LiteDeploy\' -Action $action -Principal $principal -Settings $settings -Force | Out-Null
            Start-ScheduledTask -TaskName ""Worker-$id"" -TaskPath '\LiteDeploy\'
        }
    } catch { Fail 1002 ($computer + ': ' + $_.Exception.Message) }

    $offset = 0; $deadline = (Get-Date).AddMinutes($timeout); $started = Get-Date
    while ($true) {
        Start-Sleep -Seconds 2
        $r = Invoke-Command @on -ArgumentList $id, $offset -ScriptBlock {
            param($id, $offset)
            $out = Join-Path $env:windir ""LiteDeploy\work\run-$id.out""
            $bytes = New-Object byte[] 0
            if (Test-Path -LiteralPath $out) {
                $fs = [System.IO.File]::Open($out, 'Open', 'Read', 'ReadWrite, Delete')
                try {
                    if ($fs.Length -gt $offset) {
                        [void]$fs.Seek($offset, 'Begin')
                        $bytes = New-Object byte[] ($fs.Length - $offset)
                        $n = $fs.Read($bytes, 0, $bytes.Length)
                        if ($n -lt $bytes.Length) { [Array]::Resize([ref]$bytes, $n) }
                    }
                } finally { $fs.Dispose() }
            }
            # Whole lines only: a line being written is read at the next pass
            $last = [Array]::LastIndexOf($bytes, [byte]10)
            $text = ''
            if ($last -ge 0) { $text = [System.Text.Encoding]::UTF8.GetString($bytes, 0, $last + 1); $offset += $last + 1 }
            $info = Get-ScheduledTaskInfo -TaskName ""Worker-$id"" -TaskPath '\LiteDeploy\'
            $task = Get-ScheduledTask -TaskName ""Worker-$id"" -TaskPath '\LiteDeploy\'
            $done = ($task.State -ne 'Running' -and $task.State -ne 'Queued' -and $info.LastRunTime.Year -gt 2000)
            New-Object psobject -Property @{ Text = $text; Offset = $offset; Done = $done; Result = $info.LastTaskResult }
        }
        if ($r.Text) { [Console]::Out.Write($r.Text); [Console]::Out.Flush() }
        $offset = $r.Offset
        if ($r.Done) { $code = [int]$r.Result; break }
        if ((Get-Date) -gt $deadline) { $code = 1003; break }
        # Never started (task refused by a policy, for example)
        if (((Get-Date) - $started).TotalMinutes -gt 2 -and $offset -eq 0 -and $r.Result -eq 267011) { $code = 1002; [Console]::Error.WriteLine('The scheduled task did not start.'); break }
    }
} finally {
    if ($s -or %LOCAL%) {
        Invoke-Command @on -ArgumentList $id -ErrorAction SilentlyContinue -ScriptBlock {
            param($id)
            Stop-ScheduledTask -TaskName ""Worker-$id"" -TaskPath '\LiteDeploy\' -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName ""Worker-$id"" -TaskPath '\LiteDeploy\' -Confirm:$false -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Join-Path $env:windir ""LiteDeploy\work\run-$id.out"") -Force -ErrorAction SilentlyContinue
        }
        if ($s) { Remove-PSSession $s -ErrorAction SilentlyContinue }
    }
}
if ($code -eq 1003) { [Console]::Error.WriteLine('Timed out') }
exit $code
";

        /// <summary>Readable failure when the worker sent no message.</summary>
        public static String DescribeFailure(String method, int exitCode, String text)
        {
            String last = (text ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !PsExecCommandLine.IsStatusLine(l) && !l.StartsWith("PsExec v", StringComparison.OrdinalIgnoreCase)
                            && l.IndexOf("Sysinternals", StringComparison.OrdinalIgnoreCase) < 0 && !l.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase)
                            && !l.StartsWith("+ ", StringComparison.Ordinal) && !l.StartsWith("At line:", StringComparison.OrdinalIgnoreCase))
                .LastOrDefault();
            String hint = null;
            if (method == PsExec && exitCode == 2)
                hint = Localization.T("RemoteRunner.PsExecFileNotFound", "PsExec copied the worker but Windows could not start it (an antivirus often removes an executable dropped in ADMIN$). Try the WinRM method (App > Options).");
            return method + " " + Localization.T("RemoteRunner.ExitCode", "exit code") + " " + exitCode + (last != null ? ": " + last : "") + (hint != null ? " " + hint : "");
        }
    }
}
