// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SiwReports.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: SIW report of one computer: checks the SIW folder of App > Options
//          (signature of the executables, SiwPolicy.IsSignedBy), copies the
//          executable for the architecture of the computer (and siw.lic) to
//          the ADMIN$ share of the computer (LiteDeploy\work\SIW), runs the worker "siw" command
//          as SYSTEM, copies the reports to %LOCALAPPDATA%\LiteDeploy\siw\
//          <computer>\<yyyyMMdd-HHmmss>.<ext>, then removes the folder of the
//          computer, also after an error. The XML report gets a plain HTML view
//          next to it (SiwReport.ToHtml). Reports are site data: they stay in
//          the profile of the administrator.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LiteSuite;

namespace LiteDeploy
{
    /// <summary>What a SIW report run gave on one computer.</summary>
    internal sealed class SiwRunResult
    {
        public String Computer;
        public bool Ok;
        /// <summary>Why it failed (null when Ok).</summary>
        public String Failure;
        public int Seconds;
        /// <summary>Local files (null when not made): the XML report, its HTML view, the HTML report of SIW.</summary>
        public String XmlPath, ViewPath, HtmlPath;
        /// <summary>The XML report read (null when it could not be).</summary>
        public SiwReport Report;
    }

    internal static class SiwReports
    {
        /// <summary>%LOCALAPPDATA%\LiteDeploy\siw: one folder per computer.</summary>
        public static String Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "siw"); }
        }

        /// <summary>
        /// &lt;root&gt;\&lt;computer&gt;\&lt;yyyyMMdd-HHmmss&gt;&lt;suffix&gt;.&lt;ext&gt;: the computer name keeps letters, digits,
        /// '-', '_' and '.' (anything else becomes '_'; never empty, never "." or ".."). Pure logic, unit-tested.
        /// </summary>
        public static String LocalPath(String root, String computer, DateTime when, String suffix, String extension)
        {
            String name = new String((computer ?? "").Trim().Select(c => Char.IsLetterOrDigit(c) && c < 128 || c == '-' || c == '_' || c == '.' ? c : '_').ToArray());
            if (name.Trim('.').Length == 0) name = "_";
            String ext = new String((extension ?? "").Where(Char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            String file = when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (suffix ?? "") + (ext.Length > 0 ? "." + ext : "");
            return Path.Combine(root, name, file);
        }

        /// <summary>The request of the settings: format, sections, publisher, time limit, server guard of App > Options.</summary>
        public static SiwRequest RequestFrom(UserSettings s)
        {
            return new SiwRequest
            {
                Format = s.SiwHtml ? SiwFormat.Html : SiwFormat.Xml,
                Software = s.SiwSoftware,
                Hardware = s.SiwHardware,
                Network = s.SiwNetwork,
                Publisher = String.IsNullOrWhiteSpace(s.SiwPublisher) ? SiwPolicy.DefaultPublisher : s.SiwPublisher.Trim(),
                TimeoutSeconds = SiwPolicy.ClampTimeout(s.SiwTimeoutSeconds),
                AllowServers = s.IncludeServers,
                AllowDomainControllers = s.IncludeServers && s.AllowDomainControllers
            };
        }

        /// <summary>
        /// Why the SIW folder cannot be used (null when it can): not set, not found, no executable, or an
        /// executable without a valid signature of <paramref name="publisher"/>. <paramref name="verify"/>:
        /// the signature check (AuthenticodeVerifier), replaced in the tests.
        /// </summary>
        public static String CheckFolder(String folder, String publisher, Func<String, bool> fileExists, Func<String, AuthenticodeResult> verify)
        {
            if (String.IsNullOrWhiteSpace(folder))
                return Localization.T("SiwReports.NoFolder", "No SIW folder: choose the folder that holds SIW in App > Options.");
            if (!SiwPolicy.IsValidPublisher(publisher))
                return Localization.F("SiwReports.BadPublisher", "The SiwPublisher setting is not a valid name: {0}", publisher);
            List<String> present = SiwPolicy.Executables.Where(e => fileExists(Path.Combine(folder, e))).ToList();
            if (present.Count == 0)
                return Localization.F("SiwReports.NoExecutable", "The SIW folder {0} holds none of {1}.", folder, String.Join(", ", SiwPolicy.Executables));
            foreach (String exe in present)
            {
                AuthenticodeResult r = verify(Path.Combine(folder, exe));
                if (r == null || !SiwPolicy.IsSignedBy(r.Status == AuthenticodeStatus.Valid, r.SignerSubject, publisher))
                    return Localization.F("SiwReports.BadSignature", "{0} is refused: signature {1}, signer {2}, expected O={3}.",
                        exe, r == null ? "?" : r.Status.ToString(), r == null || String.IsNullOrEmpty(r.SignerSubject) ? "-" : r.SignerSubject, publisher);
            }
            return null;
        }

        /// <summary>CheckFolder on the real files.</summary>
        public static String CheckFolder(String folder, String publisher)
        {
            try { return CheckFolder(folder == null ? null : folder.Trim(), publisher, File.Exists, p => AuthenticodeVerifier.Verify(p)); }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Worker time limit (minutes, rounded up) for SIW: once per format, plus the start.</summary>
        public static int WorkerMinutes(SiwRequest request)
        {
            int runs = request.Format == SiwFormat.Html ? 2 : 1;
            return (SiwPolicy.ClampTimeout(request.TimeoutSeconds) * runs + 59) / 60 + 1;
        }

        /// <summary>Runs SIW on one computer (on a worker thread). Never throws.</summary>
        public static SiwRunResult Run(String computer, String folder, SiwRequest request)
        {
            SiwRunResult result = new SiwRunResult { Computer = computer };
            computer = computer.Trim();
            String admin = @"\\" + computer + @"\ADMIN$";
            String remote = Path.Combine(admin, "LiteDeploy", "work", SiwPolicy.WorkFolderName);
            StoredCredential credential;
            try { credential = UpdateRunner.UpdateCredential(); }
            catch (Exception ex) { result.Failure = ex.Message; return result; }

            try
            {
                // 1. SIW to the computer (the executable for its architecture, and the license)
                using (new NetworkShare(admin, credential))
                {
                    if (!Directory.Exists(admin)) throw new IOException(Localization.F("RemoteLogs.NoAccess", "{0} cannot be reached.", admin));
                    SiwArchitecture architecture = SiwPolicy.ArchitectureFrom(Directory.Exists(Path.Combine(admin, "SysArm32")), Directory.Exists(Path.Combine(admin, "SysWOW64")));
                    String exe = SiwPolicy.ExecutableFor(architecture);
                    String source = Path.Combine(folder, exe);
                    if (!File.Exists(source))
                    {
                        result.Failure = Localization.F("SiwReports.NoExecutableFor", "{0} ({1}) is not in the SIW folder.", exe, architecture);
                        return result;
                    }
                    Directory.CreateDirectory(remote);
                    foreach (String old in Directory.GetFiles(remote)) File.Delete(old);
                    File.Copy(source, Path.Combine(remote, exe), true);
                    String license = Path.Combine(folder, SiwPolicy.LicenseFileName);
                    if (File.Exists(license)) File.Copy(license, Path.Combine(remote, SiwPolicy.LicenseFileName), true);
                }

                // 2. The worker runs it as SYSTEM
                UpdateRunResult run = UpdateRunner.Siw(computer, SiwPolicy.WorkerArguments(request), WorkerMinutes(request));
                WorkerMessage line = run.Messages.FirstOrDefault(m => m.Type == SiwPolicy.TypeResult);
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

                // 3. The reports back to this computer
                DateTime now = DateTime.Now;
                using (new NetworkShare(admin, credential))
                {
                    foreach (String file in (line["files"] ?? "").Split(';').Where(f => f.Length > 0))
                    {
                        String ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                        if (ext != "xml" && ext != "html") continue;
                        String local = LocalPath(Root, computer, now, ext == "html" ? "-siw" : "", ext);
                        Directory.CreateDirectory(Path.GetDirectoryName(local));
                        File.Copy(Path.Combine(remote, Path.GetFileName(file)), local, true);
                        if (ext == "xml") result.XmlPath = local; else result.HtmlPath = local;
                    }
                }
                if (result.XmlPath == null)
                {
                    result.Failure = Localization.T("SiwReports.NoReport", "The worker made no XML report.");
                    return result;
                }
                result.Report = SiwReport.Parse(File.ReadAllText(result.XmlPath, Encoding.UTF8));
                result.ViewPath = LocalPath(Root, computer, now, "", "html");
                File.WriteAllText(result.ViewPath, result.Report.ToHtml(), new UTF8Encoding(false));
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
                RemoveRemote(admin, remote, credential);
            }
        }

        // The folder of the run on the computer: removed whatever happened (best effort, never throws)
        static void RemoveRemote(String admin, String remote, StoredCredential credential)
        {
            try
            {
                using (new NetworkShare(admin, credential))
                {
                    for (int attempt = 0; attempt < 3 && Directory.Exists(remote); attempt++)
                    {
                        try { Directory.Delete(remote, true); }
                        catch (IOException) { Thread.Sleep(2000); }
                        catch (UnauthorizedAccessException) { Thread.Sleep(2000); }
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
