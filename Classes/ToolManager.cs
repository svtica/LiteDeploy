// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ToolManager.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Local tools of LiteDeploy (Tools > Manage Tools), same idea as the
//          tools of LiteTask: PsExec (live.sysinternals.com, Microsoft signature
//          required) and LitePM (GitHub release of svtica/LitePM, downloaded,
//          extracted and checked with LiteSuite.Common: GitHubReleaseClient,
//          UpdatePackage, LiteSuite signature policy). Tools are
//          installed in %LOCALAPPDATA%\LiteDeploy\tools; the copies provided by
//          LiteTask (its tools folder, parent of LiteDeploy's) are found too.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LiteSuite;

namespace LiteDeploy
{
    public sealed class ToolInfo
    {
        public String Id;
        public String DisplayName;
        public String Description;
        /// <summary>Path of the executable found, or null.</summary>
        public String Path;
        public String Version;
        public String Source;
    }

    static public class ToolManager
    {
        public const String PsExecId = "PsExec";
        public const String LitePMId = "LitePM";

        /// <summary>Publisher of the LiteSuite releases (pinned in LiteSuite.Common, as in LiteTask).</summary>
        public const String LiteSuiteThumbprint = SelfUpdaterOptions.LiteSuiteThumbprint;

        static String AppDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }

        static String ParentDir
        {
            get { DirectoryInfo parent = Directory.GetParent(AppDir); return parent == null ? null : parent.FullName; }
        }

        static public List<ToolInfo> List()
        {
            PsExecLocator.Find(AppDir);
            String litePM = FindLitePM();
            return new List<ToolInfo>
            {
                new ToolInfo
                {
                    Id = PsExecId,
                    DisplayName = "PsExec",
                    Description = Localization.T("ToolManager.PsExec", "Microsoft Sysinternals. Runs the deployments and the software updates."),
                    Path = PsExecLocator.Path,
                    Version = FileVersion(PsExecLocator.Path),
                    Source = "live.sysinternals.com"
                },
                new ToolInfo
                {
                    Id = LitePMId,
                    DisplayName = "LitePM",
                    Description = Localization.T("ToolManager.LitePM", "Lite Process Manager (LiteSuite): processes, services and network of this computer."),
                    Path = litePM,
                    Version = FileVersion(litePM),
                    Source = "github.com/svtica/LitePM"
                }
            };
        }

        /// <summary>LitePM.exe: LiteDeploy's tools folder, then LiteTask's (LiteTaskData\tools\LitePM).</summary>
        static public String FindLitePM()
        {
            List<String> candidates = new List<String> { System.IO.Path.Combine(PsExecDownloader.UserToolsDir, "LitePM", "LitePM.exe") };
            if (ParentDir != null) candidates.Add(System.IO.Path.Combine(ParentDir, "LitePM", "LitePM.exe"));
            return candidates.FirstOrDefault(File.Exists);
        }

        static String FileVersion(String path)
        {
            if (path == null || !File.Exists(path)) return null;
            try { return FileVersionInfo.GetVersionInfo(path).FileVersion; }
            catch (Exception) { return null; }
        }

        /// <summary>Downloads or updates a tool. Returns a message for the user; throws on failure.</summary>
        static public String Install(String id)
        {
            switch (id)
            {
                case PsExecId:
                    String path = PsExecDownloader.Download(AppDir);
                    PsExecLocator.Find(AppDir);
                    return Localization.F("ToolManager.Installed", "{0} {1} installed in {2}.", "PsExec", FileVersion(path), System.IO.Path.GetDirectoryName(path));
                case LitePMId:
                    return InstallLitePM();
                default:
                    throw new ArgumentException("Unknown tool: " + id);
            }
        }

        // ---- LitePM from its GitHub release (LiteSuite.Common) ----------------------------

        const String LitePMOwner = "svtica";
        const String LitePMRepository = "LitePM";

        /// <summary>A release download link of svtica/LitePM on github.com, over HTTPS (UpdateDownloadPolicy).</summary>
        static public bool IsSafeDownloadUrl(String url)
        {
            return UpdateDownloadPolicy.IsSafeDownloadUrl(url, LitePMOwner, LitePMRepository);
        }

