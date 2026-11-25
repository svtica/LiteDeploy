// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Runs LiteDeploy.Worker.exe on one computer as SYSTEM, through WinRM
//          and a one-time scheduled task (default) or PsExec (RemoteRunner):
//          no stored password on the computer. Returns the worker messages,
//          or why the computer could not be reached.
//          Also: where the package list and the worker are.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LiteSuite;

namespace LiteDeploy
{
    internal sealed class UpdateRunResult
    {
        public List<WorkerMessage> Messages = new List<WorkerMessage>();
        /// <summary>Why the run failed when the worker could not report (null otherwise).</summary>
        public String Failure;
    }

    internal static class UpdateRunner
    {
        public const String WorkerFileName = "LiteDeploy.Worker.exe";
        /// <summary>Minutes per winget call on the computer.</summary>
        public const int WingetTimeoutMinutes = 15;

        static String AppDir
        {
            get { return Path.GetDirectoryName(typeof(UpdateRunner).Assembly.Location); }
        }

        public static String WorkerPath
        {
            get { return Path.Combine(AppDir, WorkerFileName); }
        }

        /// <summary>The package list of the user (%LOCALAPPDATA%\LiteDeploy\packages.txt).</summary>
        public static String UserPackagesPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "packages.txt"); }
        }

        /// <summary>The example list shipped with LiteDeploy (updates\packages.txt).</summary>
        public static String DefaultPackagesPath
        {
            get { return Path.Combine(AppDir, "updates", "packages.txt"); }
        }

        /// <summary>The user's list when it exists, else the shipped one.</summary>
        public static List<UpdatePackage> LoadPackages()
        {
            return UpdatePackage.ParseFile(File.Exists(UserPackagesPath) ? UserPackagesPath : DefaultPackagesPath);
        }

        /// <summary>Saves the user's package list (%LOCALAPPDATA%\LiteDeploy\packages.txt), with its format explained on top.</summary>
        public static void SavePackages(IEnumerable<UpdatePackage> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# LiteDeploy - software updates: winget packages to keep up to date.");
            sb.AppendLine("# One package per line:  winget Id | processes to close (without .exe, comma-separated) | label");
            foreach (UpdatePackage p in list)
                sb.AppendLine(p.Id + " | " + String.Join(", ", p.Processes) + " | " + (p.Label ?? ""));
            Directory.CreateDirectory(Path.GetDirectoryName(UserPackagesPath));
            File.WriteAllText(UserPackagesPath, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Worker arguments: the command, then one --package per package.</summary>
        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages)
        {
            return BuildWorkerArguments(command, packages, false);
        }

        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages, bool allowUnsignedModules)
        {
            return BuildWorkerArguments(command, packages, allowUnsignedModules, null);
        }

        /// <summary>With <paramref name="cacheShare"/>: the shared cache of the installers of the modules (--cache).</summary>
        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages, bool allowUnsignedModules, String cacheShare)
        {
            return BuildWorkerArguments(command, packages, allowUnsignedModules, cacheShare, false, false);
        }

        /// <summary>
        /// Server guard (ServerPolicy): <paramref name="includeServers"/> adds --allow-servers
        /// (UserSettings.IncludeServers); <paramref name="allowDomainControllers"/> adds
        /// --allow-domain-controllers, only together with --allow-servers.
        /// </summary>
        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages, bool allowUnsignedModules, String cacheShare,
            bool includeServers, bool allowDomainControllers)
        {
            return BuildWorkerArguments(command, packages, allowUnsignedModules, cacheShare, includeServers, allowDomainControllers, 0, false);
        }

        /// <summary>
        /// Upgrade with <paramref name="warnSeconds"/> &gt; 0: the users are warned that long before their applications are
        /// closed (--warn-close), and may postpone with <paramref name="allowPostpone"/> (--allow-postpone). See CloseWarning.
        /// </summary>
        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages, bool allowUnsignedModules, String cacheShare,
            bool includeServers, bool allowDomainControllers, int warnSeconds, bool allowPostpone)
        {
            StringBuilder sb = new StringBuilder(command);
            foreach (UpdatePackage p in packages)
                sb.Append(" --package ").Append(PsExecCommandLine.QuoteArgument(p.ToArgument(), true));
            sb.Append(" --timeout ").Append(WingetTimeoutMinutes);
            if (allowUnsignedModules && packages.Any(p => p.IsModule)) sb.Append(" --allow-unsigned-modules");
            if (!String.IsNullOrWhiteSpace(cacheShare) && packages.Any(p => p.IsModule))
                sb.Append(" --cache ").Append(PsExecCommandLine.QuoteArgument(cacheShare.Trim().TrimEnd('\\'), true));
            if (includeServers)
            {
                sb.Append(' ').Append(ServerPolicy.AllowServersOption);
                if (allowDomainControllers) sb.Append(' ').Append(ServerPolicy.AllowDomainControllersOption);
            }
            int warn = CloseWarning.ClampSeconds(warnSeconds);
            if (command == "upgrade" && warn > 0)
            {
                sb.Append(' ').Append(CloseWarning.WarnOption).Append(' ').Append(warn);
                if (allowPostpone) sb.Append(' ').Append(CloseWarning.PostponeOption);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Explicit Install action: for each package of the list (in its order), the computers among
        /// <paramref name="computers"/> where it may be installed: not paused, last check
        /// "notinstalled", and for a module, a header that declares "# Install: yes"
        /// (<paramref name="installableModules"/>: names of those modules). Packages with no such
        /// computer are left out. Pure logic, unit-tested.
        /// </summary>
        public static List<KeyValuePair<UpdatePackage, List<String>>> InstallCandidates(IEnumerable<String> computers, IEnumerable<UpdatePackage> packages,
            Func<String, ComputerUpdateState> find, ICollection<String> installableModules)
        {
            List<KeyValuePair<UpdatePackage, List<String>>> result = new List<KeyValuePair<UpdatePackage, List<String>>>();
            List<String> list = computers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (UpdatePackage p in packages)
            {
                bool declares = p.IsModule && installableModules != null
                                && installableModules.Any(n => String.Equals(n, p.ModuleName, StringComparison.OrdinalIgnoreCase));
                List<String> targets = new List<String>();
                foreach (String computer in list)
                {
                    ComputerUpdateState c = find(computer);
                    if (c == null || c.Paused) continue;
                    PackageUpdateState s = c.Find(p.Id);
                    if (s != null && InstallPolicy.CanOffer(p, s.Status, declares)) targets.Add(computer);
                }
                if (targets.Count > 0) result.Add(new KeyValuePair<UpdatePackage, List<String>>(p, targets));
            }
            return result;
        }

        /// <summary>
        /// Uninstall mode: for each package of the list (in its order), the computers among
        /// <paramref name="computers"/> where it may be uninstalled: not paused, last check "uptodate" or
        /// "outdated", not protected, and for a module a header that declares "# Uninstall: yes"
        /// (<paramref name="uninstallableModules"/>: names of those modules). Packages with no such
        /// computer are left out. Pure logic, unit-tested.
        /// </summary>
        public static List<KeyValuePair<UpdatePackage, List<String>>> UninstallCandidates(IEnumerable<String> computers, IEnumerable<UpdatePackage> packages,
            Func<String, ComputerUpdateState> find, ICollection<String> uninstallableModules)
        {
            List<KeyValuePair<UpdatePackage, List<String>>> result = new List<KeyValuePair<UpdatePackage, List<String>>>();
            List<String> list = computers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (UpdatePackage p in packages)
            {
                bool declares = p.IsModule && uninstallableModules != null
                                && uninstallableModules.Any(n => String.Equals(n, p.ModuleName, StringComparison.OrdinalIgnoreCase));
                List<String> targets = new List<String>();
                foreach (String computer in list)
                {
                    ComputerUpdateState c = find(computer);
                    if (c == null || c.Paused) continue;
                    PackageUpdateState s = c.Find(p.Id);
                    if (s != null && UninstallPolicy.CanOffer(p, s.Status, declares)) targets.Add(computer);
                }
                if (targets.Count > 0) result.Add(new KeyValuePair<UpdatePackage, List<String>>(p, targets));
            }
            return result;
        }

        /// <summary>
        /// For each package (the active profile), what can be done on the target computers after their last check:
        /// update where it is outdated, install where it is not installed (InstallCandidates rules), and how many
        /// are up to date, not checked yet or in another state. Paused computers are left out. Pure logic, unit-tested.
        /// </summary>
        public static List<PackageChoice> PackageChoices(IEnumerable<String> computers, IEnumerable<UpdatePackage> packages,
            Func<String, ComputerUpdateState> find, ICollection<String> installableModules)
        {
            List<String> list = computers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<UpdatePackage> all = packages.ToList();
            Dictionary<String, List<String>> install = InstallCandidates(list, all, find, installableModules)
                .ToDictionary(e => e.Key.Id, e => e.Value, StringComparer.OrdinalIgnoreCase);
            List<PackageChoice> result = new List<PackageChoice>();
            foreach (UpdatePackage p in all)
            {
                PackageChoice choice = new PackageChoice { Package = p };
                List<String> toInstall;
                if (install.TryGetValue(p.Id, out toInstall)) choice.Install = toInstall;
                foreach (String computer in list)
                {
                    ComputerUpdateState c = find(computer);
                    if (c != null && c.Paused) continue;
                    PackageUpdateState s = c == null ? null : c.Find(p.Id);
                    String status = s == null ? null : s.Status;
                    if (status == WorkerProtocol.StatusOutdated) choice.Update.Add(computer);
                    else if (status == WorkerProtocol.StatusUpToDate) choice.UpToDate++;
                    else if (status == null) choice.NotChecked++;
                    else if (!choice.Install.Contains(computer)) choice.Other++;
                }
                result.Add(choice);
            }
            return result;
        }

        /// <summary>Credential chosen for this session only (not saved), used when Options names none.</summary>
        public static String SessionCredential;

        /// <summary>Name of the credential of the software updates: the one of Options, else the one of this session; null: the current user.</summary>
        public static String UpdateCredentialName
        {
            get
            {
                String name = UserSettings.Current.UpdateCredential;
                if (!String.IsNullOrWhiteSpace(name)) return name;
                return String.IsNullOrWhiteSpace(SessionCredential) ? null : SessionCredential;
            }
        }

        /// <summary>
        /// What to offer before a check or an update run as the current user: nothing when a
        /// credential is chosen (Options or this session) or the question was already asked in
        /// this session; else add one when none is stored, use the only one stored, or choose
        /// one in Options.
        /// </summary>
        internal static CredentialOffer OfferFor(String chosen, int stored, bool alreadyAsked)
        {
            if (!String.IsNullOrWhiteSpace(chosen) || alreadyAsked) return CredentialOffer.None;
            if (stored <= 0) return CredentialOffer.Add;
            return stored == 1 ? CredentialOffer.UseStored : CredentialOffer.ChooseInOptions;
        }

        /// <summary>The credential chosen in Options (or for this session) for the software updates; null: the current user.</summary>
        public static StoredCredential UpdateCredential()
        {
            String name = UpdateCredentialName;
            if (String.IsNullOrWhiteSpace(name)) return null;
            StoredCredential c = CredentialStore.Read(name);
            if (c == null) throw new InvalidOperationException(Localization.F("UpdateRunner.NoCredential", "The credential \"{0}\" no longer exists (Tools > Credentials).", name));
            return c;
        }

        // Authenticate with the stored credential and still run the worker as SYSTEM
        static PsExecOptions WithCredential(PsExecOptions o, StoredCredential credential)
        {
            if (credential == null) return o;
            o.UseCredentials = true;
            o.UserName = credential.UserName;
            o.Password = credential.Password;
            o.SystemWithCredentials = true;
            return o;
        }

        public static PsExecOptions BuildOptions(String computer, String command, IList<UpdatePackage> packages)
        {
            return new PsExecOptions
            {
                Computer = computer,
                RunAsSystem = true,
                CopyExecutable = true,
                OverwriteExisting = true,
                ConnectionTimeout = 20,
                Executable = WorkerPath,
                Arguments = BuildWorkerArguments(command, packages, UserSettings.Current.AllowUnsignedModules, UserSettings.Current.UpdateCacheShare,
                    UserSettings.Current.IncludeServers, UserSettings.Current.AllowDomainControllers,
                    UserSettings.Current.CloseWarningSeconds, UserSettings.Current.CloseWarningPostpone)
            };
        }

        /// <summary>Runs the worker on one computer. <paramref name="started"/> receives the PsExec process (to stop it).</summary>
        public static UpdateRunResult Run(String computer, String command, IList<UpdatePackage> packages, Action<Process> started)
        {
            return Run(computer, command, packages, started, null);
        }

        /// <summary><paramref name="onMessage"/> receives each worker message as soon as it arrives (live display).</summary>
        public static UpdateRunResult Run(String computer, String command, IList<UpdatePackage> packages, Action<Process> started, Action<WorkerMessage> onMessage)
        {
            UpdateRunResult result = new UpdateRunResult();
            String missing = RemoteRunner.Prerequisite();
            if (missing != null) { result.Failure = missing; return result; }
            StoredCredential credential;
            try { credential = UpdateCredential(); }
            catch (Exception ex) { result.Failure = ex.Message; return result; }

            // Update modules: copied first to the administrators-only folder of the computer, with
            // their parameter files (deleted by the worker, and again here after the run)
            bool parameterFiles;
            String copyError = UpdateModules.CopyTo(computer, packages, credential, out parameterFiles);
            try
            {
                if (copyError != null) { result.Failure = copyError; return result; }
                return RunWorker(computer, command, packages, started, onMessage, credential, result);
            }
            finally
            {
                if (parameterFiles) UpdateModules.RemoveParameterFiles(computer, packages, credential);
            }
        }

        static UpdateRunResult RunWorker(String computer, String command, IList<UpdatePackage> packages, Action<Process> started,
            Action<WorkerMessage> onMessage, StoredCredential credential, UpdateRunResult result)
        {
            // Enough for every winget call of every package (list, upgrade or install, list), plus the copy
            int timeoutMinutes = 5 + packages.Count * WingetTimeoutMinutes * (command == "scan" ? 1 : 3);
            int timeoutMs = (int)TimeSpan.FromMinutes(timeoutMinutes + 2).TotalMilliseconds;
            PsExecOptions options = WithCredential(BuildOptions(computer, command, packages), credential);
            String method = RemoteRunner.Method;

            Process startedProcess;
            try { startedProcess = RemoteRunner.Start(computer, options.Arguments, credential, timeoutMinutes, options); }
            catch (Exception ex) { result.Failure = ex.Message; return result; }
            using (Process p = startedProcess)
            {
                if (started != null) started(p);
                // Read line by line: the worker flushes each message, PsExec relays it as it comes
                Task<String> stdout = Task.Run(() =>
                {
                    StringBuilder all = new StringBuilder();
                    String line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        all.AppendLine(line);
                        WorkerMessage m = WorkerProtocol.Parse(line);
                        if (m != null && onMessage != null)
                        {
                            try { onMessage(m); } catch (Exception) { }
                        }
                    }
                    return all.ToString();
                });
                Task<String> stderr = p.StandardError.ReadToEndAsync();
                bool exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { p.Kill(); } catch (Exception) { }
                }
                Task.WaitAll(new Task[] { stdout, stderr }, 10000);
                String output = stdout.IsCompleted ? stdout.Result : "";
                String errors = stderr.IsCompleted ? stderr.Result : "";
                result.Messages = WorkerProtocol.ParseAll(output);

                if (!exited)
                    result.Failure = "Timed out";
                else if (result.Messages.Count == 0)
                    result.Failure = RemoteRunner.DescribeFailure(method, p.ExitCode, errors + "\n" + output);
            }
            return result;
        }

        /// <summary>Computer card: "LiteDeploy.Worker.exe info" on the computer (one message, or the failure).</summary>
        public static UpdateRunResult Info(String computer)
        {
            return RunSimple(computer, "info");
        }

        /// <summary>Processes of the computer, the most handles first ("LiteDeploy.Worker.exe processes").</summary>
        public static UpdateRunResult Processes(String computer)
        {
            return RunSimple(computer, "processes");
        }

        /// <summary>One command of the fixed table QuickCommands ("LiteDeploy.Worker.exe quick &lt;id&gt;"), as SYSTEM.</summary>
        public static UpdateRunResult Quick(String computer, String quickCommandId)
        {
            // gpupdate can take minutes: up to 4 per step on the computer, plus the connection
            return RunSimple(computer, QuickCommands.WorkerArguments(quickCommandId), 10);
        }

        /// <summary>
        /// A message to the users logged on to the computer ("LiteDeploy.Worker.exe message --text &lt;base64&gt; ..."),
        /// as SYSTEM. An empty or too long text: ArgumentException (UserMessage.WorkerArguments).
        /// </summary>
        public static UpdateRunResult Message(String computer, String text, int seconds, String title)
        {
            return RunSimple(computer, UserMessage.WorkerArguments(text, seconds, title), 2);
        }

        /// <summary>
        /// SIW report ("LiteDeploy.Worker.exe siw ...", SiwPolicy.WorkerArguments), as SYSTEM; SIW must already be
        /// in %windir%\LiteDeploy\work\SIW (SiwReports). <paramref name="timeoutMinutes"/>: SIW on the computer.
        /// </summary>
        public static UpdateRunResult Siw(String computer, String workerArguments, int timeoutMinutes)
        {
            return RunSimple(computer, workerArguments, timeoutMinutes);
        }

        /// <summary>
        /// Portable report tool ("LiteDeploy.Worker.exe tool &lt;id&gt; ...", PortableToolPolicy.WorkerArguments), as
        /// SYSTEM; the tool must already be in %windir%\LiteDeploy\work\Tools\&lt;id&gt; (PortableToolReports).
        /// </summary>
        public static UpdateRunResult RunPortableTool(String computer, String workerArguments, int timeoutMinutes)
        {
            return RunSimple(computer, workerArguments, timeoutMinutes);
        }

        /// <summary>The programs of the Uninstall keys of HKLM of the computer ("LiteDeploy.Worker.exe programs", read only).</summary>
        public static UpdateRunResult Programs(String computer)
        {
            return RunSimple(computer, ProgramPolicy.ListCommand, 2);
        }

        /// <summary>One program removed ("LiteDeploy.Worker.exe uninstall-program ...", ProgramPolicy.UninstallArguments), as SYSTEM.</summary>
        public static UpdateRunResult UninstallProgram(String computer, String workerArguments)
        {
            return RunSimple(computer, workerArguments, ProgramPolicy.TimeoutMinutes + 2);
        }

        static UpdateRunResult RunSimple(String computer, String workerCommand)
        {
            return RunSimple(computer, workerCommand, 2);
        }

        static UpdateRunResult RunSimple(String computer, String workerCommand, int timeoutMinutes)
        {
            UpdateRunResult result = new UpdateRunResult();
            String missing = RemoteRunner.Prerequisite();
            if (missing != null) { result.Failure = missing; return result; }
            StoredCredential credential;
            try { credential = UpdateCredential(); }
            catch (Exception ex) { result.Failure = ex.Message; return result; }
            PsExecOptions o = WithCredential(new PsExecOptions
            {
                Computer = computer,
                RunAsSystem = true,
                CopyExecutable = true,
                OverwriteExisting = true,
                ConnectionTimeout = 20,
                Executable = WorkerPath,
                Arguments = workerCommand
            }, credential);
            String method = RemoteRunner.Method;
            Process started;
            try { started = RemoteRunner.Start(computer, workerCommand, credential, timeoutMinutes, o); }
            catch (Exception ex) { result.Failure = ex.Message; return result; }
            using (Process p = started)
            {
                Task<String> stdout = p.StandardOutput.ReadToEndAsync();
                Task<String> stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit((int)TimeSpan.FromMinutes(timeoutMinutes + 2).TotalMilliseconds)) { try { p.Kill(); } catch (Exception) { } }
                Task.WaitAll(new Task[] { stdout, stderr }, 10000);
                String output = stdout.IsCompleted ? stdout.Result : "";
                result.Messages = WorkerProtocol.ParseAll(output);
                if (result.Messages.Count == 0)
                    result.Failure = RemoteRunner.DescribeFailure(method, p.HasExited ? p.ExitCode : -1, (stderr.IsCompleted ? stderr.Result : "") + "\n" + output);
            }
            return result;
        }
    }

    internal enum CredentialOffer
    {
        None,
        /// <summary>No credential stored: offer to add one.</summary>
        Add,
        /// <summary>One credential stored, none chosen: offer to use it.</summary>
        UseStored,
        /// <summary>Several stored, none chosen: offer to choose one in Options.</summary>
        ChooseInOptions
    }

    /// <summary>What Install or update offers for one package on the target computers.</summary>
    internal sealed class PackageChoice
    {
        public UpdatePackage Package;
        /// <summary>Computers where the last check says an update is available.</summary>
        public List<String> Update = new List<String>();
        /// <summary>Computers where it can be installed (not installed, install allowed).</summary>
        public List<String> Install = new List<String>();
        public int UpToDate, NotChecked, Other;
    }
}
