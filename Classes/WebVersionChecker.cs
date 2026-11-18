// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WebVersionChecker.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Tools > Web versions...: for every instance of CachedInstaller and
//          MsixProvision that has a WingetId parameter, the latest version
//          known to winget ("winget show", x64 installer of the type of the
//          instance) against the version of the file the instance uses in the
//          shared installer cache. Runs on the administrator's computer only,
//          never on the managed computers. Results kept in
//          %LOCALAPPDATA%\LiteDeploy\web-versions.xml; optional daily check
//          (internal scheduler of the main window). Version comparison, targets,
//          winget arguments and schedule are pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using LiteDeploy.Worker;

namespace LiteDeploy
{
    public enum WebVersionStatus
    {
        UpToDate,
        NewerOnWeb,
        NotFound,
        NoWingetId,
        Error
    }

    /// <summary>Result of the web check of one module instance (one row of Tools > Web versions...).</summary>
    public class WebVersionEntry
    {
        /// <summary>"Module/Instance".</summary>
        [XmlAttribute] public String Key;
        [XmlAttribute] public String Module;
        [XmlAttribute] public String Instance;
        public String Product;
        public String WingetId;
        /// <summary>msi, exe or msix.</summary>
        public String Type;
        /// <summary>Full path of the file of the instance in the cache (null: unknown).</summary>
        public String CacheFile;
        public String CacheVersion;
        public String WebVersion;
        [XmlAttribute] public WebVersionStatus Status = WebVersionStatus.Error;
        public String InstallerType;
        public String InstallerUrl;
        public String InstallerSha256;
        public String VendorUrl;
        public String Message;
        public DateTime Checked;

        /// <summary>A new version can be downloaded: newer on the web, with an https installer and its SHA256.</summary>
        [XmlIgnore]
        public bool CanDownload
        {
            get
            {
                return Status == WebVersionStatus.NewerOnWeb && CacheDownloader.IsHttps(InstallerUrl)
                    && !String.IsNullOrEmpty(InstallerSha256) && !String.IsNullOrEmpty(CacheFile);
            }
        }
    }

    [XmlRoot("WebVersions")]
    public class WebVersionResults
    {
        public DateTime? LastCheck;
        /// <summary>Last daily check (App setting "check every day at HH:mm").</summary>
        public DateTime? LastScheduledCheck;
        [XmlElement("Entry")] public List<WebVersionEntry> Entries = new List<WebVersionEntry>();

