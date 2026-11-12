// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ToolManager.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Local tools of LiteDeploy (Tools > Manage Tools), same idea as the
//          tools of LiteTask: PsExec (live.sysinternals.com, Microsoft signature
//          required) and LitePM (GitHub release of svtica/LitePM, LiteSuite
//          signature checked with the update policy of LiteTask). Tools are
//          installed in %LOCALAPPDATA%\LiteDeploy\tools; the copies provided by
//          LiteTask (its tools folder, parent of LiteDeploy's) are found too.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
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

        /// <summary>Publisher of the LiteSuite releases (same pin as LiteTask's UpdateManager).</summary>
        public const String LiteSuiteThumbprint = "EC2B412C4025C2F1C8DB512CEC66632E65C017F0";

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

        // ---- LitePM from its GitHub release ------------------------------------------

        [DataContract]
        sealed class Release
        {
            [DataMember(Name = "tag_name")] public String Tag;
            [DataMember(Name = "assets")] public List<Asset> Assets;
        }

        [DataContract]
        sealed class Asset
        {
            [DataMember(Name = "name")] public String Name;
            [DataMember(Name = "browser_download_url")] public String Url;
        }

        /// <summary>A release download link of github.com, over HTTPS.</summary>
        static public bool IsSafeDownloadUrl(String url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.Contains("/releases/download/");
        }

        static String InstallLitePM()
        {
            String target = System.IO.Path.Combine(PsExecDownloader.UserToolsDir, "LitePM");
            String work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LiteDeploy-LitePM-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Release release;
                using (WebClient client = NewClient())
                {
                    String json = client.DownloadString("https://api.github.com/repos/svtica/LitePM/releases/latest");
                    using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                        release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(ms);
                }
                Regex pattern = new Regex(@"^LitePM.*\.zip$", RegexOptions.IgnoreCase);
                Asset asset = (release.Assets ?? new List<Asset>()).FirstOrDefault(a => a.Name != null && pattern.IsMatch(a.Name) && IsSafeDownloadUrl(a.Url));
                if (asset == null)
                    throw new InvalidDataException(Localization.F("ToolManager.NoAsset", "The release {0} of LitePM has no LitePM.zip.", release.Tag));

                String zip = System.IO.Path.Combine(work, "LitePM.zip");
                using (WebClient client = NewClient()) client.DownloadFile(asset.Url, zip);

                // ExtractToDirectory refuses entries that would escape the folder
                String extract = System.IO.Path.Combine(work, "extract");
                ZipFile.ExtractToDirectory(zip, extract);
                String exe = Directory.GetFiles(extract, "LitePM.exe", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
                if (exe == null) throw new InvalidDataException(Localization.T("ToolManager.NoExe", "LitePM.exe is not in the downloaded package."));

                // Signature: LiteSuite publisher (or this LiteDeploy's own signer); an unsigned LitePM
                // is accepted only by an unsigned (development) LiteDeploy, as in LiteTask
                AuthenticodeResult running = AuthenticodeVerifier.Verify(System.Reflection.Assembly.GetExecutingAssembly().Location);
                AuthenticodeResult package = AuthenticodeVerifier.Verify(exe);
                UpdateSignatureVerdict verdict = UpdateSignaturePolicy.Evaluate(package, running, UpdateSignaturePolicy.ExpectedThumbprints(LiteSuiteThumbprint, running));
                if (verdict != UpdateSignatureVerdict.Accepted && verdict != UpdateSignatureVerdict.AcceptedUnsignedDevBuild)
                    throw new InvalidDataException(Localization.F("ToolManager.BadSignature", "LitePM was refused: {0} ({1}).", verdict, package));

                String source = System.IO.Path.GetDirectoryName(exe);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                CopyDirectory(source, target);
                MarkOfTheWeb.RemoveFromDirectory(target);

                String message = Localization.F("ToolManager.Installed", "{0} {1} installed in {2}.", "LitePM", release.Tag, target);
                if (verdict == UpdateSignatureVerdict.AcceptedUnsignedDevBuild)
                    message += " " + Localization.T("ToolManager.Unsigned", "Warning: LitePM is not signed.");
                return message;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch (Exception) { }
            }
        }

        static WebClient NewClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "LiteDeploy-ToolManager";   // required by the GitHub API
            client.Proxy = WebRequest.GetSystemWebProxy();
            client.Proxy.Credentials = CredentialCache.DefaultCredentials;
            return client;
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
    }
}
