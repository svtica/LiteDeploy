// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CacheDownloader.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Tools > Web versions... > Download to the cache: puts the new version
//          of a module instance (CachedInstaller, MsixProvision) found by the
//          web check into the shared installer cache, from the administrator's
//          computer:
//            1. download over HTTPS only (redirects too) to a new temporary
//               folder of the administrator;
//            2. SHA256 of the winget manifest, then Authenticode signature:
//               Valid and O=<Publisher> of the instance; for an MSIX, the
//               package name and the signer = Publisher of its manifest, as
//               the module checks them;
//            3. copy to the folder of the current file, under a new versioned
//               name (never over an existing file), SHA256 checked again;
//            4. File (and Version when it was set) of the instance updated in
//               module-settings.xml;
//            5. the previous file moved to the OLD subfolder of that folder.
//          Only the file the instance uses is moved; nothing else in the cache
//          is touched. Every step is logged. Naming and rotation are pure or
//          file-only logic, unit-tested on a temporary folder.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using LiteSuite;

namespace LiteDeploy
{
    internal sealed class CacheDownloadResult
    {
        /// <summary>Entry updated: new cache file and version, status Up to date.</summary>
        public WebVersionEntry Entry;
        /// <summary>New value of the File parameter.</summary>
        public String NewFileParameter;
        /// <summary>Where the previous file went (null: not moved).</summary>
        public String OldFileMovedTo;
        /// <summary>Not fatal (the new version is in place): why the previous file stayed.</summary>
        public String Warning;
    }

    internal sealed class CacheDownloader
    {
        public const String OldFolder = "OLD";
        const int MaxRedirects = 10;

        readonly Action<String> log;

        public CacheDownloader(Action<String> log)
        {
            this.log = log ?? (s => { });
        }

        // ---- Pure logic ----------------------------------------------------------------------

        /// <summary>True for an absolute https:// URL.</summary>
        static public bool IsHttps(String url)
        {
            Uri uri;
            return !String.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Valid signature whose signer has O=<paramref name="organisation"/> (quoted when it has a
        /// comma), like the Publisher check of the modules. Pure logic, unit-tested.
        /// </summary>
        static public bool IsSignedBy(AuthenticodeResult result, String organisation)
        {
            if (result == null || result.Status != AuthenticodeStatus.Valid || String.IsNullOrEmpty(result.SignerSubject) || String.IsNullOrWhiteSpace(organisation))
                return false;
            String pattern = "(^|,\\s*)O=(\"?)" + Regex.Escape(organisation.Trim()) + "\\2(,|$)";
            return Regex.IsMatch(result.SignerSubject, pattern);
        }

        /// <summary>Two distinguished names in the same canonical form (MSIX: signer = Publisher of the manifest).</summary>
        static public bool SameDistinguishedName(String a, String b)
        {
            if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return new X500DistinguishedName(a).Decode(X500DistinguishedNameFlags.None)
                    == new X500DistinguishedName(b).Decode(X500DistinguishedNameFlags.None);
            }
            catch (CryptographicException) { return false; }
        }

        /// <summary>
        /// Name of the new file: the current name with its version replaced by the new one
        /// ("App-2.4.1.msi" -> "App-2.5.0.msi"), else with "-&lt;version&gt;" added; extension of the
        /// download when given. Pure logic, unit-tested.
        /// </summary>
        static public String VersionedFileName(String currentName, IEnumerable<String> oldVersions, String newVersion, String extension)
        {
            String ext = String.IsNullOrEmpty(extension) ? Path.GetExtension(currentName) : (extension.StartsWith(".") ? extension : "." + extension);
            String stem = Path.GetFileNameWithoutExtension(currentName);
            String version = SafeVersion(newVersion);
            String result = null;

            foreach (String old in (oldVersions ?? new String[0]).Where(v => !String.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).OrderByDescending(v => v.Length))
            {
                int at = stem.LastIndexOf(old, StringComparison.OrdinalIgnoreCase);
                if (at >= 0 && IsBoundary(stem, at - 1) && IsBoundary(stem, at + old.Length))
                {
                    result = stem.Substring(0, at) + version + stem.Substring(at + old.Length);
                    break;
                }
            }
            if (result == null)
            {
                MatchCollection dotted = Regex.Matches(stem, @"(?<![0-9A-Za-z])\d+(?:\.\d+)+(?![0-9A-Za-z])");
                if (dotted.Count > 0)
                {
                    Match last = dotted[dotted.Count - 1];
                    result = stem.Substring(0, last.Index) + version + stem.Substring(last.Index + last.Length);
                }
            }
            if (result == null || String.Equals(result, stem, StringComparison.OrdinalIgnoreCase)) result = stem + "-" + version;
            return result + ext;
        }

        static bool IsBoundary(String s, int i)
        {
            return i < 0 || i >= s.Length || !Char.IsLetterOrDigit(s[i]);
        }

        /// <summary>A version usable in a file name: letters, digits and . - _ only.</summary>
        static public String SafeVersion(String version)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (version ?? "").Trim())
                sb.Append(c < 128 && (Char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') ? c : '_');
            String s = Regex.Replace(sb.ToString(), "_+", "_").Trim('_', '.');
            return s.Length == 0 ? "new" : s;
        }

        /// <summary>The File parameter after the change: same folder part (and separator), new name.</summary>
        static public String NewFileParameter(String oldParameter, String newName)
        {
            String p = oldParameter ?? "";
            int cut = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
            return cut < 0 ? newName : p.Substring(0, cut + 1) + newName;
        }

        /// <summary>
        /// Where the previous file goes: &lt;folder&gt;\OLD\&lt;name&gt;, or with a time stamp when that
        /// name is taken (nothing is overwritten in OLD either).
        /// </summary>
        static public String OldDestination(String folder, String fileName, Func<String, bool> exists, DateTime now)
        {
            String dir = Path.Combine(folder, OldFolder);
            String target = Path.Combine(dir, fileName);
            if (!exists(target)) return target;
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName) + "-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(fileName));
        }

