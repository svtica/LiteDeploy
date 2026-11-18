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
//               Valid and O= one of the organisations of the Publisher
//               parameter (separated by ;); for an MSIX, the package name and
//               the signer = Publisher of its manifest, as the module checks
//               them. A zip installer (it has no signature): its SHA256, then
//               the only .msi or .exe it holds (of the type of the instance) is
//               extracted, within size limits, and its signature checked; a
//               zip with none or several of them is refused (download it
//               manually);
//            3. copy to the folder of the current file, under a new versioned
//               name (never over an existing file), SHA256 checked again
//               (for a zip: the installer it holds, not the zip);
//            4. File (and Version when it was set) of the instance updated in
//               module-settings.xml;
//            5. the previous file moved to the OLD subfolder of that folder.
//          Only the file the instance uses is moved; nothing else in the cache
//          is touched. Every step is logged and reported (WebVersionProgress,
//          with the bytes received). Several files: one at a time; cancelled,
//          the run stops after the current file. Naming, rotation and the run
//          loop (with a fake download) are pure or file-only logic, unit-tested
//          on a temporary folder.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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

    /// <summary>One file of a Download to the cache: its result, or why it failed.</summary>
    internal sealed class CacheDownloadOutcome
    {
        public WebVersionEntry Entry;
        /// <summary>Null: failed (Error).</summary>
        public CacheDownloadResult Result;
        /// <summary>Full message of the failure (details window and log).</summary>
        public String Error;
        /// <summary>Short reason of the failure (one line of the summary).</summary>
        public String Reason;
        /// <summary>The download was refused by a check (hash, signature, zip...), not stopped by an error.</summary>
        public bool Refused;
    }

    /// <summary>
    /// What the window shows after a Download to the cache: one line per product (summary), and the full
    /// messages with the log file (Details window). Pure logic, unit-tested.
    /// </summary>
    internal static class CacheDownloadSummary
    {
        static String Name(WebVersionEntry e)
        {
            return ((e.Product ?? e.Key ?? "?") + " " + (e.WebVersion ?? "")).Trim();
        }

        /// <summary>One line per file processed, then the files not processed (cancelled).</summary>
        static public String Summary(IList<CacheDownloadOutcome> outcomes, int requested)
        {
            List<String> lines = new List<String>();
            foreach (CacheDownloadOutcome o in outcomes)
            {
                if (o.Result != null)
                    lines.Add(o.Result.Warning == null
                        ? Localization.F("CacheDownloadSummary.Ok", "OK: {0}", Name(o.Entry))
                        : Localization.F("CacheDownloadSummary.OkWarning", "OK: {0} (previous file not moved to {1})", Name(o.Entry), CacheDownloader.OldFolder));
                else if (o.Refused)
                    lines.Add(Localization.F("CacheDownloadSummary.Refused", "Refused: {0} - {1}", Name(o.Entry), o.Reason ?? o.Error));
                else
                    lines.Add(Localization.F("CacheDownloadSummary.Failed", "Failed: {0} - {1}", Name(o.Entry), o.Reason ?? o.Error));
            }
            if (outcomes.Count < requested)
                lines.Add(Localization.F("CacheDownloadSummary.NotProcessed", "Cancelled: {0} file(s) not processed.", requested - outcomes.Count));
            return String.Join(Environment.NewLine, lines);
        }

        /// <summary>A file was not downloaded, or the previous file stayed: the details are worth a look.</summary>
        static public bool HasDetails(IList<CacheDownloadOutcome> outcomes)
        {
            return outcomes.Any(o => o.Result == null || o.Result.Warning != null);
        }

        /// <summary>Full messages of each file, then the log file.</summary>
        static public String Details(IList<CacheDownloadOutcome> outcomes, String logPath)
        {
            StringBuilder sb = new StringBuilder();
            foreach (CacheDownloadOutcome o in outcomes)
            {
                WebVersionEntry e = o.Entry;
                sb.AppendLine(Name(e) + " [" + (e.Key ?? "?") + "]");
                if (o.Result != null)
                {
                    sb.AppendLine("  " + Localization.F("CacheDownloadSummary.NewFile", "File parameter: {0}", o.Result.NewFileParameter));
                    if (o.Result.OldFileMovedTo != null)
                        sb.AppendLine("  " + Localization.F("CacheDownloadSummary.Moved", "Previous file moved to: {0}", o.Result.OldFileMovedTo));
                    if (o.Result.Warning != null) sb.AppendLine("  " + o.Result.Warning);
                }
                else sb.AppendLine("  " + (o.Error ?? o.Reason ?? "?"));
                sb.AppendLine();
            }
            if (!String.IsNullOrEmpty(logPath))
                sb.AppendLine(Localization.F("CacheDownloadSummary.Log", "Log file: {0}", logPath));
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>A download refused, with a short reason for the summary; the message has the details.</summary>
    internal sealed class CacheDownloadException : Exception
    {
        public readonly String Reason;

        public CacheDownloadException(String reason, String message) : base(message)
        {
            Reason = reason;
        }
    }

    /// <summary>A step of a download: the step, bytes received, length of the download (0 or less: unknown).</summary>
    internal delegate void CacheDownloadStep(WebVersionStep step, long received, long total);

    internal sealed class CacheDownloader
    {
        public const String OldFolder = "OLD";
        const int MaxRedirects = 10;
        /// <summary>Bytes received reported at most this often (the window redraws its status line).</summary>
        const int ReportIntervalMs = 250;

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
        /// Organisations of a Publisher parameter: separated by ";" (a vendor that renamed itself signs
        /// with its new name), trimmed, without empty ones; a name may be in double quotes (then a ";" in
        /// it is kept). Pure logic, unit-tested; same rule as the modules.
        /// </summary>
        static public List<String> PublisherNames(String publisher)
        {
            List<String> names = new List<String>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            Action flush = () =>
            {
                String name = current.ToString().Trim();
                if (name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"') name = name.Substring(1, name.Length - 2).Trim();
                if (name.Length > 0 && !names.Contains(name)) names.Add(name);
                current.Clear();
            };
            foreach (char c in publisher ?? "")
            {
                if (c == '"') quoted = !quoted;
                if (c == ';' && !quoted) flush();
                else current.Append(c);
            }
            flush();
            return names;
        }

        /// <summary>
        /// Valid signature whose signer has O= one of the organisations of <paramref name="publisher"/> (see
        /// PublisherNames; O= quoted when it has a comma), like the Publisher check of the modules. Pure
        /// logic, unit-tested.
        /// </summary>
        static public bool IsSignedBy(AuthenticodeResult result, String publisher)
        {
            if (result == null || result.Status != AuthenticodeStatus.Valid || String.IsNullOrEmpty(result.SignerSubject))
                return false;
            return PublisherNames(publisher).Any(name => HasOrganisation(result.SignerSubject, name));
        }

        /// <summary>O=<paramref name="organisation"/> in a certificate subject (quoted or not), exactly.</summary>
        static public bool HasOrganisation(String subject, String organisation)
        {
            if (String.IsNullOrEmpty(subject) || String.IsNullOrWhiteSpace(organisation)) return false;
            String pattern = @"(^|,\s*)O=(""?)" + Regex.Escape(organisation.Trim()) + @"\2\s*(,|$)";
            return Regex.IsMatch(subject, pattern);
        }

        static readonly Regex OrganisationField = new Regex(@"(?:^|,)\s*O=(?:""(?<q>(?:[^""]|"""")*)""|(?<v>[^,]*))");

        /// <summary>The O= value of a certificate subject, without its quotes (null: none). Pure logic, unit-tested.</summary>
        static public String OrganisationOf(String subject)
        {
            if (String.IsNullOrEmpty(subject)) return null;
            Match m = OrganisationField.Match(subject);
            if (!m.Success) return null;
            String o = m.Groups["q"].Success ? m.Groups["q"].Value.Replace("\"\"", "\"") : m.Groups["v"].Value;
            o = o.Trim();
            return o.Length == 0 ? null : o;
        }

        /// <summary>
        /// Why a signature is refused for <paramref name="publisher"/>: a short reason (summary of the run) and
        /// a message (details). A valid signature of another organisation names it and says how to accept a
        /// vendor that renamed itself. Pure logic, unit-tested.
        /// </summary>
        static public CacheDownloadException SignatureRefusal(AuthenticodeResult signature, String publisher)
        {
            String expected = String.Join("; ", PublisherNames(publisher));
            if (signature == null || signature.Status != AuthenticodeStatus.Valid || String.IsNullOrEmpty(signature.SignerSubject))
            {
                String status = signature == null ? "?" : signature.Status.ToString();
                return new CacheDownloadException(
                    Localization.F("CacheDownloader.NoValidSignatureShort", "no valid signature ({0})", status),
                    Localization.F("CacheDownloader.NoValidSignature", "The download has no valid signature ({0}, signer {1}): refused.",
                        status, signature == null || String.IsNullOrEmpty(signature.SignerSubject) ? "-" : signature.SignerSubject));
            }
            String actual = OrganisationOf(signature.SignerSubject) ?? signature.SignerSubject;
            return new CacheDownloadException(
                Localization.F("CacheDownloader.SignedByOtherShort", "signed by {0} instead of {1}", actual, expected),
                Localization.F("CacheDownloader.SignedByOther",
                    "The download is validly signed, but by {0} instead of {1}: refused. If this vendor renamed itself, add {0} to the Publisher parameter of the instance (names separated by ;). Signer: {2}",
                    actual, expected, signature.SignerSubject));
        }

        /// <summary>Short reason of a failed download for the summary: the one of a refusal, else the first line of the message.</summary>
        static public String ShortReason(Exception ex)
        {
            CacheDownloadException refusal = ex as CacheDownloadException;
            if (refusal != null && !String.IsNullOrEmpty(refusal.Reason)) return refusal.Reason;
            String message = ((ex == null ? null : ex.Message) ?? "").Trim();
            int eol = message.IndexOfAny(new[] { '\r', '\n' });
            if (eol >= 0) message = message.Substring(0, eol).Trim();
            const int max = 120;
            return message.Length <= max ? message : message.Substring(0, max - 3).TrimEnd() + "...";
        }

        // ---- Zip installers --------------------------------------------------------------------

        /// <summary>Largest installer extracted from a zip.</summary>
        public const long MaxNestedBytes = 4L * 1024 * 1024 * 1024;
        /// <summary>Zip with more entries than this: refused (not an installer archive).</summary>
        public const int MaxZipEntries = 10000;

        /// <summary>A zip file (local file header "PK\x03\x04"); an MSIX is one too, so this is used for msi and exe instances only.</summary>
        static public bool IsZipFile(String path)
        {
            byte[] head = new byte[4];
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return f.Read(head, 0, 4) == 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04;
        }

        /// <summary>
        /// A relative path of a zip entry that stays in the folder it is extracted to: no root, drive, ".."
        /// or control character. Pure logic, unit-tested.
        /// </summary>
        static public bool IsSafeEntryName(String name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            String n = name.Replace('\\', '/');
            if (n.StartsWith("/") || n.Contains(":") || n.Any(Char.IsControl)) return false;
            return !n.Split('/').Any(part => part == "..");
        }

        /// <summary>
        /// The installer a zip holds for an instance of <paramref name="type"/> (msi or exe): its only file of
        /// that extension. None, several or one with an unsafe path: refused (download it manually). Pure
        /// logic, unit-tested.
        /// </summary>
        static public String NestedInstallerEntry(IEnumerable<String> entryNames, String type)
        {
            return NestedInstallerEntry(entryNames, type, null);
        }

        /// <summary>
        /// With several files of the type in the zip, the one to keep: first the one with the same file name
        /// as the current file of the instance (<paramref name="currentFileName"/>, e.g. "Vendor_x64.msi"),
        /// else the only one that names x64 ("x64", "win64", "amd64", "x86_64", "64-bit"), else the only one
        /// that is not for another architecture (ARM64, 32-bit): LiteDeploy installs the x64 installers, as
        /// the winget query (--architecture x64). Still several: refused (download it manually).
        /// </summary>
        static public String NestedInstallerEntry(IEnumerable<String> entryNames, String type, String currentFileName)
        {
            String ext = "." + (type ?? "").Trim().ToLowerInvariant();
            if (ext != ".msi" && ext != ".exe")
                throw ZipManual(Localization.F("CacheDownloader.ZipType", "the instance type is {0}, not msi or exe", type ?? "?"));
            List<String> candidates = (entryNames ?? Enumerable.Empty<String>())
                .Where(n => !String.IsNullOrEmpty(n) && !n.EndsWith("/") && !n.EndsWith("\\") && n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToList();
            String unsafeName = candidates.FirstOrDefault(n => !IsSafeEntryName(n));
            if (unsafeName != null)
                throw new CacheDownloadException(Localization.T("CacheDownloader.ZipUnsafeShort", "zip installer: unsafe path"),
                    Localization.F("CacheDownloader.ZipUnsafe", "The zip holds a file with an unsafe path ({0}): refused.", unsafeName));
            if (candidates.Count == 0)
                throw ZipManual(Localization.F("CacheDownloader.ZipNone", "no {0} file in it", ext));
            if (candidates.Count == 1) return candidates[0];

            if (!String.IsNullOrWhiteSpace(currentFileName))
            {
                String current = Path.GetFileName(currentFileName.Trim().Replace('/', '\\'));
                List<String> same = candidates.Where(n => String.Equals(EntryFileName(n), current, StringComparison.OrdinalIgnoreCase)).ToList();
                if (same.Count == 1) return same[0];
            }
            List<String> x64 = candidates.Where(NamesX64).ToList();
            if (x64.Count == 1) return x64[0];
            List<String> notOther = candidates.Where(n => !IsOtherArchitecture(n)).ToList();
            if (notOther.Count == 1) return notOther[0];
            throw ZipManual(Localization.F("CacheDownloader.ZipSeveral", "{0} {1} files in it: {2}", candidates.Count, ext, String.Join(", ", candidates)));
        }

        static String EntryFileName(String entryName)
        {
            String n = entryName.Replace('\\', '/');
            int slash = n.LastIndexOf('/');
            return slash < 0 ? n : n.Substring(slash + 1);
        }

        /// <summary>
        /// Whether a path in a zip names an x64 installer: a part or a word "x64", "win64", "amd64", "x86_64" /
        /// "x86-64", "64bit" / "64-bit". Pure logic, unit-tested.
        /// </summary>
        static public bool NamesX64(String entryName)
        {
            if (String.IsNullOrEmpty(entryName)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(entryName.ToLowerInvariant(),
                "(^|[^a-z0-9])(x64|win64|amd64|x86[_-]64|64-?bit)([^a-z0-9]|$)");
        }

        /// <summary>
        /// Whether a path in a zip names an installer for another architecture than x64: a part or a word
        /// "arm64", "aarch64", "arm", "x86", "win32", "32bit" / "32-bit" ("x86_64" and "x86-64" are x64).
        /// Pure logic, unit-tested.
        /// </summary>
        static public bool IsOtherArchitecture(String entryName)
        {
            if (String.IsNullOrEmpty(entryName)) return false;
            String n = System.Text.RegularExpressions.Regex.Replace(entryName.ToLowerInvariant(), "x86[_-]64", "x64");
            return System.Text.RegularExpressions.Regex.IsMatch(n, "(^|[^a-z0-9])(arm64|aarch64|arm|x86|win32|32-?bit)([^a-z0-9]|$)");
        }

        static CacheDownloadException ZipManual(String why)
        {
            return new CacheDownloadException(Localization.T("CacheDownloader.ZipManualShort", "zip installer: download it manually"),
                Localization.F("CacheDownloader.ZipManual", "Zip installer ({0}): LiteDeploy cannot tell which installer to keep. Download it manually from the vendor.", why));
        }

        /// <summary>
        /// Extracts the installer a zip holds for an instance of <paramref name="type"/> (see NestedInstallerEntry)
        /// to <paramref name="folder"/>, as "nested.msi" or "nested.exe" (never under the name of the entry).
        /// Refused: too many entries, larger than <paramref name="maxBytes"/> (announced or actual size).
        /// Returns its path; <paramref name="entryName"/>: its path in the zip. Unit-tested on a temporary zip.
        /// </summary>
        public String ExtractNestedInstaller(String zipPath, String type, String folder, long maxBytes, out String entryName)
        {
            return ExtractNestedInstaller(zipPath, type, folder, maxBytes, null, out entryName);
        }

        /// <summary>With <paramref name="currentFileName"/>: the current file of the instance, to choose among several installers.</summary>
        public String ExtractNestedInstaller(String zipPath, String type, String folder, long maxBytes, String currentFileName, out String entryName)
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > MaxZipEntries)
                    throw ZipManual(Localization.F("CacheDownloader.ZipTooManyEntries", "more than {0} entries", MaxZipEntries));
                entryName = NestedInstallerEntry(zip.Entries.Select(e => e.FullName), type, currentFileName);
                String name = entryName;
                ZipArchiveEntry entry = zip.Entries.First(e => e.FullName == name);
                if (entry.Length > maxBytes)
                    throw new CacheDownloadException(Localization.T("CacheDownloader.ZipTooLargeShort", "zip installer: file too large"),
                        Localization.F("CacheDownloader.ZipTooLarge", "{0} in the zip is larger than {1} bytes: refused.", entryName, maxBytes));
                String target = Path.Combine(folder, "nested." + type.Trim().ToLowerInvariant());
                log("  extract " + entryName + " (" + entry.Length + " bytes) to " + target);
                using (Stream source = entry.Open())
                using (FileStream copy = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    long written = 0;
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += read;
                        if (written > maxBytes)
                            throw new CacheDownloadException(Localization.T("CacheDownloader.ZipTooLargeShort", "zip installer: file too large"),
                                Localization.F("CacheDownloader.ZipTooLarge", "{0} in the zip is larger than {1} bytes: refused.", entryName, maxBytes));
                        copy.Write(buffer, 0, read);
                    }
                }
                return target;
            }
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
        /// Downloads the entries one at a time (see Download), each step reported to <paramref name="progress"/>.
        /// Cancelled: stops before the next file. Returns the outcome of each file processed.
        /// </summary>
        public List<CacheDownloadOutcome> DownloadAll(IList<WebVersionEntry> entries, String cacheRoot, IProgress<WebVersionProgress> progress, CancellationToken token)
        {
            return RunDownloads(entries, (e, step) => Download(e, cacheRoot, step), progress, token, log);
        }

        /// <summary>
        /// The run loop of DownloadAll, with the download as a parameter: one file at a time, each step and
        /// each outcome reported, nothing new started once cancelled; an exception is the outcome of its file.
        /// Unit-tested with a fake download.
        /// </summary>
        static public List<CacheDownloadOutcome> RunDownloads(IList<WebVersionEntry> entries, Func<WebVersionEntry, CacheDownloadStep, CacheDownloadResult> download,
            IProgress<WebVersionProgress> progress, CancellationToken token, Action<String> log)
        {
            List<CacheDownloadOutcome> outcomes = new List<CacheDownloadOutcome>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    if (log != null) log("Download cancelled: " + outcomes.Count + " of " + entries.Count + " file(s) processed");
                    break;
                }
                WebVersionEntry e = entries[i];
                int index = i + 1, done = outcomes.Count;
                CacheDownloadStep step = (s, received, total) => WebVersionProgressText.Report(progress, new WebVersionProgress
                {
                    Operation = WebVersionOperation.Download, Index = index, Total = entries.Count, Done = done,
                    Key = e.Key, Product = e.Product, Step = s, BytesReceived = received, BytesTotal = total
                });
                CacheDownloadOutcome outcome = new CacheDownloadOutcome { Entry = e };
                try { outcome.Result = download(e, step); }
                catch (Exception ex)
                {
                    outcome.Error = ex.Message;
                    outcome.Reason = ShortReason(ex);
                    outcome.Refused = ex is CacheDownloadException;
                }
                outcomes.Add(outcome);
                WebVersionProgressText.Report(progress, new WebVersionProgress
                {
                    Operation = WebVersionOperation.Download, Index = index, Total = entries.Count, Done = outcomes.Count,
                    Key = e.Key, Product = e.Product, Step = WebVersionStep.Done,
                    Entry = outcome.Result != null ? outcome.Result.Entry : null, Error = outcome.Error
                });
            }
            return outcomes;
        }

        /// <summary>
        /// Downloads the new version of the instance of <paramref name="entry"/> into the cache (see
        /// the steps above). Throws with a readable message; nothing is changed in the cache or the
        /// settings then (except the previous file, which is moved only once the new one is in place).
        /// </summary>
        public CacheDownloadResult Download(WebVersionEntry entry, String cacheRoot, CacheDownloadStep step = null)
        {
            if (step == null) step = (s, received, total) => { };
            step(WebVersionStep.Preparing, 0, 0);
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
                DownloadHttps(entry.InstallerUrl, download, step);

                step(WebVersionStep.VerifyingHash, 0, 0);
                String sha = Sha256Of(download);
                if (!String.Equals(sha, entry.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                    throw new CacheDownloadException(Localization.T("CacheDownloader.HashMismatchShort", "SHA256 differs from the winget manifest"),
                        Localization.F("CacheDownloader.HashMismatch",
                        "The SHA256 of the download ({0}) is not the one of the winget manifest ({1}): refused.", sha, entry.InstallerSha256));
                log("  SHA256 matches the winget manifest: " + sha);

                // A zip installer has no signature: the installer it holds is checked and put in the cache instead
                if (!target.IsMsix && (WebVersionChecker.IsArchive(entry.InstallerType) || IsZipFile(download)))
                {
                    if (!String.IsNullOrEmpty(entry.NestedInstallerType) && !WebVersionChecker.NestedTypeMatches(target.Type, entry.NestedInstallerType))
                        throw new CacheDownloadException(Localization.T("CacheDownloader.ZipManualShort", "zip installer: download it manually"),
                            WebVersionChecker.ZipManualMessage(entry.NestedInstallerType, target.Type));
                    step(WebVersionStep.Extracting, 0, 0);
                    String entryName;
                    String extractDir = Path.Combine(temp, "nested");
                    Directory.CreateDirectory(extractDir);
                    download = ExtractNestedInstaller(download, target.Type, extractDir, MaxNestedBytes, target.File, out entryName);
                    extension = Path.GetExtension(download);
                    sha = Sha256Of(download);
                    log("  zip installer: " + entryName + " extracted, SHA256 " + sha);
                }

                step(WebVersionStep.VerifyingSignature, 0, 0);
                AuthenticodeResult signature = AuthenticodeVerifier.Verify(download);
                log("  signature: " + DescribeSignature(signature));
                if (target.IsMsix) CheckMsix(download, target, signature);
                else if (!IsSignedBy(signature, target.Publisher))
                    throw SignatureRefusal(signature, target.Publisher);
                log("  signature accepted");

                // The downloaded file must not be older than the one of the cache
                String newFileVersion = WebVersionChecker.FileVersionOf(download, target.Type);
                String oldFileVersion = null;
                try { oldFileVersion = WebVersionChecker.FileVersionOf(current, target.Type); } catch (Exception) { }
                log("  version of the download " + (newFileVersion ?? "?") + ", of the cache file " + (oldFileVersion ?? "?"));
                if (WebVersionChecker.CompareVersions(newFileVersion, oldFileVersion) < 0)
                    throw new CacheDownloadException(Localization.T("CacheDownloader.OlderShort", "older than the file of the cache"),
                        Localization.F("CacheDownloader.Older", "The download ({0}) is older than the file of the cache ({1}): refused.", newFileVersion, oldFileVersion));

                String newName = VersionedFileName(Path.GetFileName(current), new[] { entry.CacheVersion, oldFileVersion }, entry.WebVersion, extension);
                step(WebVersionStep.CopyingToCache, 0, 0);
                String placed = CopyIntoCache(download, sha, folder, newName);
                step(WebVersionStep.UpdatingSettings, 0, 0);

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
                throw new CacheDownloadException(Localization.F("CacheDownloader.OtherPackageShort", "package {0}, not {1}", m.Name, target.PackageName),
                    Localization.F("CacheDownloader.OtherPackage", "The download is the package {0}, not {1}: refused.", m.Name, target.PackageName));
            if (signature == null || signature.Status != AuthenticodeStatus.Valid || !SameDistinguishedName(signature.SignerSubject, m.Publisher))
                throw new CacheDownloadException(Localization.T("CacheDownloader.MsixSignerShort", "not signed by the publisher of its manifest"),
                    Localization.F("CacheDownloader.MsixSigner", "The package is not validly signed by the Publisher of its manifest ({0}): refused.", m.Publisher));
            if (!String.IsNullOrWhiteSpace(target.Publisher) && !IsSignedBy(signature, target.Publisher))
                throw SignatureRefusal(signature, target.Publisher);
        }

        static String DescribeSignature(AuthenticodeResult signature)
        {
            return signature == null ? "(none)" : signature.ToString();
        }

        // Extension of the installer: from the URL when it is a known one, else the one of the cache file
        static String DownloadExtension(String url, String fallback)
        {
            String path = new Uri(url).AbsolutePath;
            String ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".msi" || ext == ".exe" || ext == ".msix" || ext == ".msixbundle" || ext == ".zip" ? ext : fallback;
        }

        static WebVersionEntry Clone(WebVersionEntry e)
        {
            return new WebVersionEntry
            {
                Key = e.Key, Module = e.Module, Instance = e.Instance, Product = e.Product, WingetId = e.WingetId, Type = e.Type,
                CacheFile = e.CacheFile, CacheVersion = e.CacheVersion, WebVersion = e.WebVersion, Status = e.Status,
                InstallerType = e.InstallerType, NestedInstallerType = e.NestedInstallerType, InstallerUrl = e.InstallerUrl, InstallerSha256 = e.InstallerSha256,
                VendorUrl = e.VendorUrl, Message = e.Message, Checked = e.Checked
            };
        }

        // HTTPS only, redirects included (each one followed by hand and refused unless https)
        void DownloadHttps(String url, String path, CacheDownloadStep step)
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
                    long length = response.ContentLength;   // -1: not given by the server
                    step(WebVersionStep.Downloading, 0, length);
                    using (Stream s = response.GetResponseStream())
                    using (FileStream f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long received = 0;
                        int read;
                        DateTime nextReport = DateTime.UtcNow.AddMilliseconds(ReportIntervalMs);
                        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            f.Write(buffer, 0, read);
                            received += read;
                            if (DateTime.UtcNow >= nextReport)
                            {
                                step(WebVersionStep.Downloading, received, length);
                                nextReport = DateTime.UtcNow.AddMilliseconds(ReportIntervalMs);
                            }
                        }
                        step(WebVersionStep.Downloading, received, length);
                    }
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
