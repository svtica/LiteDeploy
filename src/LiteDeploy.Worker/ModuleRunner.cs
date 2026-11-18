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
//          Parameters (contract v2): LiteDeploy writes <Name>.params next to the
//          script (administrators and SYSTEM only); the worker reads it, deletes
//          it at once and gives the values declared in the header to the module
//          process only, as LITEDEPLOY_PARAM_<Name>; secret values are masked in
//          the messages.
//          Install: "-Action Install" runs only for a module whose signed header
//          declares "# Install: yes" (InstallPolicy).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
        public const string LiteSuiteThumbprint = "D21B5D39EA4C7BDC69C94C86FC4602E8A1CA15B4";

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

        public static string ParametersPath(UpdatePackage module)
        {
            return Path.Combine(ModulesDir, module.ModuleName + ModuleParameters.FileExtension);
        }

        /// <summary>
        /// The values of the parameter file of the module, which is deleted at once (also when it
        /// cannot be read): empty when there is none.
        /// </summary>
        public static Dictionary<string, string> TakeParameters(UpdatePackage module)
        {
            string path = ParametersPath(module);
            try
            {
                if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return ModuleParameters.ParseFile(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception)
            {
                // Unreadable: the required parameters are then reported missing (no content in the message)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteParameters(module);
            }
        }

        /// <summary>Deletes the parameter file of the module, if any (end of the run, also on failure).</summary>
        public static void DeleteParameters(UpdatePackage module)
        {
            try
            {
                string path = ParametersPath(module);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Environment of the module process: any LITEDEPLOY_PARAM_ variable inherited is removed,
        /// then the values of this module are set. Pure logic, unit-tested.
        /// </summary>
        public static void ApplyEnvironment(StringDictionary target, IDictionary<string, string> environment)
        {
            List<string> inherited = new List<string>();
            foreach (string key in target.Keys)
                if (key.StartsWith(ModuleParameters.EnvironmentPrefix, StringComparison.OrdinalIgnoreCase)) inherited.Add(key);
            foreach (string key in inherited) target.Remove(key);
            if (environment == null) return;
            foreach (KeyValuePair<string, string> v in environment) target[v.Key] = v.Value;
        }

        /// <summary>The result with every secret value masked (message and versions).</summary>
        public static ModuleResult MaskSecrets(ModuleResult r, IList<string> secrets)
        {
            if (r == null || secrets == null || secrets.Count == 0) return r;
            r.Message = ModuleParameters.MaskSecrets(r.Message, secrets);
            r.Installed = ModuleParameters.MaskSecrets(r.Installed, secrets);
            r.Available = ModuleParameters.MaskSecrets(r.Available, secrets);
            return r;
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

        /// <summary>The actions of the module contract: Check, Update, Install (only when the header declares "# Install: yes").</summary>
        public static bool IsValidAction(string action)
        {
            return action == "Check" || action == "Update" || action == InstallPolicy.ModuleAction;
        }

        /// <summary>
        /// Whether the module script on the computer declares "# Install: yes" (first 20 lines); false
        /// when it is missing or unreadable. Run checks it again after the signature, before Install.
        /// </summary>
        public static bool DeclaresInstall(UpdatePackage module)
        {
            try
            {
                string script = ScriptPath(module);
                return File.Exists(script) && InstallPolicy.DeclaresInstall(File.ReadLines(script).Take(20).ToList());
            }
            catch (Exception) { return false; }
        }

        /// <summary>Runs "-Action Check", "-Action Update" or "-Action Install"; a refused or missing script is reported as an error.</summary>
        public static ModuleResult Run(UpdatePackage module, string action, bool allowUnsigned, TimeSpan timeout)
        {
            return Run(module, action, allowUnsigned, timeout, null);
        }

        /// <summary>
        /// With <paramref name="parameters"/> (TakeParameters): the values of the parameters declared
        /// in the header; a required one without value: error, the module is not started.
        /// </summary>
        public static ModuleResult Run(UpdatePackage module, string action, bool allowUnsigned, TimeSpan timeout, IDictionary<string, string> parameters)
        {
            if (!IsValidAction(action)) return Error("Unknown module action: " + action);
            string script = ScriptPath(module);
            if (!File.Exists(script))
                return Error("Module script not found: " + script);

            AuthenticodeResult running = AuthenticodeVerifier.Verify(Assembly.GetExecutingAssembly().Location);
            AuthenticodeResult signature = AuthenticodeVerifier.Verify(script);
            UpdateSignatureVerdict verdict = UpdateSignaturePolicy.Evaluate(signature, running,
                UpdateSignaturePolicy.ExpectedThumbprints(LiteSuiteThumbprint, running));
            if (!MayRun(verdict, allowUnsigned))
                return Error("Module refused: " + verdict + " (" + signature.Status + ")");

            List<string> header = File.ReadLines(script).Take(20).ToList();
            // Install: only a module whose (signed) header declares it
            if (action == InstallPolicy.ModuleAction && !InstallPolicy.DeclaresInstall(header))
                return Error(InstallPolicy.NotSupportedMessage(module.ModuleName));

            // Parameters declared in the header of the (signed) script; values from LiteDeploy or the defaults
            List<ModuleParameter> declared = ModuleParameters.ParseHeader(header);
            Dictionary<string, string> environment;
            List<string> secrets;
            string missing = ModuleParameters.Resolve(declared, parameters, out environment, out secrets);
            if (missing != null) return Error(missing);

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
            // Values for this process only, never on its command line
            ApplyEnvironment(psi.EnvironmentVariables, environment);

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
                return MaskSecrets(Interpret(p.ExitCode, stdout.IsCompleted ? stdout.Result : "", stderr.IsCompleted ? stderr.Result : ""), secrets);
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
