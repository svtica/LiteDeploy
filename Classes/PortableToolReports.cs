// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PortableToolReports.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Report of one portable tool of the fixed table (PortableToolPolicy)
//          on one computer: finds the tool in the folder of App > Options (the
//          folder itself or a sub-folder named after the product) and checks
//          its signature, copies it to the ADMIN$ share of the computer
//          (LiteDeploy\work\Tools\<id>), runs the worker "tool <id>" command as
//          SYSTEM, copies the report to %LOCALAPPDATA%\LiteDeploy\tools\<id>\
//          <computer>\<yyyyMMdd-HHmmss>.<ext> with a plain HTML view next to
//          it, then removes the folder of the computer, also after an error.
//          Same steps as the SIW report (SiwReports). Reports are site data:
//          they stay in the profile of the administrator.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LiteSuite;

namespace LiteDeploy
{
    /// <summary>What a portable tool gave on one computer.</summary>
    internal sealed class ToolRunResult
    {
        public String Computer;
        public bool Ok;
        public String Failure;
        public int Seconds;
        /// <summary>Local files (null when not made): the report of the tool and its HTML view.</summary>
        public String ReportPath, ViewPath;
        public CsvReport Report;
    }

    internal static class PortableToolReports
    {
        /// <summary>%LOCALAPPDATA%\LiteDeploy\tools\&lt;id&gt;: one folder per computer.</summary>
        public static String Root(PortableTool tool)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "tools", tool.Id);
        }

        /// <summary>
        /// Where the tool is: &lt;folder&gt;\&lt;exe&gt;, else &lt;folder&gt;\&lt;product&gt;\&lt;exe&gt;; null when neither exists.
        /// Pure logic (<paramref name="fileExists"/>), unit-tested.
        /// </summary>
        public static String Locate(String folder, PortableTool tool, Func<String, bool> fileExists)
        {
            if (String.IsNullOrWhiteSpace(folder) || tool == null) return null;
            foreach (String candidate in new[] { Path.Combine(folder.Trim(), tool.Executable), Path.Combine(folder.Trim(), tool.Product, tool.Executable) })
                if (fileExists(candidate)) return candidate;
            return null;
        }

        /// <summary>Why the tool cannot be used from this folder (null when it can): not set, not found, or not signed by the publisher of the table.</summary>
        public static String Check(String folder, PortableTool tool, Func<String, bool> fileExists, Func<String, AuthenticodeResult> verify)
        {
            if (String.IsNullOrWhiteSpace(folder))
                return Localization.T("PortableToolReports.NoFolder", "No folder of portable tools: choose it in App > Options.");
            String path = Locate(folder, tool, fileExists);
            if (path == null)
                return Localization.F("PortableToolReports.NotFound", "{0} is neither in {1} nor in its sub-folder {2}.", tool.Executable, folder.Trim(), tool.Product);
            AuthenticodeResult r = verify(path);
            if (r == null || !SiwPolicy.IsSignedBy(r.Status == AuthenticodeStatus.Valid, r.SignerSubject, tool.Publisher))
                return Localization.F("SiwReports.BadSignature", "{0} is refused: signature {1}, signer {2}, expected O={3}.",
                    tool.Executable, r == null ? "?" : r.Status.ToString(), r == null || String.IsNullOrEmpty(r.SignerSubject) ? "-" : r.SignerSubject, tool.Publisher);
            return null;
        }

        public static String Check(String folder, PortableTool tool)
        {
            try { return Check(folder, tool, File.Exists, p => AuthenticodeVerifier.Verify(p)); }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>The request of the settings: time limit and server guard of App > Options.</summary>
        public static PortableToolRequest RequestFrom(UserSettings s, PortableTool tool)
        {
            return new PortableToolRequest
            {
                Tool = tool,
                TimeoutSeconds = PortableToolPolicy.ClampTimeout(s.ToolTimeoutSeconds),
                AllowServers = s.IncludeServers,
                AllowDomainControllers = s.IncludeServers && s.AllowDomainControllers
            };
        }

        /// <summary>The menu text of a tool: what its report holds.</summary>
        public static String Text(PortableTool tool)
        {
            switch (tool.Id)
            {
                case "uninstallview": return Localization.T("PortableToolReports.UninstallView", "Installed programs (UninstallView)...");
                case "winupdatesview": return Localization.T("PortableToolReports.WinUpdatesView", "Windows Update history (WinUpdatesView)...");
                default: return tool.Product + "...";
            }
        }

        /// <summary>Runs the tool on one computer (on a worker thread). Never throws.</summary>
        public static ToolRunResult Run(String computer, String folder, PortableToolRequest request)
        {
            PortableTool tool = request.Tool;
            ToolRunResult result = new ToolRunResult { Computer = computer };
            computer = computer.Trim();
            String admin = @"\\" + computer + @"\ADMIN$";
            String remote = Path.Combine(admin, "LiteDeploy", "work", PortableToolPolicy.WorkFolderName, tool.Id);
            StoredCredential credential;
            try { credential = UpdateRunner.UpdateCredential(); }
            catch (Exception ex) { result.Failure = ex.Message; return result; }
            try
            {
                String source = Locate(folder, tool, File.Exists);
                if (source == null) { result.Failure = Check(folder, tool); return result; }
                using (new NetworkShare(admin, credential))
                {
                    if (!Directory.Exists(admin)) throw new IOException(Localization.F("RemoteLogs.NoAccess", "{0} cannot be reached.", admin));
                    Directory.CreateDirectory(remote);
                    foreach (String old in Directory.GetFiles(remote)) File.Delete(old);
                    File.Copy(source, Path.Combine(remote, tool.Executable), true);
                }

                int minutes = (PortableToolPolicy.ClampTimeout(request.TimeoutSeconds) + 59) / 60 + 1;
                UpdateRunResult run = UpdateRunner.RunPortableTool(computer, PortableToolPolicy.WorkerArguments(request), minutes);
                WorkerMessage line = run.Messages.FirstOrDefault(m => m.Type == PortableToolPolicy.TypeResult);
                if (line == null)
                {
                    WorkerMessage error = run.Messages.FirstOrDefault(m => m.Type == WorkerProtocol.TypeError);
                    result.Failure = error != null ? error["message"] : run.Failure ?? "?";
                    return result;
                }
                int seconds;
                Int32.TryParse(line["seconds"], NumberStyles.None, CultureInfo.InvariantCulture, out seconds);
                result.Seconds = seconds;
                if (line["result"] != "ok")
                {
                    result.Failure = line["message"] ?? "?";
                    return result;
                }

                DateTime now = DateTime.Now;
                result.ReportPath = SiwReports.LocalPath(Root(tool), computer, now, "", tool.ReportExtension);
                Directory.CreateDirectory(Path.GetDirectoryName(result.ReportPath));
                using (new NetworkShare(admin, credential))
                    File.Copy(Path.Combine(remote, PortableToolPolicy.ReportFileName(tool)), result.ReportPath, true);
                result.Report = CsvReport.Parse(CsvReport.Decode(File.ReadAllBytes(result.ReportPath)));
                result.ViewPath = SiwReports.LocalPath(Root(tool), computer, now, "", "html");
                File.WriteAllText(result.ViewPath, result.Report.ToHtml(tool.Product + " - " + computer), new UTF8Encoding(false));
                result.Ok = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Failure = ex.Message;
                return result;
            }
            finally
            {
                SiwReports.RemoveRemote(admin, remote, credential);
            }
        }
    }
}