        static public String DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "web-versions.xml"); }
        }

        /// <summary>The saved results; empty when the file is missing or unreadable.</summary>
        static public WebVersionResults Load(String path)
        {
            try
            {
                if (File.Exists(path))
                    using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        return (WebVersionResults)new XmlSerializer(typeof(WebVersionResults)).Deserialize(f) ?? new WebVersionResults();
            }
            catch (Exception) { }
            return new WebVersionResults();
        }

        public void Save(String path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            String temp = path + ".tmp";
            using (FileStream f = new FileStream(temp, FileMode.Create, FileAccess.Write))
                new XmlSerializer(typeof(WebVersionResults)).Serialize(f, this);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>Replaces the entry of the same key, or adds it.</summary>
        public void Set(WebVersionEntry entry)
        {
            int i = Entries.FindIndex(e => String.Equals(e.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) Entries[i] = entry; else Entries.Add(entry);
        }
    }

    /// <summary>One module instance to check, with the values of its parameters.</summary>
    internal sealed class WebVersionTarget
    {
        public String Module;
        public String Instance;
        public String Product;
        public String WingetId;
        /// <summary>msi, exe or msix.</summary>
        public String Type;
        public String Publisher;
        /// <summary>CachedInstaller: path under the cache; MsixProvision: file name in Folder (empty: the newest package).</summary>
        public String File;
        /// <summary>MsixProvision: folder under the cache.</summary>
        public String Folder;
        /// <summary>MsixProvision: package identity name.</summary>
        public String PackageName;
        /// <summary>CachedInstaller: Version parameter (expected version once installed), else null.</summary>
        public String Version;

        public String Key { get { return ModuleSettings.Key(Module, Instance); } }
        public bool IsMsix { get { return Module.Equals(WebVersionChecker.MsixProvision, StringComparison.OrdinalIgnoreCase); } }
    }

    internal static class WebVersionChecker
    {
        public const String CachedInstaller = "CachedInstaller";
        public const String MsixProvision = "MsixProvision";
        public const String WingetIdParameter = "WingetId";

        static int running;

        /// <summary>One check or download at a time (window and daily check): false when one runs already.</summary>
        static public bool TryBegin() { return Interlocked.CompareExchange(ref running, 1, 0) == 0; }
        static public void End() { Interlocked.Exchange(ref running, 0); }

        // ---- Log -----------------------------------------------------------------------

        static readonly Object logLock = new Object();

        /// <summary>%LOCALAPPDATA%\LiteDeploy\logs\web-versions-yyyyMMdd.log (every check and download step).</summary>
        static public void Log(String line)
        {
            lock (logLock)
            {
                try
                {
                    Directory.CreateDirectory(ScheduleRunner.LogDir);
                    System.IO.File.AppendAllText(Path.Combine(ScheduleRunner.LogDir, "web-versions-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception) { }
            }
        }

        // ---- Versions --------------------------------------------------------------------

        static readonly Regex Dotted = new Regex(@"\d+(?:\.\d+)*");
        static readonly Regex Build = new Regex(@"\(\s*(?:build\s*)?(\d+)\s*\)", RegexOptions.IgnoreCase);

        /// <summary>
        /// Numbers of a version text: the first dotted number ("v2.4.1 beta" -> 2.4.1) and the build in
        /// parentheses when there is one ("6.1.5 (12345)": build 12345; "6.1.x (12345)": 6.1, build
        /// 12345). Null when the text has no number.
        /// </summary>
        static public long[] VersionParts(String text, out long? build)
        {
            build = null;
            if (String.IsNullOrWhiteSpace(text)) return null;
            Match m = Dotted.Match(text);
            if (!m.Success) return null;
            List<long> parts = new List<long>();
            foreach (String p in m.Value.Split('.'))
            {
                long n;
                if (!Int64.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return null;
                parts.Add(n);
            }
            Match b = Build.Match(text.Substring(m.Index + m.Length));
            long bn;
            if (b.Success && Int64.TryParse(b.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out bn)) build = bn;
            return parts.ToArray();
        }

        /// <summary>
        /// Compares two versions: &gt; 0 when <paramref name="a"/> is newer. Missing parts count as 0
        /// (2.4 = 2.4.0.0, 26.06 = 26.6). A build in parentheses on one side only is compared with the
        /// part of the other side that follows the common parts ("6.1.5 (12345)" = 6.1.5.12345).
        /// Null when one of them has no number: the texts cannot be compared.
        /// </summary>
        static public int? CompareVersions(String a, String b)
        {
            long? buildA, buildB;
            long[] pa = VersionParts(a, out buildA);
            long[] pb = VersionParts(b, out buildB);
            if (pa == null || pb == null) return null;
            if (buildA.HasValue && !buildB.HasValue) { buildB = PartAt(pb, pa.Length); pb = pb.Take(pa.Length).ToArray(); }
            else if (buildB.HasValue && !buildA.HasValue) { buildA = PartAt(pa, pb.Length); pa = pa.Take(pb.Length).ToArray(); }

            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int c = PartAt(pa, i).CompareTo(PartAt(pb, i));
                if (c != 0) return c;
            }
            return (buildA ?? 0).CompareTo(buildB ?? 0);
        }

        static long PartAt(long[] parts, int i)
        {
            return i < parts.Length ? parts[i] : 0;
        }

        /// <summary>Status of an instance from its cache and web versions. Pure logic, unit-tested.</summary>
        static public WebVersionStatus StatusOf(String cacheVersion, String webVersion)
        {
            if (String.IsNullOrWhiteSpace(webVersion)) return WebVersionStatus.NotFound;
            if (String.IsNullOrWhiteSpace(cacheVersion)) return WebVersionStatus.Error;
            int? c = CompareVersions(webVersion, cacheVersion);
            if (c == null)
                return String.Equals(webVersion.Trim(), cacheVersion.Trim(), StringComparison.OrdinalIgnoreCase) ? WebVersionStatus.UpToDate : WebVersionStatus.Error;
            return c > 0 ? WebVersionStatus.NewerOnWeb : WebVersionStatus.UpToDate;
        }

        // ---- Targets ----------------------------------------------------------------------

        /// <summary>
        /// The instances of CachedInstaller and MsixProvision that have values in module-settings.xml,
        /// with or without a WingetId (without: status No WingetId). Pure logic, unit-tested.
        /// </summary>
        static public List<WebVersionTarget> Targets(ModuleSettings settings)
        {
            List<WebVersionTarget> targets = new List<WebVersionTarget>();
            foreach (String key in settings.Modules)
            {
                int slash = key.IndexOf(UpdatePackage.InstanceSeparator);
                if (slash <= 0) continue;
                String module = key.Substring(0, slash);
                String instance = key.Substring(slash + 1);
                bool msix = module.Equals(MsixProvision, StringComparison.OrdinalIgnoreCase);
                if (!msix && !module.Equals(CachedInstaller, StringComparison.OrdinalIgnoreCase)) continue;

                Func<String, String> value = name =>
                {
                    ModuleParameterSetting s = settings.Get(key, name);
                    return s == null || String.IsNullOrWhiteSpace(s.Value) ? null : s.Value.Trim();
                };
                WebVersionTarget t = new WebVersionTarget
                {
                    Module = msix ? MsixProvision : CachedInstaller,
                    Instance = instance,
                    Product = value("Product") ?? (msix ? value("PackageName") : null) ?? instance,
                    WingetId = value(WingetIdParameter),
                    Publisher = value("Publisher"),
                    File = value("File"),
                    Folder = value("Folder"),
                    PackageName = value("PackageName"),
                    Version = msix ? null : value("Version")
                };
                if (msix) t.Type = "msix";
                else
                {
                    String type = (value("Type") ?? Path.GetExtension(t.File ?? "").TrimStart('.')).ToLowerInvariant();
                    t.Type = type == "msi" || type == "exe" ? type : null;
                }
                targets.Add(t);
            }
            return targets;
        }

        /// <summary>
        /// Full path of a path relative to the cache ("CachedInstaller/Vendor/app.msi", "/" or "\");
        /// null when it is empty, rooted, has a drive or ".." or leaves the cache (same rules as the
        /// modules). Pure logic, unit-tested.
        /// </summary>
        static public String ResolveUnderCache(String cacheRoot, String relative)
        {
            if (String.IsNullOrWhiteSpace(cacheRoot) || String.IsNullOrWhiteSpace(relative)) return null;
            String rel = relative.Trim().Replace('/', '\\').TrimStart('\\');
            if (rel.Length == 0 || rel.Contains(":") || rel.Split('\\').Contains("..")) return null;
            try
            {
                if (Path.IsPathRooted(rel)) return null;
                String root = Path.GetFullPath(cacheRoot.Trim()).TrimEnd('\\') + "\\";
                String full = Path.GetFullPath(Path.Combine(root, rel));
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length ? full : null;
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        /// <summary>Arguments of "winget show" for the installer of an instance. Pure logic, unit-tested.</summary>
        static public String ShowArguments(String wingetId, String installerType)
        {
            if (!IsValidWingetId(wingetId)) throw new ArgumentException("Invalid winget package Id.", "wingetId");
            return "show --id " + wingetId + " -e --source winget --architecture x64"
                + (String.IsNullOrEmpty(installerType) ? "" : " --installer-type " + installerType)
                + " --accept-source-agreements --disable-interactivity";
        }

        static public bool IsValidWingetId(String id)
        {
            return UpdatePackage.IsValidId(id) && !id.StartsWith(UpdatePackage.ModulePrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>winget installer types of an instance type, tried in this order (an MSI can be a "wix" installer).</summary>
        static public String[] InstallerTypesOf(String type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "msi": return new[] { "msi", "wix" };
                case "exe": return new[] { "exe", "inno", "nullsoft", "burn" };
                case "msix": return new[] { "msix" };
                default: return new String[0];
            }
        }

        /// <summary>
        /// Daily check due: a time HH:mm is set and a start was due between the last daily check (or
        /// <paramref name="since"/>, the start of LiteDeploy) and now. Pure logic, unit-tested.
        /// </summary>
        static public bool IsDailyCheckDue(String time, DateTime? lastRun, DateTime now, DateTime since)
        {
            TimeSpan t;
            if (String.IsNullOrWhiteSpace(time) || !TimeSpan.TryParseExact(time.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out t)) return false;
            UpdateSchedule daily = new UpdateSchedule
            {
                Name = "web-versions",
                Days = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday",
                Time = time.Trim(),
                LastRun = lastRun
            };
            return daily.IsDue(now, since);
        }

        // ---- Check (winget, cache) ----------------------------------------------------------

        /// <summary>Checks every target; each result is logged and kept in web-versions.xml.</summary>
        static public WebVersionResults CheckAll(Action<WebVersionEntry> progress)
        {
            String cache = UserSettings.Current.UpdateCacheShare;
            List<WebVersionTarget> targets = Targets(ModuleSettings.Load());
            String winget = WingetClient.Locate();
            Log("Web check started: " + targets.Count + " instance(s), cache " + (String.IsNullOrWhiteSpace(cache) ? "(none)" : cache)
                + ", winget " + (winget ?? "not found"));
            WebVersionResults results = WebVersionResults.Load(WebVersionResults.DefaultPath);
            results.Entries.Clear();
            foreach (WebVersionTarget t in targets)
            {
                WebVersionEntry e = Check(t, cache, winget);
                Log("  " + e.Key + " [" + (e.WingetId ?? "-") + "]: " + e.Status + ", cache " + (e.CacheVersion ?? "?") + ", web " + (e.WebVersion ?? "?")
                    + (e.Message != null ? " (" + e.Message + ")" : ""));
                results.Entries.Add(e);
                if (progress != null) progress(e);
            }
            results.LastCheck = DateTime.Now;
            try { results.Save(WebVersionResults.DefaultPath); }
            catch (Exception ex) { Log("  results file: " + ex.Message); }
            Log("Web check finished: " + results.Entries.Count(e => e.Status == WebVersionStatus.NewerOnWeb) + " newer on the web");
            return results;
        }

        /// <summary>Checks one instance: version of its file in the cache, then winget.</summary>
        static public WebVersionEntry Check(WebVersionTarget t, String cacheRoot, String winget)
        {
            WebVersionEntry e = new WebVersionEntry
            {
                Key = t.Key, Module = t.Module, Instance = t.Instance, Product = t.Product, WingetId = t.WingetId,
                Type = t.Type, Checked = DateTime.Now
            };
            String cacheError = null;
            try
            {
                e.CacheFile = CacheFileOf(t, cacheRoot);
                e.CacheVersion = String.IsNullOrEmpty(t.Version) ? FileVersionOf(e.CacheFile, t.Type) : t.Version;
                if (String.IsNullOrEmpty(e.CacheVersion))
                    cacheError = Localization.F("WebVersions.NoCacheVersion", "Version of {0} unknown: set the Version parameter.", Path.GetFileName(e.CacheFile));
            }
            catch (Exception ex) { cacheError = ex.Message; }

            if (String.IsNullOrEmpty(t.WingetId))
            {
                e.Status = WebVersionStatus.NoWingetId;
                e.Message = cacheError;
                return e;
            }
            if (!IsValidWingetId(t.WingetId))
            {
                e.Status = WebVersionStatus.Error;
                e.Message = Localization.F("WebVersions.InvalidWingetId", "Invalid WingetId: {0}", t.WingetId);
                return e;
            }
            if (winget == null)
            {
                e.Status = WebVersionStatus.Error;
                e.Message = Localization.T("WebVersions.NoWinget", "winget is not available on this computer.");
                return e;
            }

            WingetShowInfo web = null;
            try
            {
                foreach (String type in InstallerTypesOf(t.Type).DefaultIfEmpty(null))
                {
                    int exitCode;
                    String output = RunWinget(winget, ShowArguments(t.WingetId, type), TimeSpan.FromMinutes(2), out exitCode);
                    WingetShowInfo info = WingetShowParser.Parse(output);
                    if (!info.Found)
                    {
                        if (exitCode == -1) throw new TimeoutException(Localization.T("WebVersions.WingetTimeout", "winget did not answer within 2 minutes."));
                        // "winget show" answers 0x8A150014 (no package found) for an unknown Id
                        if (exitCode != 0 && exitCode != WingetClient.NoPackageFound && exitCode != WingetClient.PackageNotInstalled)
                            throw new InvalidOperationException("winget " + WingetClient.Hex(exitCode) + ": " + WingetClient.Describe(exitCode));
                        break;
                    }
                    if (web == null || info.InstallerUrl != null) web = info;
                    if (info.InstallerUrl != null) break;
                }
            }
            catch (Exception ex)
            {
                e.Status = WebVersionStatus.Error;
                e.Message = ex.Message;
                return e;
            }

            if (web == null || !web.Found)
            {
                e.Status = WebVersionStatus.NotFound;
                e.Message = Localization.F("WebVersions.NotInWinget", "{0} is not in the winget source.", t.WingetId);
                return e;
            }
            e.WebVersion = web.Version;
            e.InstallerType = web.InstallerType;
            e.InstallerUrl = web.InstallerUrl;
            e.InstallerSha256 = web.InstallerSha256;
            e.VendorUrl = web.VendorUrl;
            if (cacheError != null)
            {
                e.Status = WebVersionStatus.Error;
                e.Message = cacheError;
                return e;
            }
            e.Status = StatusOf(e.CacheVersion, e.WebVersion);
            if (e.Status == WebVersionStatus.Error)
                e.Message = Localization.F("WebVersions.CannotCompare", "Versions cannot be compared: {0} / {1}", e.CacheVersion, e.WebVersion);
            else if (e.Status == WebVersionStatus.NewerOnWeb && e.InstallerUrl == null)
                e.Message = Localization.F("WebVersions.NoInstaller", "No x64 {0} installer in the winget manifest: download it from the vendor.", t.Type);
            return e;
        }

        /// <summary>Full path of the file of the instance in the cache; throws with the reason.</summary>
        static public String CacheFileOf(WebVersionTarget t, String cacheRoot)
        {
            if (String.IsNullOrWhiteSpace(cacheRoot))
                throw new InvalidOperationException(Localization.T("WebVersions.NoCache", "No shared installer cache: set one in App > Options > Software updates."));
            if (!t.IsMsix)
            {
                String full = ResolveUnderCache(cacheRoot, t.File);
                if (full == null) throw new InvalidOperationException(Localization.F("WebVersions.InvalidFile", "Invalid File parameter: {0}", t.File ?? ""));
                if (!System.IO.File.Exists(full)) throw new FileNotFoundException(Localization.F("WebVersions.FileMissing", "File not found in the cache: {0}", full));
                return full;
            }

            String folder = ResolveUnderCache(cacheRoot, t.Folder);
            if (folder == null) throw new InvalidOperationException(Localization.F("WebVersions.InvalidFolder", "Invalid Folder parameter: {0}", t.Folder ?? ""));
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(Localization.F("WebVersions.FolderMissing", "Folder not found in the cache: {0}", folder));
            if (!String.IsNullOrEmpty(t.File))
            {
                if (t.File != Path.GetFileName(t.File) || t.File.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException(Localization.F("WebVersions.InvalidFile", "Invalid File parameter: {0}", t.File));
                String path = Path.Combine(folder, t.File);
                if (!System.IO.File.Exists(path)) throw new FileNotFoundException(Localization.F("WebVersions.FileMissing", "File not found in the cache: {0}", path));
                return path;
            }
            // File empty: the newest package of that name, as the module picks it
            String newest = null;
            String newestVersion = null;
            foreach (String path in Directory.GetFiles(folder).Where(p => p.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)))
            {
                MsixManifest m;
                try { m = MsixManifest.Read(path); }
                catch (Exception) { continue; }
                if (!String.Equals(m.Name, t.PackageName, StringComparison.Ordinal)) continue;
                if (newest == null || CompareVersions(m.Version, newestVersion) > 0) { newest = path; newestVersion = m.Version; }
            }
            if (newest == null) throw new FileNotFoundException(Localization.F("WebVersions.NoPackage", "No .msix or .msixbundle of the package {0} in {1}", t.PackageName ?? "", folder));
            return newest;
        }

        /// <summary>Version of an installer file: MSI ProductVersion, EXE product (else file) version, MSIX manifest version.</summary>
        static public String FileVersionOf(String path, String type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "msi": return MsiProductVersion(path);
                case "msix": return MsixManifest.Read(path).Version;
                default:
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                    foreach (String v in new[] { info.ProductVersion, info.FileVersion })
                    {
                        long? build;
                        if (VersionParts(v, out build) != null) return v.Trim();
                    }
                    return null;
            }
        }

        /// <summary>ProductVersion of the Property table of an MSI, read without installing it.</summary>
        static public String MsiProductVersion(String path)
        {
            Type type = Type.GetTypeFromProgID("WindowsInstaller.Installer");
            if (type == null) return null;
            Object installer = Activator.CreateInstance(type);
            Object db = null, view = null, record = null;
            try
            {
                db = Invoke(installer, "OpenDatabase", path, 0);
                view = Invoke(db, "OpenView", "SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'");
                Invoke(view, "Execute");
                record = Invoke(view, "Fetch");
                return record == null ? null : (String)record.GetType().InvokeMember("StringData", BindingFlags.GetProperty, null, record, new Object[] { 1 });
            }
            finally
            {
                if (view != null) { try { Invoke(view, "Close"); } catch (Exception) { } }
                foreach (Object o in new[] { record, view, db, installer })
                    if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
        }

        static Object Invoke(Object target, String method, params Object[] args)
        {
            return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
        }

        /// <summary>Runs winget with UTF-8 output; exit code -1 on time-out.</summary>
        static String RunWinget(String winget, String arguments, TimeSpan timeout, out int exitCode)
        {
            ProcessStartInfo psi = new ProcessStartInfo(winget, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (Process p = Process.Start(psi))
            {
                Task<String> stdout = p.StandardOutput.ReadToEndAsync();
                Task<String> stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try { p.Kill(); } catch (Exception) { }
                    exitCode = -1;
                }
                else
                {
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                }
                Task.WaitAll(new Task[] { stdout, stderr }, 10000);
                return (stdout.IsCompleted ? stdout.Result : "") + (stderr.IsCompleted ? stderr.Result : "");
            }
        }

        // ---- Daily check ---------------------------------------------------------------------

        /// <summary>
        /// The daily check (main window timer): report only, unless "Download new versions
        /// automatically" is on. Returns a one-line summary for the console.
        /// </summary>
        static public String RunScheduled(bool autoDownload)
        {
            WebVersionResults results = CheckAll(null);
            int newer = results.Entries.Count(e => e.Status == WebVersionStatus.NewerOnWeb);
            int downloaded = 0, failed = 0;
            if (autoDownload)
            {
                CacheDownloader downloader = new CacheDownloader(Log);
                foreach (WebVersionEntry e in results.Entries.Where(x => x.CanDownload).ToList())
                {
                    try
                    {
                        CacheDownloadResult r = downloader.Download(e, UserSettings.Current.UpdateCacheShare);
                        results.Set(r.Entry);
                        downloaded++;
                    }
                    catch (Exception ex)
                    {
                        Log("  " + e.Key + ": download failed: " + ex.Message);
                        failed++;
                    }
                }
            }
            results.LastScheduledCheck = DateTime.Now;
            try { results.Save(WebVersionResults.DefaultPath); }
            catch (Exception ex) { Log("  results file: " + ex.Message); }
            return autoDownload
                ? Localization.F("WebVersions.ScheduledDownload", "Web versions checked: {0} newer on the web, {1} downloaded to the cache, {2} failed.", newer, downloaded, failed)
                : Localization.F("WebVersions.ScheduledReport", "Web versions checked: {0} newer on the web (Tools > Web versions...).", newer);
        }
    }
}