        static public String Sha256Of(String path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        // ---- File steps (unit-tested on a temporary folder) --------------------------------

        /// <summary>
        /// Copies the verified download into the cache folder as <paramref name="newName"/> (a
        /// ".partial" file first, its SHA256 checked, then renamed: an existing file is never
        /// overwritten; one with the same SHA256 is kept as it is). Returns the path in the cache.
        /// </summary>
        public String CopyIntoCache(String download, String sha256, String folder, String newName)
        {
            String target = Path.Combine(folder, newName);
            if (File.Exists(target))
            {
                if (!String.Equals(Sha256Of(target), sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(Localization.F("CacheDownloader.TargetExists", "{0} already exists in the cache with another content: nothing was changed.", target));
                log("  " + newName + " is already in the cache with the same SHA256: kept as it is");
            }
            else
            {
                String partial = target + ".partial";
                if (File.Exists(partial)) File.Delete(partial);   // left by an interrupted download of the same version
                log("  copy to " + partial);
                using (FileStream source = new FileStream(download, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream copy = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(copy);
                String copied = Sha256Of(partial);
                if (!String.Equals(copied, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(partial);
                    throw new IOException(Localization.F("CacheDownloader.CopyHash", "The copy in the cache does not have the expected SHA256 ({0}): it was deleted.", copied));
                }
                File.Move(partial, target);   // fails if the name was taken meanwhile: never overwrites
                log("  " + target + " in place (SHA256 " + copied + ")");
            }
            return target;
        }

        /// <summary>
        /// Moves the file the instance used before (<paramref name="previous"/>) to the OLD subfolder
        /// of its folder, unless it is the new file itself. Returns where it went (null: not moved);
        /// <paramref name="warning"/>: why it stayed (not fatal: the new version is already in place).
        /// </summary>
        public String MoveToOld(String previous, String newFile, out String warning)
        {
            warning = null;
            if (String.IsNullOrEmpty(previous) || String.Equals(Path.GetFullPath(previous), Path.GetFullPath(newFile), StringComparison.OrdinalIgnoreCase)
                || !File.Exists(previous))
                return null;
            try
            {
                String old = OldDestination(Path.GetDirectoryName(previous), Path.GetFileName(previous), File.Exists, DateTime.Now);
                Directory.CreateDirectory(Path.GetDirectoryName(old));
                File.Move(previous, old);
                log("  previous file moved to " + old);
                return old;
            }
            catch (Exception ex)
            {
                warning = Localization.F("CacheDownloader.OldNotMoved", "The previous file {0} could not be moved to {1}: {2}", previous, OldFolder, ex.Message);
                log("  WARNING previous file not moved: " + ex.Message);
                return null;
            }
        }

        /// <summary>Throws with a clear message when the administrator cannot write to the folder.</summary>
        public void AssertWritable(String folder)
        {
            String probe = Path.Combine(folder, ".litedeploy-write-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) { }
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                log("  no write access to " + folder + ": " + ex.Message);
                throw new UnauthorizedAccessException(Localization.F("CacheDownloader.NoWriteAccess",
                    "No write access to {0} ({1}). Downloading to the cache needs write access to the folder of the installer: only the administrators who fill the cache have it.",
                    folder, ex.Message));
            }
        }

        // ---- Download -------------------------------------------------------------------------

        /// <summary>
        /// Downloads the new version of the instance of <paramref name="entry"/> into the cache (see
        /// the steps above). Throws with a readable message; nothing is changed in the cache or the
        /// settings then (except the previous file, which is moved only once the new one is in place).
        /// </summary>
        public CacheDownloadResult Download(WebVersionEntry entry, String cacheRoot)
        {
            log("Download " + entry.Key + " [" + entry.WingetId + "] " + entry.CacheVersion + " -> " + entry.WebVersion + " from " + entry.InstallerUrl);
            if (!entry.CanDownload)
                throw new InvalidOperationException(Localization.T("CacheDownloader.NothingToDownload", "No newer version with an HTTPS installer and its SHA256: check again first."));

            // The instance as it is now (not as it was at the check)
            ModuleSettings settings = ModuleSettings.Load();
            WebVersionTarget target = WebVersionChecker.Targets(settings).FirstOrDefault(t => String.Equals(t.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
            if (target == null || !String.Equals(target.WingetId, entry.WingetId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Localization.F("CacheDownloader.Changed", "The parameters of {0} changed since the check: check again.", entry.Key));
            String current = WebVersionChecker.CacheFileOf(target, cacheRoot);
            if (!String.Equals(Path.GetFullPath(current), Path.GetFullPath(entry.CacheFile), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Localization.F("CacheDownloader.Changed", "The parameters of {0} changed since the check: check again.", entry.Key));
            if (!target.IsMsix && String.IsNullOrWhiteSpace(target.Publisher))
                throw new InvalidOperationException(Localization.F("CacheDownloader.NoPublisher", "{0}: the Publisher parameter is required to check the signature of the download.", entry.Key));

            String folder = Path.GetDirectoryName(current);
            log("  cache folder " + folder);
            AssertWritable(folder);

            String temp = Path.Combine(Path.GetTempPath(), "LiteDeploy-web-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                String extension = DownloadExtension(entry.InstallerUrl, Path.GetExtension(current));
                String download = Path.Combine(temp, "download" + extension);
                log("  download to " + download);
                DownloadHttps(entry.InstallerUrl, download);

                String sha = Sha256Of(download);
                if (!String.Equals(sha, entry.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Localization.F("CacheDownloader.HashMismatch",
                        "The SHA256 of the download ({0}) is not the one of the winget manifest ({1}): refused.", sha, entry.InstallerSha256));
                log("  SHA256 matches the winget manifest: " + sha);

                AuthenticodeResult signature = AuthenticodeVerifier.Verify(download);
                log("  signature: " + signature);
                if (target.IsMsix) CheckMsix(download, target, signature);
                else if (!IsSignedBy(signature, target.Publisher))
                    throw new InvalidDataException(Localization.F("CacheDownloader.BadSignature",
                        "The download is not validly signed by {0} ({1}): refused.", target.Publisher, signature.ToString()));
                log("  signature accepted");

                // The downloaded file must not be older than the one of the cache
                String newFileVersion = WebVersionChecker.FileVersionOf(download, target.Type);
                String oldFileVersion = null;
                try { oldFileVersion = WebVersionChecker.FileVersionOf(current, target.Type); } catch (Exception) { }
                log("  version of the download " + (newFileVersion ?? "?") + ", of the cache file " + (oldFileVersion ?? "?"));
                if (WebVersionChecker.CompareVersions(newFileVersion, oldFileVersion) < 0)
                    throw new InvalidDataException(Localization.F("CacheDownloader.Older", "The download ({0}) is older than the file of the cache ({1}): refused.", newFileVersion, oldFileVersion));

                String newName = VersionedFileName(Path.GetFileName(current), new[] { entry.CacheVersion, oldFileVersion }, entry.WebVersion, extension);
                String placed = CopyIntoCache(download, sha, folder, newName);

                // Settings first (the computers get the new file at their next run), then the previous file to OLD
                String newParameter = target.IsMsix ? newName : NewFileParameter(target.File, newName);
                settings = ModuleSettings.Load();
                settings.SetValue(entry.Key, "File", newParameter);
                if (!target.IsMsix && !String.IsNullOrEmpty(target.Version))
                {
                    settings.SetValue(entry.Key, "Version", entry.WebVersion);
                    log("  Version parameter " + target.Version + " -> " + entry.WebVersion);
                }
                settings.Save();
                log("  File parameter " + (target.File ?? "(empty)") + " -> " + newParameter + " (module-settings.xml)");

                String warning;
                String movedTo = MoveToOld(current, placed, out warning);

                WebVersionEntry updated = Clone(entry);
                updated.CacheFile = placed;
                updated.CacheVersion = !target.IsMsix && !String.IsNullOrEmpty(target.Version) ? entry.WebVersion : (newFileVersion ?? entry.WebVersion);
                updated.Status = WebVersionChecker.StatusOf(updated.CacheVersion, entry.WebVersion);
                updated.Message = warning;
                updated.Checked = DateTime.Now;
                log("Download " + entry.Key + " done: " + newName);
                return new CacheDownloadResult { Entry = updated, NewFileParameter = newParameter, OldFileMovedTo = movedTo, Warning = warning };
            }
            catch (Exception ex)
            {
                log("Download " + entry.Key + " FAILED: " + ex.Message);
                throw;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
                catch (Exception ex) { log("  temporary folder " + temp + " not removed: " + ex.Message); }
            }
        }

        // MSIX: the package of the instance, signed by the Publisher of its own manifest (and by the
        // organisation of the Publisher parameter when set), as the module checks it before provisioning
        void CheckMsix(String download, WebVersionTarget target, AuthenticodeResult signature)
        {
            MsixManifest m = MsixManifest.Read(download);
            log("  manifest " + m.Name + " " + m.Version + ", publisher " + m.Publisher);
            if (!String.Equals(m.Name, target.PackageName, StringComparison.Ordinal))
                throw new InvalidDataException(Localization.F("CacheDownloader.OtherPackage", "The download is the package {0}, not {1}: refused.", m.Name, target.PackageName));
            if (signature == null || signature.Status != AuthenticodeStatus.Valid || !SameDistinguishedName(signature.SignerSubject, m.Publisher))
                throw new InvalidDataException(Localization.F("CacheDownloader.MsixSigner", "The package is not validly signed by the Publisher of its manifest ({0}): refused.", m.Publisher));
            if (!String.IsNullOrWhiteSpace(target.Publisher) && !IsSignedBy(signature, target.Publisher))
                throw new InvalidDataException(Localization.F("CacheDownloader.BadSignature",
                    "The download is not validly signed by {0} ({1}): refused.", target.Publisher, signature.ToString()));
        }

        // Extension of the installer: from the URL when it is a known one, else the one of the cache file
        static String DownloadExtension(String url, String fallback)
        {
            String path = new Uri(url).AbsolutePath;
            String ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".msi" || ext == ".exe" || ext == ".msix" || ext == ".msixbundle" ? ext : fallback;
        }

        static WebVersionEntry Clone(WebVersionEntry e)
        {
            return new WebVersionEntry
            {
                Key = e.Key, Module = e.Module, Instance = e.Instance, Product = e.Product, WingetId = e.WingetId, Type = e.Type,
                CacheFile = e.CacheFile, CacheVersion = e.CacheVersion, WebVersion = e.WebVersion, Status = e.Status,
                InstallerType = e.InstallerType, InstallerUrl = e.InstallerUrl, InstallerSha256 = e.InstallerSha256,
                VendorUrl = e.VendorUrl, Message = e.Message, Checked = e.Checked
            };
        }

        // HTTPS only, redirects included (each one followed by hand and refused unless https)
        void DownloadHttps(String url, String path)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Uri uri = new Uri(url);
            for (int i = 0; i <= MaxRedirects; i++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException(Localization.F("CacheDownloader.NotHttps", "Download refused: not an HTTPS address ({0}).", uri.GetLeftPart(UriPartial.Path)));
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false;
                request.Timeout = 60000;
                request.ReadWriteTimeout = 120000;
                request.UserAgent = "LiteDeploy";
                // Corporate proxy: the Windows settings, with the user's credentials
                request.Proxy = WebRequest.GetSystemWebProxy();
                request.Proxy.Credentials = CredentialCache.DefaultCredentials;
                using (HttpWebResponse response = GetResponse(request))
                {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400)
                    {
                        String location = response.Headers[HttpResponseHeader.Location];
                        if (String.IsNullOrEmpty(location)) throw new WebException("HTTP " + code + " without Location");
                        uri = new Uri(uri, location);
                        log("  redirected to " + uri.GetLeftPart(UriPartial.Path));
                        continue;
                    }
                    if (code != 200) throw new WebException("HTTP " + code + " " + response.StatusDescription);
                    using (Stream s = response.GetResponseStream())
                    using (FileStream f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        s.CopyTo(f);
                    log("  downloaded " + new FileInfo(path).Length + " bytes");
                    return;
                }
            }
            throw new WebException(Localization.T("CacheDownloader.TooManyRedirects", "Too many redirects."));
        }

        // A 3xx answer is a WebException when AllowAutoRedirect is false on some versions: keep its response
        static HttpWebResponse GetResponse(HttpWebRequest request)
        {
            try { return (HttpWebResponse)request.GetResponse(); }
            catch (WebException ex) when (ex.Response is HttpWebResponse && (int)((HttpWebResponse)ex.Response).StatusCode >= 300 && (int)((HttpWebResponse)ex.Response).StatusCode < 400)
            {
                return (HttpWebResponse)ex.Response;
            }
        }
    }
}
