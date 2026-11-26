// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    Program.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Entry point of LiteDeploy.Worker.exe, started by LiteDeploy through
//          PsExec as SYSTEM on each target computer:
//            LiteDeploy.Worker.exe scan    --package "Id|proc1,proc2|Label" ...
//            LiteDeploy.Worker.exe upgrade --package "Id|proc1,proc2|Label" ...
//            LiteDeploy.Worker.exe install --package "Id|proc1,proc2|Label" ...
//            LiteDeploy.Worker.exe uninstall --package "Id|proc1,proc2|Label" ...
//                                  (uninstall mode of LiteDeploy: removes a package
//                                  the check says is installed, never a protected
//                                  one, a module only with "# Uninstall: yes":
//                                  UninstallPolicy; never run by a schedule)
//                                  [--timeout <minutes per winget call or module>]
//                                  [--allow-unsigned-modules] [--cache \\server\share]
//                                  [--allow-servers [--allow-domain-controllers]]
//                                  [--warn-close <seconds> [--allow-postpone]]
//          Server guard (ServerPolicy): upgrade and install do nothing on a
//          server unless --allow-servers is given, nor on a domain controller
//          unless --allow-domain-controllers is given too (error line, done
//          line, exit code 102). scan works everywhere; its done line (and
//          the info card) carries "os" and "ostype".
//          A package "module:<Name>" is an update module, a PowerShell script
//          copied by LiteDeploy to %windir%\LiteDeploy\modules (ModuleRunner),
//          with its parameter file, deleted at the end of the run;
//          "module:<Name>/<Instance>" runs the same script with the parameter
//          file of that instance.
//          scan reports the installed and available version of each package;
//          upgrade closes the package's processes and upgrades it when an
//          upgrade is available. install (explicit, only the packages given:
//          LiteDeploy passes the one the administrator chose) installs a
//          package whose check says "notinstalled" (a module only when its
//          header declares "# Install: yes"), then checks it again; it never
//          touches an installed package. Results: WorkerProtocol lines on stdout.
//            LiteDeploy.Worker.exe info    (computer card: ComputerInfo)
//            LiteDeploy.Worker.exe processes  (processes, handles and memory)
//            LiteDeploy.Worker.exe quick <id> (one command of the fixed table
//                                  QuickCommands: gpupdate, flush DNS...)
//            LiteDeploy.Worker.exe message --text <base64 UTF-8> [--seconds N]
//                                  [--title <base64 UTF-8>] (a message box in
//                                  every active user session: SessionMessenger)
//            LiteDeploy.Worker.exe siw --format xml|html --software N
//                                  --hardware N --network N --publisher "O name"
//                                  --timeout <seconds> [--allow-servers
//                                  [--allow-domain-controllers]] (SIW report:
//                                  SiwRunner, SiwPolicy)
//            LiteDeploy.Worker.exe programs  (programs of the Uninstall keys
//                                  of HKLM and how each can be removed)
//            LiteDeploy.Worker.exe uninstall-program --view 64|32 --key K
//                                  --name <base64 UTF-8> [--allow-servers ...]
//                                  (one of them: ProgramRunner, ProgramPolicy)
//            LiteDeploy.Worker.exe tool <id> --timeout <seconds>
//                                  [--allow-servers [--allow-domain-controllers]]
//                                  (portable report tool of the fixed table:
//                                  PortableToolRunner, PortableToolPolicy)
//          --output <file> (any command): the messages are also written to
//          that file, flushed line by line. Used by the WinRM method, where the
//          worker runs in a scheduled task without a console to read.
//          Exit code: number of failures (0: everything went well).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace LiteDeploy.Worker
{
    internal static class Program
    {
        const int ExitUsage = 100;
        const int ExitNoWinget = 101;
        const int ExitServerRefused = 102;

        // LiteSuite.Common.dll (NuGet package) is embedded in the worker so that PsExec -c, which
        // copies the exe only, still has it: loaded from the resource when the runtime asks for it.
        // The static constructor runs before Main, before any type of LiteSuite.Common is needed.
        static Program()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        }

        internal static System.Reflection.Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string name = new System.Reflection.AssemblyName(args.Name).Name;
            if (!string.Equals(name, "LiteSuite.Common", StringComparison.OrdinalIgnoreCase)) return null;
            using (System.IO.Stream stream = typeof(Program).Assembly.GetManifestResourceStream("LiteSuite.Common.dll"))
            {
                if (stream == null) return null;
                byte[] image = new byte[stream.Length];
                int read = 0;
                while (read < image.Length)
                {
                    int n = stream.Read(image, read, image.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return System.Reflection.Assembly.Load(image);
            }
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            string outputError;
            args = TakeOutputOption(args, out outputError);
            if (outputError != null)
            {
                Console.Error.WriteLine(outputError);
                return ExitUsage;
            }

            if (args != null && args.Length == 1 && args[0].Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                Emit(ComputerInfo.Collect());
                return 0;
            }
            if (args != null && args.Length == 1 && args[0].Equals("processes", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string line in ComputerInfo.Processes(60)) Emit(line);
                return 0;
            }
            if (args != null && args.Length >= 1 && args[0].Equals(QuickCommands.WorkerCommand, StringComparison.OrdinalIgnoreCase))
            {
                // "quick <id> [--message-language fr|en|computer]"
                QuickCommand quick = args.Length == 2 || (args.Length == 4 && args[2] == CloseWarning.LanguageOption && CloseWarning.IsValidLanguage(args[3]))
                    ? QuickCommands.Find(args[1]) : null;
                if (quick == null)
                {
                    Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", "Unknown quick command"));
                    return ExitUsage;
                }
                return RunQuick(quick, CloseWarning.IsFrench(args.Length == 4 ? args[3] : CloseWarning.LanguageComputer,
                    System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName));
            }
            if (args != null && args.Length == 1 && args[0] == ProgramPolicy.ListCommand)
                return ProgramRunner.List(Emit);
            if (args != null && args.Length >= 1 && args[0] == ProgramPolicy.UninstallCommand)
            {
                string view, key, name;
                bool programServers, programDcs;
                string programError = ProgramPolicy.ParseUninstall(args, out view, out key, out name, out programServers, out programDcs);
                if (programError != null)
                {
                    Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", programError));
                    return ExitUsage;
                }
                return ProgramRunner.Uninstall(view, key, name, programServers, programDcs, Emit);
            }
            if (args != null && args.Length >= 1 && args[0].Equals(SiwPolicy.Command, StringComparison.OrdinalIgnoreCase))
            {
                SiwRequest siw;
                string siwError = SiwPolicy.ParseArguments(args, out siw);
                if (siwError != null)
                {
                    Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", siwError));
                    return ExitUsage;
                }
                return SiwRunner.Run(siw, Emit);
            }
            if (args != null && args.Length >= 1 && args[0].Equals(PortableToolPolicy.Command, StringComparison.OrdinalIgnoreCase))
            {
                PortableToolRequest tool;
                string toolError = PortableToolPolicy.ParseArguments(args, out tool);
                if (toolError != null)
                {
                    Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", toolError));
                    return ExitUsage;
                }
                return PortableToolRunner.Run(tool, Emit);
            }
            if (args != null && args.Length >= 1 && args[0].Equals(UserMessage.WorkerCommand, StringComparison.OrdinalIgnoreCase))
            {
                string text, title;
                int seconds;
                string messageError = UserMessage.ParseArguments(args, out text, out seconds, out title);
                if (messageError != null)
                {
                    Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", messageError));
                    return ExitUsage;
                }
                return RunMessage(text, seconds, title);
            }

            string command;
            List<UpdatePackage> packages;
            int timeoutMinutes;
            bool allowUnsigned;
            string cache;
            bool allowServers, allowDomainControllers;
            string usageError = ParseArguments(args, out command, out packages, out timeoutMinutes, out allowUnsigned, out cache,
                out allowServers, out allowDomainControllers);
            // Shared cache of the installers, for the update modules (LITEDEPLOY_CACHE, docs/modules.md)
            if (cache != null) Environment.SetEnvironmentVariable("LITEDEPLOY_CACHE", cache);
            try
            {
                if (usageError != null)
                {
                    Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", usageError));
                    Console.Error.WriteLine("Usage: LiteDeploy.Worker.exe scan|upgrade|install --package \"Id|processes|label\" ... [--timeout minutes] [--allow-servers [--allow-domain-controllers]]");
                    return ExitUsage;
                }
                return Run(command, packages, timeoutMinutes, allowUnsigned, allowServers, allowDomainControllers);
            }
            finally
            {
                // Parameter files of the modules (docs/modules.md): never left behind, whatever happened
                foreach (UpdatePackage p in packages.Where(x => x.IsModule)) ModuleRunner.DeleteParameters(p);
            }
        }

        static int Run(string command, List<UpdatePackage> packages, int timeoutMinutes, bool allowUnsigned, bool allowServers, bool allowDomainControllers)
        {
            string wingetPath = WingetClient.Locate();
            bool needsWinget = packages.Any(p => !p.IsModule);
            List<string> macs, ips;
            ComputerInfo.Network(out macs, out ips);
            string os;
            int productType = ComputerInfo.ReadOperatingSystem(out os);
            string osType = ServerPolicy.OsType(productType);
            List<string> start = new List<string>
            {
                "type", "start",
                "computer", Environment.MachineName,
                "user", WindowsIdentity.GetCurrent().Name,
                "command", command,
                "winget", wingetPath ?? "",
                "macs", string.Join(" ", macs),
                "ips", string.Join(" ", ips),
                "users", string.Join(", ", ComputerInfo.LoggedOnUsers())
            };
            // The hardware inventory comes with every check or update (a few WMI queries)
            start.AddRange(ComputerInfo.Hardware());
            Emit(WorkerProtocol.Format(start.ToArray()));

            // Server guard: nothing is checked, closed, upgraded or installed on a server that is not allowed
            ServerDecision guard = ServerPolicy.Decide(command, productType, allowServers, allowDomainControllers);
            if (guard != ServerDecision.Allowed)
            {
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", ServerPolicy.RefusalMessage(guard, productType)));
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "failed", packages.Count.ToString(), "os", os, "ostype", osType));
                return ExitServerRefused;
            }

            if (wingetPath == null && needsWinget)
            {
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", "winget.exe not found (App Installer missing)"));
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "failed", packages.Count.ToString(), "os", os, "ostype", osType));
                return ExitNoWinget;
            }

            WingetClient winget = wingetPath == null ? null : new WingetClient(wingetPath, TimeSpan.FromMinutes(timeoutMinutes));
            int failed = 0, upgraded = 0, installed = 0;
            foreach (UpdatePackage package in packages)
            {
                if (package.IsModule)
                {
                    RunModule(package, command, allowUnsigned, TimeSpan.FromMinutes(timeoutMinutes), ref failed, ref upgraded, ref installed);
                    continue;
                }

                Progress(package, WorkerProtocol.StepChecking);
                PackageState state = Check(winget, package);
                Emit(state.ToMessage());
                if (command == InstallPolicy.Command)
                {
                    InstallPackage(winget, package, state, ref failed, ref installed);
                    continue;
                }
                if (command == UninstallPolicy.Command)
                {
                    UninstallPackage(winget, package, state, ref failed, ref installed);
                    continue;
                }
                if (state.Status == WorkerProtocol.StatusError) { failed++; continue; }
                if (command != "upgrade") continue;

                if (state.Status != WorkerProtocol.StatusOutdated)
                {
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultSkipped, null,
                        state.Status == WorkerProtocol.StatusNotInstalled ? "Not installed" : "Already up to date"));
                    continue;
                }

                Progress(package, WorkerProtocol.StepClosing);
                string postponed = WarnBeforeClosing(package);
                if (postponed != null)
                {
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultSkipped, null, postponed));
                    continue;
                }
                string closeError = CloseProcesses(package);
                if (closeError != null)
                {
                    failed++;
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultFailed, null, closeError));
                    continue;
                }

                Progress(package, WorkerProtocol.StepUpgrading);
                WingetResult result = winget.Upgrade(package.Id);
                string outcome = WingetClient.Outcome(result.ExitCode, WorkerProtocol.ResultUpgraded);
                bool ok = outcome == WorkerProtocol.ResultUpgraded;
                if (ok) upgraded++;
                if (outcome == WorkerProtocol.ResultFailed) failed++;
                Emit(UpgradeMessage(package, outcome, result.Hex,
                    WingetClient.Describe(result.ExitCode) + (ok ? "" : LastLines(result.Output))));

                // State after the upgrade (new installed version)
                Emit(Check(winget, package).ToMessage());
            }

            if (command == InstallPolicy.Command)
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "installed", installed.ToString(), "failed", failed.ToString(), "os", os, "ostype", osType));
            else if (command == UninstallPolicy.Command)
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "uninstalled", installed.ToString(), "failed", failed.ToString(), "os", os, "ostype", osType));
            else
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "upgraded", upgraded.ToString(), "failed", failed.ToString(), "os", os, "ostype", osType));
            return Math.Min(failed, 99);
        }

        // Uninstall run, winget package: removes it only when its check says it is installed and it is not
        // protected, after closing its processes, then a fresh state
        static void UninstallPackage(WingetClient winget, UpdatePackage package, PackageState state, ref int failed, ref int uninstalled)
        {
            switch (UninstallPolicy.Decide(package, true, state.Status))
            {
                case UninstallDecision.CheckFailed:
                    failed++;
                    return;
                case UninstallDecision.Protected:
                    failed++;
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultFailed, null, UninstallPolicy.ProtectedMessage(package.Id)));
                    return;
                case UninstallDecision.NotInstalled:
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultSkipped, null, "Not installed"));
                    return;
            }

            Progress(package, WorkerProtocol.StepClosing);
            string closeError = CloseProcesses(package);
            if (closeError != null)
            {
                failed++;
                Emit(UpgradeMessage(package, WorkerProtocol.ResultFailed, null, closeError));
                return;
            }
            Progress(package, WorkerProtocol.StepUninstalling);
            WingetResult result = winget.Uninstall(package.Id);
            string outcome = WingetClient.Outcome(result.ExitCode, WorkerProtocol.ResultUninstalled);
            if (outcome == WorkerProtocol.ResultSkipped) outcome = WorkerProtocol.ResultFailed;
            bool ok = outcome == WorkerProtocol.ResultUninstalled;
            if (ok) uninstalled++; else failed++;
            Emit(UpgradeMessage(package, outcome, result.Hex,
                WingetClient.Describe(result.ExitCode) + (ok ? "" : LastLines(result.Output))));

            // State after the uninstall (expected: notinstalled)
            Emit(Check(winget, package).ToMessage());
        }

        // Uninstall run, module: "-Action Uninstall" only when installed, not protected and declared by the header
        static void UninstallModule(UpdatePackage module, ModuleResult check, bool allowUnsigned, TimeSpan timeout,
            Dictionary<string, string> parameters, ref int failed, ref int uninstalled)
        {
            switch (UninstallPolicy.Decide(module, ModuleRunner.DeclaresUninstall(module), check.Status))
            {
                case UninstallDecision.CheckFailed:
                    failed++;
                    return;
                case UninstallDecision.Protected:
                    failed++;
                    Emit(UpgradeMessage(module, WorkerProtocol.ResultFailed, null, UninstallPolicy.ProtectedMessage(module.Id)));
                    return;
                case UninstallDecision.NotSupported:
                    failed++;
                    Emit(UpgradeMessage(module, WorkerProtocol.ResultFailed, null, UninstallPolicy.NotSupportedMessage(module.ModuleName)));
                    return;
                case UninstallDecision.NotInstalled:
                    Emit(UpgradeMessage(module, WorkerProtocol.ResultSkipped, null, "Not installed"));
                    return;
            }

            Progress(module, WorkerProtocol.StepUninstalling);
            ModuleResult uninstall = ModuleRunner.Run(module, UninstallPolicy.ModuleAction, allowUnsigned, timeout, parameters);
            bool ok = uninstall.Status != WorkerProtocol.StatusError;
            if (ok) uninstalled++; else failed++;
            Emit(UpgradeMessage(module, ok ? WorkerProtocol.ResultUninstalled : WorkerProtocol.ResultFailed,
                uninstall.ExitCode.ToString(), uninstall.Message ?? (ok ? "OK" : "Failed")));

            Emit(ModuleMessage(module, ModuleRunner.Run(module, "Check", allowUnsigned, timeout, parameters)));
        }

        // Install run, winget package: installs it only when its check says "notinstalled", then a fresh state.
        // No process is closed: the product is not installed for the computer.
        static void InstallPackage(WingetClient winget, UpdatePackage package, PackageState state, ref int failed, ref int installed)
        {
            switch (InstallPolicy.Decide(false, true, state.Status))
            {
                case InstallDecision.CheckFailed:
                    failed++;
                    return;
                case InstallDecision.AlreadyInstalled:
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultSkipped, null, "Already installed"));
                    return;
            }

            Progress(package, WorkerProtocol.StepInstalling);
            WingetResult result = winget.Install(package.Id);
            string outcome = WingetClient.Outcome(result.ExitCode, WorkerProtocol.ResultInstalled);
            bool ok = outcome == WorkerProtocol.ResultInstalled;
            if (ok) installed++;
            if (outcome == WorkerProtocol.ResultFailed) failed++;
            Emit(UpgradeMessage(package, outcome, result.Hex,
                WingetClient.Describe(result.ExitCode) + (ok ? "" : LastLines(result.Output))));

            // State after the install (installed version)
            Emit(Check(winget, package).ToMessage());
        }

        // Update module: Check, then (upgrade) Update when it reports an update, or (install) Install
        // when it reports "notinstalled" and its header declares "# Install: yes", then Check again
        static void RunModule(UpdatePackage module, string command, bool allowUnsigned, TimeSpan timeout, ref int failed, ref int upgraded, ref int installed)
        {
            // Values of the parameters: read once, the file is deleted at once
            Dictionary<string, string> parameters = ModuleRunner.TakeParameters(module);
            Progress(module, WorkerProtocol.StepChecking);
            ModuleResult check = ModuleRunner.Run(module, "Check", allowUnsigned, timeout, parameters);
            Emit(ModuleMessage(module, check));
            if (command == InstallPolicy.Command)
            {
                InstallModule(module, check, allowUnsigned, timeout, parameters, ref failed, ref installed);
                return;
            }
            if (command == UninstallPolicy.Command)
            {
                UninstallModule(module, check, allowUnsigned, timeout, parameters, ref failed, ref installed);
                return;
            }
            if (check.Status == WorkerProtocol.StatusError) { failed++; return; }
            if (command != "upgrade") return;

            if (check.Status != WorkerProtocol.StatusOutdated)
            {
                Emit(UpgradeMessage(module, WorkerProtocol.ResultSkipped, null,
                    check.Status == WorkerProtocol.StatusNotInstalled ? "Not installed" : "Already up to date"));
                return;
            }

            Progress(module, WorkerProtocol.StepClosing);
            string postponed = WarnBeforeClosing(module);
            if (postponed != null)
            {
                Emit(UpgradeMessage(module, WorkerProtocol.ResultSkipped, null, postponed));
                return;
            }
            string closeError = CloseProcesses(module);
            if (closeError != null)
            {
                failed++;
                Emit(UpgradeMessage(module, WorkerProtocol.ResultFailed, null, closeError));
                return;
            }

            Progress(module, WorkerProtocol.StepUpgrading);
            ModuleResult update = ModuleRunner.Run(module, "Update", allowUnsigned, timeout, parameters);
            bool ok = update.Status != WorkerProtocol.StatusError;
            if (ok) upgraded++; else failed++;
            Emit(UpgradeMessage(module, ok ? WorkerProtocol.ResultUpgraded : WorkerProtocol.ResultFailed,
                update.ExitCode.ToString(), update.Message ?? (ok ? "OK" : "Failed")));

            Emit(ModuleMessage(module, ModuleRunner.Run(module, "Check", allowUnsigned, timeout, parameters)));
        }

        // Install run, module: same result line, exit codes, time limit, signature and parameters as Update
        static void InstallModule(UpdatePackage module, ModuleResult check, bool allowUnsigned, TimeSpan timeout,
            Dictionary<string, string> parameters, ref int failed, ref int installed)
        {
            switch (InstallPolicy.Decide(true, ModuleRunner.DeclaresInstall(module), check.Status))
            {
                case InstallDecision.CheckFailed:
                    failed++;
                    return;
                case InstallDecision.NotSupported:
                    failed++;
                    Emit(UpgradeMessage(module, WorkerProtocol.ResultFailed, null, InstallPolicy.NotSupportedMessage(module.ModuleName)));
                    return;
                case InstallDecision.AlreadyInstalled:
                    Emit(UpgradeMessage(module, WorkerProtocol.ResultSkipped, null, "Already installed"));
                    return;
            }

            Progress(module, WorkerProtocol.StepInstalling);
            ModuleResult install = ModuleRunner.Run(module, InstallPolicy.ModuleAction, allowUnsigned, timeout, parameters);
            bool ok = install.Status != WorkerProtocol.StatusError;
            if (ok) installed++; else failed++;
            Emit(UpgradeMessage(module, ok ? WorkerProtocol.ResultInstalled : WorkerProtocol.ResultFailed,
                install.ExitCode.ToString(), install.Message ?? (ok ? "OK" : "Failed")));

            Emit(ModuleMessage(module, ModuleRunner.Run(module, "Check", allowUnsigned, timeout, parameters)));
        }

        static string ModuleMessage(UpdatePackage module, ModuleResult r)
        {
            return WorkerProtocol.Format(
                "type", WorkerProtocol.TypePackage,
                "id", module.Id,
                "status", r.Status,
                "installed", r.Installed,
                "available", r.Available,
                "message", r.Message);
        }

        sealed class PackageState
        {
            public UpdatePackage Package;
            public string Status;
            public string Installed;
            public string Available;
            public string Message;

            public string ToMessage()
            {
                return WorkerProtocol.Format(
                    "type", WorkerProtocol.TypePackage,
                    "id", Package.Id,
                    "status", Status,
                    "installed", Installed,
                    "available", Available,
                    "message", Message);
            }
        }

        static PackageState Check(WingetClient winget, UpdatePackage package)
        {
            PackageState state = new PackageState { Package = package };
            WingetResult list = winget.List(package.Id);
            WingetListRow row;
            state.Status = WingetListParser.Classify(list.ExitCode, list.Output, package.Id, out row);
            if (state.Status == WorkerProtocol.StatusError)
            {
                state.Message = "winget list " + list.Hex + " " + WingetClient.Describe(list.ExitCode) + LastLines(list.Output);
                return state;
            }
            if (row != null)
            {
                state.Installed = row.Version;
                state.Available = row.Available;
            }
            return state;
        }

        /// <summary>--warn-close: seconds of warning before the processes of a package are closed (0: none).</summary>
        static int closeWarningSeconds;
        /// <summary>--allow-postpone: the users may postpone (Cancel).</summary>
        static bool allowPostpone;
        /// <summary>--message-language: fr, en or computer (null: computer).</summary>
        static string messageLanguage;

        /// <summary>The warning options of the last ParseArguments (tests).</summary>
        internal static int CloseWarningSeconds { get { return closeWarningSeconds; } }
        internal static bool AllowPostpone { get { return allowPostpone; } }
        internal static string MessageLanguage { get { return messageLanguage; } }

        /// <summary>
        /// Before CloseProcesses: when a process of the package runs in a user session and --warn-close is set, the
        /// users of those sessions get a message (CloseWarning) and the worker waits for OK, Cancel or the end of the
        /// delay. Returns the result message when a user postponed (nothing is closed), else null.
        /// </summary>
        static string WarnBeforeClosing(UpdatePackage package)
        {
            if (closeWarningSeconds <= 0 || package.Processes.Length == 0) return null;
            List<int> sessions = new List<int>();
            List<string> running = new List<string>();
            foreach (string name in package.Processes)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        int session;
                        try { session = p.SessionId; } catch (Exception) { continue; }
                        if (session == 0) continue;   // services: nobody to warn
                        if (!sessions.Contains(session)) sessions.Add(session);
                        if (!running.Contains(name, StringComparer.OrdinalIgnoreCase)) running.Add(name);
                    }
                }
            }
            if (sessions.Count == 0) return null;
            bool french = CloseWarning.IsFrench(messageLanguage, System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName);
            string product = string.IsNullOrWhiteSpace(package.Label) ? package.Id : package.Label;
            List<string> users = SessionMessenger.AskBeforeClosing(sessions, CloseWarning.Title(french),
                CloseWarning.Text(product, CloseWarning.ReadableNames(running), closeWarningSeconds, allowPostpone, french),
                closeWarningSeconds, allowPostpone);
            if (users.Count == 0) return null;
            return CloseWarning.PostponedMessage + " (" + string.Join(", ", users.Distinct()) + ")";
        }

        // Force-closes the package's processes (SYSTEM cannot ask them to close); null when none is left running
        static string CloseProcesses(UpdatePackage package)
        {
            List<string> closed = new List<string>();
            foreach (string name in package.Processes)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try { p.Kill(); closed.Add(name); }
                        catch (Exception) { }
                    }
                }
            }
            if (closed.Count == 0) return null;

            Thread.Sleep(3000);
            string[] still = package.Processes.Where(n => Process.GetProcessesByName(n).Length > 0).ToArray();
            if (still.Length > 0) return "Could not close " + string.Join(", ", still);
            Emit(WorkerProtocol.Format("type", "closed", "id", package.Id, "processes", string.Join(", ", closed.Distinct())));
            return null;
        }

        static string UpgradeMessage(UpdatePackage package, string result, string code, string message)
        {
            return WorkerProtocol.Format("type", WorkerProtocol.TypeUpgrade, "id", package.Id, "result", result, "code", code, "message", message);
        }

        // Last meaningful lines of a winget output (no spinner or progress bar), for a failure message
        static string LastLines(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            string[] lines = output.Split('\n')
                .Select(l => { int cr = l.TrimEnd('\r').LastIndexOf('\r'); return (cr >= 0 ? l.Substring(cr + 1) : l).Trim(); })
                .Where(l => l.Length > 2 && l.Any(char.IsLetter) && l.IndexOf('█') < 0 && l.IndexOf('▒') < 0)
                .ToArray();
            return lines.Length == 0 ? "" : ": " + string.Join(" / ", lines.Skip(Math.Max(0, lines.Length - 3)));
        }

        /// <summary>A UNC path (\\server\share\...) without quotes or wildcards.</summary>
        internal static bool IsValidCachePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 260 || !path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length < 5) return false;
            return path.IndexOfAny(new[] { '"', '*', '?', '<', '>', '|', '\r', '\n' }) < 0 && !path.Contains("..");
        }

        /// <summary>Minutes per step of a quick command (gpupdate can take a while).</summary>
        const int QuickStepTimeoutMinutes = 4;

        /// <summary>
        /// "quick &lt;id&gt;": the steps of a command of the fixed table (QuickCommands), from System32,
        /// their output relayed line by line (type=output), then one type=quick line with the exit code.
        /// </summary>
        static int RunQuick(QuickCommand quick, bool french)
        {
            int code = 0;
            foreach (QuickCommandStep template in quick.Steps)
            {
                // The text shown to the users (restart comment) in the language of the messages
                QuickCommandStep step = new QuickCommandStep { FileName = template.FileName, Arguments = QuickCommands.LocalizedArguments(quick, template, french),
                    IgnoreFailure = template.IgnoreFailure, AcceptedCodes = template.AcceptedCodes };
                Emit(WorkerProtocol.Format("type", QuickCommands.TypeOutput, "text", "> " + step.FileName + " " + step.Arguments));
                code = RunQuickStep(step);
                if (code != 0 && step.IsSuccess(code))
                {
                    Emit(WorkerProtocol.Format("type", QuickCommands.TypeOutput, "text", "Exit code " + code + ": accepted"));
                    code = 0;
                }
                if (code != 0 && !step.IgnoreFailure) break;
            }
            Emit(WorkerProtocol.Format("type", QuickCommands.TypeResult, "id", quick.Id, "code", code.ToString(), "computer", Environment.MachineName));
            return code == 0 ? 0 : 1;
        }

        static int RunQuickStep(QuickCommandStep step)
        {
            Encoding oem;
            try { oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch (Exception) { oem = Encoding.Default; }
            ProcessStartInfo psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, step.FileName), step.Arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem
            };
            try
            {
                using (Process p = new Process { StartInfo = psi })
                {
                    DataReceivedEventHandler relay = (s, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            lock (psi) Emit(WorkerProtocol.Format("type", QuickCommands.TypeOutput, "text", e.Data.TrimEnd()));
                    };
                    p.OutputDataReceived += relay;
                    p.ErrorDataReceived += relay;
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit((int)TimeSpan.FromMinutes(QuickStepTimeoutMinutes).TotalMilliseconds))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        Emit(WorkerProtocol.Format("type", QuickCommands.TypeOutput, "text", "Timed out after " + QuickStepTimeoutMinutes + " minutes"));
                        return 1460;   // ERROR_TIMEOUT
                    }
                    p.WaitForExit();   // the last lines of the redirected output
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Emit(WorkerProtocol.Format("type", QuickCommands.TypeOutput, "text", step.FileName + ": " + ex.Message));
                return 1;
            }
        }

        /// <summary>
        /// "message": the text in every active user session (SessionMessenger), then one type=message line:
        /// sessions (sent), users, failed (sessions not reached) and error. No user logged on: sessions=0, exit 0.
        /// </summary>
        static int RunMessage(string text, int seconds, string title)
        {
            MessageOutcome outcome = SessionMessenger.Send(string.IsNullOrEmpty(title) ? "LiteDeploy" : title, text, seconds);
            Emit(MessageResultLine(outcome));
            return outcome.Errors.Count == 0 ? 0 : 1;
        }

        internal static string MessageResultLine(MessageOutcome outcome)
        {
            return WorkerProtocol.Format("type", UserMessage.TypeResult,
                "sessions", outcome.Sent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "users", string.Join(", ", outcome.Users),
                "failed", outcome.Errors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "error", string.Join(" | ", outcome.Errors),
                "computer", Environment.MachineName);
        }

        static void Progress(UpdatePackage package, string step)
        {
            Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeProgress, "id", package.Id, "step", step));
        }

        static System.IO.StreamWriter output;

        static void Emit(string line)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
            if (output != null)
            {
                output.WriteLine(line);
                output.Flush();
            }
        }

        /// <summary>Removes "--output file" from the arguments and opens that file (readable while written).</summary>
        internal static string[] TakeOutputOption(string[] args, out string error)
        {
            error = null;
            if (args == null) return null;
            List<string> rest = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].Equals("--output", StringComparison.OrdinalIgnoreCase)) { rest.Add(args[i]); continue; }
                if (i + 1 >= args.Length) { error = "--output needs a file"; return rest.ToArray(); }
                string path = args[++i];
                try
                {
                    System.IO.FileStream stream = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                    output = new System.IO.StreamWriter(stream, new UTF8Encoding(false));
                    output.AutoFlush = true;
                }
                catch (Exception ex) { error = "--output " + path + ": " + ex.Message; }
            }
            return rest.ToArray();
        }

        internal static string ParseArguments(string[] args, out string command, out List<UpdatePackage> packages, out int timeoutMinutes, out bool allowUnsigned)
        {
            string cache;
            return ParseArguments(args, out command, out packages, out timeoutMinutes, out allowUnsigned, out cache);
        }

        internal static string ParseArguments(string[] args, out string command, out List<UpdatePackage> packages, out int timeoutMinutes, out bool allowUnsigned, out string cache)
        {
            bool allowServers, allowDomainControllers;
            return ParseArguments(args, out command, out packages, out timeoutMinutes, out allowUnsigned, out cache, out allowServers, out allowDomainControllers);
        }

        /// <summary>With the server guard options (ServerPolicy): --allow-servers, --allow-domain-controllers.</summary>
        internal static string ParseArguments(string[] args, out string command, out List<UpdatePackage> packages, out int timeoutMinutes, out bool allowUnsigned, out string cache,
            out bool allowServers, out bool allowDomainControllers)
        {
            cache = null;
            allowServers = false;
            allowDomainControllers = false;
            closeWarningSeconds = 0;
            allowPostpone = false;
            messageLanguage = null;
            command = null;
            packages = new List<UpdatePackage>();
            timeoutMinutes = 15;
            allowUnsigned = false;
            if (args == null || args.Length == 0) return "No command";

            command = args[0].ToLowerInvariant();
            if (command != "scan" && command != "upgrade" && command != InstallPolicy.Command && command != UninstallPolicy.Command) return "Unknown command: " + args[0];

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--package" && i + 1 < args.Length)
                {
                    UpdatePackage p = UpdatePackage.Parse(args[++i]);
                    if (p == null) return "Invalid package: " + args[i];
                    if (!packages.Any(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))) packages.Add(p);
                }
                else if (a == "--cache" && i + 1 < args.Length)
                {
                    cache = args[++i];
                    if (!IsValidCachePath(cache)) return "Invalid cache path: " + cache;
                }
                else if (a == "--allow-unsigned-modules")
                {
                    allowUnsigned = true;
                }
                else if (a == ServerPolicy.AllowServersOption)
                {
                    allowServers = true;
                }
                else if (a == ServerPolicy.AllowDomainControllersOption)
                {
                    allowDomainControllers = true;
                }
                else if (a == CloseWarning.WarnOption && i + 1 < args.Length)
                {
                    int s;
                    if (!int.TryParse(args[++i], out s) || s < 0 || s > CloseWarning.MaxSeconds) return "Invalid warning delay: " + args[i];
                    closeWarningSeconds = s;
                }
                else if (a == CloseWarning.PostponeOption)
                {
                    allowPostpone = true;
                }
                else if (a == CloseWarning.LanguageOption && i + 1 < args.Length)
                {
                    if (!CloseWarning.IsValidLanguage(args[++i])) return "Invalid message language: " + args[i];
                    messageLanguage = args[i];
                }
                else if (a == "--timeout" && i + 1 < args.Length)
                {
                    int t;
                    if (!int.TryParse(args[++i], out t) || t < 1 || t > 240) return "Invalid timeout: " + args[i];
                    timeoutMinutes = t;
                }
                else
                {
                    return "Unknown argument: " + a;
                }
            }
            if (packages.Count == 0) return "No package";
            return null;
        }
    }
}
