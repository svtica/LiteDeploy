// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PortableToolRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: "tool <id>" command of the worker: runs one portable report tool of
//          the fixed table (PortableToolPolicy), that LiteDeploy copied to
//          %windir%\LiteDeploy\work\Tools\<id> (only administrators and SYSTEM
//          can write there), as SYSTEM, and writes report.<ext> in that folder
//          for LiteDeploy to fetch. The tool runs only when its Authenticode
//          signature is valid and its signer has the O= of the table; its
//          command line is the fixed one of the table; the run has a time
//          limit. Same server guard as the updates (ServerPolicy). Everything
//          but the report is deleted at the end, whatever happened (the tool,
//          and the settings file some tools write next to themselves).
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LiteSuite;

namespace LiteDeploy.Worker
{
    internal static class PortableToolRunner
    {
        const int ExitFailed = 1;
        const int ExitServerRefused = 102;

        public static string WorkDir(PortableTool tool)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiteDeploy", "work", PortableToolPolicy.WorkFolderName, tool.Id);
        }

        /// <summary>Runs the request; <paramref name="emit"/> receives the worker lines. Returns the exit code.</summary>
        public static int Run(PortableToolRequest request, Action<string> emit)
        {
            PortableTool tool = request.Tool;
            string dir = WorkDir(tool);
            string os;
            int productType = ComputerInfo.ReadOperatingSystem(out os);
            string osType = ServerPolicy.OsType(productType);
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                ServerDecision guard = ServerPolicy.Decide(PortableToolPolicy.Command, productType, request.AllowServers, request.AllowDomainControllers);
                if (guard != ServerDecision.Allowed)
                {
                    emit(Result(tool, false, watch, osType,
                        tool.Product + " refused on this server (ProductType " + productType + "): it needs 'Include servers' in LiteDeploy"
                        + (guard == ServerDecision.RefusedServer ? "" : " and " + ServerPolicy.AllowDomainControllersOption) + "."));
                    return ExitServerRefused;
                }

                string exe = Path.Combine(dir, tool.Executable);
                if (!File.Exists(exe))
                {
                    emit(Result(tool, false, watch, osType, tool.Executable + " not found in " + dir + "."));
                    return ExitFailed;
                }
                AuthenticodeResult signature = AuthenticodeVerifier.Verify(exe);
                if (!SiwPolicy.IsSignedBy(signature.Status == AuthenticodeStatus.Valid, signature.SignerSubject, tool.Publisher))
                {
                    emit(Result(tool, false, watch, osType,
                        tool.Product + " refused: signature " + signature.Status + ", signer "
                        + (string.IsNullOrEmpty(signature.SignerSubject) ? "-" : signature.SignerSubject) + ", expected O=" + tool.Publisher + "."));
                    return ExitFailed;
                }

                string report = Path.Combine(dir, PortableToolPolicy.ReportFileName(tool));
                if (File.Exists(report)) File.Delete(report);
                TimeSpan timeout = TimeSpan.FromSeconds(PortableToolPolicy.ClampTimeout(request.TimeoutSeconds));
                ProcessStartInfo psi = new ProcessStartInfo(exe, PortableToolPolicy.Arguments(tool, report))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = dir
                };
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        emit(Result(tool, false, watch, osType, tool.Product + " timed out after " + (int)timeout.TotalSeconds + " s."));
                        return ExitFailed;
                    }
                }
                FileInfo made = new FileInfo(report);
                if (!made.Exists || made.Length == 0)
                {
                    emit(Result(tool, false, watch, osType, tool.Product + " wrote no report."));
                    return ExitFailed;
                }
                emit(Result(tool, true, watch, osType, "OK"));
                return 0;
            }
            catch (Exception ex)
            {
                emit(Result(tool, false, watch, osType, ex.Message));
                return ExitFailed;
            }
            finally
            {
                RemoveProgram(dir);
            }
        }

        static string Result(PortableTool tool, bool ok, Stopwatch watch, string osType, string message)
        {
            return WorkerProtocol.Format(
                "type", PortableToolPolicy.TypeResult,
                "tool", tool.Id,
                "result", ok ? "ok" : "failed",
                "file", ok ? PortableToolPolicy.ReportFileName(tool) : "",
                "seconds", ((int)watch.Elapsed.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                "ostype", osType ?? "",
                "computer", Environment.MachineName,
                "message", message);
        }

        // Every file of the folder but the report
        static void RemoveProgram(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string file in Directory.GetFiles(dir))
                {
                    if (Path.GetFileName(file).StartsWith(PortableToolPolicy.ReportBaseName + ".", StringComparison.OrdinalIgnoreCase)) continue;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try { File.Delete(file); break; }
                        catch (IOException) { System.Threading.Thread.Sleep(1000); }
                        catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(1000); }
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