        // Runs on a worker thread (ToolsForm): the asynchronous calls of GitHubReleaseClient are waited for
        static String InstallLitePM()
        {
            String target = System.IO.Path.Combine(PsExecDownloader.UserToolsDir, "LitePM");
            String work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LiteDeploy-LitePM-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                GitHubRelease release;
                String zip = System.IO.Path.Combine(work, "LitePM.zip");
                using (GitHubReleaseClient client = new GitHubReleaseClient("LiteDeploy-ToolManager"))
                {
                    release = client.GetLatestAsync(LitePMOwner, LitePMRepository, CancellationToken.None).GetAwaiter().GetResult();
                    GitHubReleaseAsset asset = UpdateDownloadPolicy.FindPackage(release, LitePMOwner, LitePMRepository, @"^LitePM.*\.zip$", "LitePM.zip");
                    if (asset == null)
                        throw new InvalidDataException(Localization.F("ToolManager.NoAsset", "The release {0} of LitePM has no LitePM.zip.", release.TagName));
                    client.DownloadAsync(asset, LitePMOwner, LitePMRepository, zip, CancellationToken.None).GetAwaiter().GetResult();
                }

                // Extract refuses entries that would escape the folder; LiteSuite's UpdatePackage
                // (LiteDeploy has its own UpdatePackage: the software update catalog)
                String extract = System.IO.Path.Combine(work, "extract");
                LiteSuite.UpdatePackage.Extract(zip, extract);
                String exe = LiteSuite.UpdatePackage.LocateExecutable(extract, "LitePM.exe");
                if (exe == null) throw new InvalidDataException(Localization.T("ToolManager.NoExe", "LitePM.exe is not in the downloaded package."));

                // Signature: LiteSuite publisher (or this LiteDeploy's own signer); an unsigned LitePM
                // is accepted only by an unsigned (development) LiteDeploy, as in LiteTask
                UpdateSignatureVerdict verdict;
                try
                {
                    AuthenticodeResult signature;
                    verdict = LiteSuite.UpdatePackage.VerifySignature(exe, LiteSuiteThumbprint, System.Reflection.Assembly.GetExecutingAssembly().Location, out signature);
                }
                catch (UpdateException ex) when (ex.Failure == UpdateFailure.SignatureRejected)
                {
                    throw new InvalidDataException(Localization.F("ToolManager.BadSignature", "LitePM was refused: {0} ({1}).", ex.Verdict, ex.Signature), ex);
                }

                String source = System.IO.Path.GetDirectoryName(exe);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                CopyDirectory(source, target);
                MarkOfTheWeb.RemoveFromDirectory(target);

                String message = Localization.F("ToolManager.Installed", "{0} {1} installed in {2}.", "LitePM", release.TagName, target);
                if (verdict == UpdateSignatureVerdict.AcceptedUnsignedDevBuild)
                    message += " " + Localization.T("ToolManager.Unsigned", "Warning: LitePM is not signed.");
                return message;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch (Exception) { }
            }
        }

        static void CopyDirectory(String source, String target)
        {
            Directory.CreateDirectory(target);
            foreach (String file in Directory.GetFiles(source))
                File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetFileName(file)), true);
            foreach (String dir in Directory.GetDirectories(source))
                CopyDirectory(dir, System.IO.Path.Combine(target, System.IO.Path.GetFileName(dir)));
        }

        /// <summary>Starts a tool (LitePM).</summary>
        static public void Open(ToolInfo tool)
        {
            if (tool == null || tool.Path == null) return;
            using (Process.Start(new ProcessStartInfo(tool.Path) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(tool.Path) })) { }
        }

        /// <summary>A computer name or address LitePM accepts after -REMOTE: letters, digits, . - _ only.</summary>
        static public bool IsValidComputerName(String computer)
        {
            if (String.IsNullOrWhiteSpace(computer) || computer.Length > 253) return false;
            return computer.All(c => Char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_');
        }

        /// <summary>Arguments that open LitePM connected to a computer through WMI, as the current
        /// user (LitePM 'Unreleased' or later). A second LitePM may run next to a local one.</summary>
        static public String LitePMRemoteArguments(String computer)
        {
            if (!IsValidComputerName(computer)) throw new ArgumentException(Localization.F("ToolManager.BadComputer", "Invalid computer name: {0}", computer));
            return "-DONOTCHECKPREVINSTANCE -REMOTE " + computer;
        }

        /// <summary>Starts LitePM connected to a remote computer.</summary>
        static public void OpenLitePM(ToolInfo tool, String computer)
        {
            if (tool == null || tool.Path == null) return;
            using (Process.Start(new ProcessStartInfo(tool.Path, LitePMRemoteArguments(computer)) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(tool.Path) })) { }
        }
    }
}
