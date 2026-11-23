// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SiwRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: "siw" command of the worker: runs SIW (System Information for
//          Windows), that LiteDeploy copied to %windir%\LiteDeploy\work\SIW
//          (only administrators and SYSTEM can write there), as SYSTEM, and
//          writes report.xml (and report.html with --format html) in that
//          folder for LiteDeploy to fetch. SIW runs only when its Authenticode
//          signature is valid and its signer has O=<expected publisher>
//          (SiwPolicy.IsSignedBy); the command line is built from numbers only
//          (SiwPolicy.SiwArguments); each run has a time limit. Same server
//          guard as the updates (ServerPolicy). The SIW executable, its license
//          and its settings file are deleted at the end, whatever happened
//          (the license of SIW does not allow leaving it on the computer); the
//          reports stay until LiteDeploy fetches them and removes the folder.
//          SIW always exits with 0: a report is good when its file exists (and
//          for XML, can be read to the end).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using LiteSuite;

namespace LiteDeploy.Worker
{
    internal static class SiwRunner
    {
        const int ExitFailed = 1;
        const int ExitServerRefused = 102;

        public static string WorkDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiteDeploy", "work", SiwPolicy.WorkFolderName); }
        }

        /// <summary>Runs the request; <paramref name="emit"/> receives the worker lines. Returns the exit code.</summary>
        public static int Run(SiwRequest request, Action<string> emit)
        {
            string dir = WorkDir;
            string os;
            int productType = ComputerInfo.ReadOperatingSystem(out os);
            string osType = ServerPolicy.OsType(productType);
            Stopwatch watch = Stopwatch.StartNew();
            string exeName = null;
            try
            {
                ServerDecision guard = ServerPolicy.Decide(SiwPolicy.Command, productType, request.AllowServers, request.AllowDomainControllers);
                if (guard != ServerDecision.Allowed)
                {
                    emit(Result(false, null, null, watch, osType,
                        "SIW report refused on this server (ProductType " + productType + "): it needs 'Include servers' in LiteDeploy"
                        + (guard == ServerDecision.RefusedServer ? "" : " and " + SiwPolicy.AllowDomainControllersOption) + "."));
                    return ExitServerRefused;
                }

                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                SiwArchitecture architecture = SiwPolicy.ArchitectureFrom(
                    Directory.Exists(Path.Combine(windows, "SysArm32")), Directory.Exists(Path.Combine(windows, "SysWOW64")));
                exeName = SiwPolicy.ExecutableFor(architecture);
                string exe = Path.Combine(dir, exeName);
                if (!File.Exists(exe))
                {
                    emit(Result(false, exeName, null, watch, osType, exeName + " not found in " + dir + " (" + architecture + ")."));
                    return ExitFailed;
                }

                AuthenticodeResult signature = AuthenticodeVerifier.Verify(exe);
                if (!SiwPolicy.IsSignedBy(signature.Status == AuthenticodeStatus.Valid, signature.SignerSubject, request.Publisher))
                {
                    emit(Result(false, exeName, null, watch, osType,
                        "SIW refused: signature " + signature.Status + ", signer " + (string.IsNullOrEmpty(signature.SignerSubject) ? "-" : signature.SignerSubject)
                        + ", expected O=" + request.Publisher + "."));
                    return ExitFailed;
                }

                // Reports of an earlier run are never taken for this one
                foreach (string old in new[] { "xml", "html" }) DeleteQuietly(Path.Combine(dir, SiwPolicy.ReportBaseName + "." + old));

                TimeSpan timeout = TimeSpan.FromSeconds(SiwPolicy.ClampTimeout(request.TimeoutSeconds));
                List<string> made = new List<string>();
                foreach (SiwFormat format in request.Format == SiwFormat.Html ? new[] { SiwFormat.Xml, SiwFormat.Html } : new[] { SiwFormat.Xml })
                {
                    string report = Path.Combine(dir, SiwPolicy.ReportBaseName + "." + SiwPolicy.Extension(format));
                    string error = RunSiw(exe, dir, SiwPolicy.SiwArguments(report, format, request.Software, request.Hardware, request.Network), timeout);
                    if (error == null) error = CheckReport(report, format);
                    if (error != null)
                    {
                        emit(Result(false, exeName, made, watch, osType, error));
                        return ExitFailed;
                    }
                    made.Add(Path.GetFileName(report));
                }
                emit(Result(true, exeName, made, watch, osType, "OK"));
                return 0;
            }
            catch (Exception ex)
            {
                emit(Result(false, exeName, null, watch, osType, ex.Message));
                return ExitFailed;
            }
            finally
            {
                RemoveProgram(dir);
            }
        }

        static string Result(bool ok, string exe, List<string> files, Stopwatch watch, string osType, string message)
        {
            return WorkerProtocol.Format(
                "type", SiwPolicy.TypeResult,
                "result", ok ? "ok" : "failed",
                "files", files == null ? "" : string.Join(";", files),
                "exe", exe ?? "",
                "seconds", ((int)watch.Elapsed.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                "ostype", osType ?? "",
                "computer", Environment.MachineName,
                "message", message);
        }

        // One SIW run, hidden, with a time limit; null when it ended in time
        static string RunSiw(string exe, string dir, string arguments, TimeSpan timeout)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = dir
            };
            using (Process p = Process.Start(psi))
            {
                if (p.WaitForExit((int)timeout.TotalMilliseconds)) return null;
                try { p.Kill(); } catch (Exception) { }
                return "SIW timed out after " + (int)timeout.TotalSeconds + " s.";
            }
        }

        /// <summary>The report exists, is not empty and (XML) can be read to the end; null when it is fine.</summary>
        static string CheckReport(string path, SiwFormat format)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length == 0) return "SIW wrote no report (" + file.Name + ").";
            if (format != SiwFormat.Xml) return null;
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(path, settings))
                    while (reader.Read()) { }
                return null;
            }
            catch (XmlException ex) { return "The XML report cannot be read: " + ex.Message; }
        }

        // Every file of the folder but the reports: executables, license, settings file of SIW
        static void RemoveProgram(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string file in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(file);
                    if (name.StartsWith(SiwPolicy.ReportBaseName + ".", StringComparison.OrdinalIgnoreCase)) continue;
                    DeleteQuietly(file);
                }
            }
            catch (Exception) { }
        }

        static void DeleteQuietly(string path)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                catch (IOException) { System.Threading.Thread.Sleep(1000); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(1000); }
            }
        }
    }
}
