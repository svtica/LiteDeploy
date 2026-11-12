// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Runs an update module on the computer, as SYSTEM: a PowerShell
//          script that LiteDeploy copied to %windir%\LiteDeploy\modules (only
//          administrators can write there). The script runs only when its
//          Authenticode signature is valid and from the LiteSuite publisher or
//          the signer of this worker; an unsigned script runs only when
//          LiteDeploy allows it explicitly (--allow-unsigned-modules). Contract
//          of a module: docs/modules.md.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using LiteSuite;

namespace LiteDeploy.Worker
{
    internal sealed class ModuleResult
    {
        public string Status;      // WorkerProtocol.Status*
        public string Installed;
        public string Available;
        public string Message;
        public int ExitCode;
    }

    internal static class ModuleRunner
    {
        /// <summary>Publisher of the LiteSuite releases (same pin as LiteTask).</summary>
        public const string LiteSuiteThumbprint = "EC2B412C4025C2F1C8DB512CEC66632E65C017F0";

        /// <summary>Exit code of an update that needs a restart (msiexec convention).</summary>
        public const int RestartRequired = 3010;

        public static string ModulesDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiteDeploy", "modules"); }
        }

        public static string ScriptPath(UpdatePackage module)
        {
            return Path.Combine(ModulesDir, module.ModuleName + ".ps1");
        }

        /// <summary>
        /// Whether a script with this signature verdict may run as SYSTEM. Accepted: signed by an
        /// expected publisher. Unsigned (whatever the worker): only when explicitly allowed. A
        /// tampered script, another publisher or an invalid signature: never.
        /// </summary>
        public static bool MayRun(UpdateSignatureVerdict verdict, bool allowUnsigned)
        {
            switch (verdict)
            {
                case UpdateSignatureVerdict.Accepted:
                    return true;
                case UpdateSignatureVerdict.AcceptedUnsignedDevBuild:
                case UpdateSignatureVerdict.RejectedUnsigned:
                    return allowUnsigned;
                default:
                    return false;
            }
        }

        /// <summary>Runs "-Action Check" or "-Action Update"; a refused or missing script is reported as an error.</summary>
        public static ModuleResult Run(UpdatePackage module, string action, bool allowUnsigned, TimeSpan timeout)
        {
            string script = ScriptPath(module);
            if (!File.Exists(script))
                return Error("Module script not found: " + script);

            AuthenticodeResult running = AuthenticodeVerifier.Verify(Assembly.GetExecutingAssembly().Location);
            AuthenticodeResult signature = AuthenticodeVerifier.Verify(script);
            UpdateSignatureVerdict verdict = UpdateSignaturePolicy.Evaluate(signature, running,
                UpdateSignaturePolicy.ExpectedThumbprints(LiteSuiteThumbprint, running));
            if (!MayRun(verdict, allowUnsigned))
                return Error("Module refused: " + verdict + " (" + signature.Status + ")");

            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            // The signature was checked above: the execution policy is not what protects this run
            ProcessStartInfo psi = new ProcessStartInfo(powershell,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -Action " + action)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = ModulesDir
            };

            using (Process p = Process.Start(psi))
            {
                Task<string> stdout = p.StandardOutput.ReadToEndAsync();
                Task<string> stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try { p.Kill(); } catch (Exception) { }
                    return Error("Module timed out after " + timeout.TotalMinutes + " min");
                }
                p.WaitForExit();
                Task.WaitAll(new Task[] { stdout, stderr }, 10000);
                return Interpret(p.ExitCode, stdout.IsCompleted ? stdout.Result : "", stderr.IsCompleted ? stderr.Result : "");
            }
        }

        /// <summary>The last "module" message of the script, its exit code and its errors. Pure logic, unit-tested.</summary>
        public static ModuleResult Interpret(int exitCode, string output, string errors)
        {
            WorkerMessage m = WorkerProtocol.ParseAll(output).LastOrDefault(x => x.Type == WorkerProtocol.TypeModule);
            ModuleResult r = new ModuleResult { ExitCode = exitCode };
            if (m != null)
            {
                r.Status = m["status"];
                r.Installed = m["installed"];
                r.Available = m["available"];
                r.Message = m["message"];
            }
            bool knownStatus = r.Status == WorkerProtocol.StatusUpToDate || r.Status == WorkerProtocol.StatusOutdated
                               || r.Status == WorkerProtocol.StatusNotInstalled || r.Status == WorkerProtocol.StatusError;
            if ((exitCode != 0 && exitCode != RestartRequired) || !knownStatus)
            {
                r.Status = WorkerProtocol.StatusError;
                string err = (errors ?? "").Trim();
                if (err.Length > 300) err = err.Substring(err.Length - 300);
                r.Message = (r.Message ?? (knownStatus ? "" : "No result from the module")) + (exitCode != 0 ? " (exit code " + exitCode + ")" : "")
                            + (err.Length > 0 ? ": " + err : "");
            }
            else if (exitCode == RestartRequired)
            {
                r.Message = ((r.Message ?? "") + " Restart required.").Trim();
            }
            return r;
        }

        static ModuleResult Error(string message)
        {
            return new ModuleResult { Status = WorkerProtocol.StatusError, Message = message, ExitCode = -1 };
        }
    }
}
