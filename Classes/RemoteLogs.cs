// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteLogs.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Retrieves the logs of a computer into %LOCALAPPDATA%\LiteDeploy\logs\
//          <computer>\<date>: logs of the update modules (%windir%\LiteDeploy\
//          work), the newest winget logs of SYSTEM, and the extra folders of
//          the ExtraLogFolders setting (label|folder under %windir%|newest
//          files). Through the ADMIN$ share of the computer, with the account
//          of the software updates.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteSuite;

namespace LiteDeploy
{
    static public class RemoteLogs
    {
        /// <summary>Files taken from an extra folder when its entry gives no number.</summary>
        public const int DefaultMaxFiles = 10;
        /// <summary>Highest number of files taken from an extra folder.</summary>
        public const int MaxFilesLimit = 100;

        /// <summary>Source folders (relative to ADMIN$, that is %windir%) and how many of the newest files to take.</summary>
        static readonly Tuple<String, String, int>[] Sources =
        {
            Tuple.Create("modules", @"LiteDeploy\work", 20),
            Tuple.Create("winget", @"System32\config\systemprofile\AppData\Local\Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\DiagOutputDir", 5)
        };

        /// <summary>
        /// Parses the ExtraLogFolders setting: entries "label|folder under %windir%|newest files"
        /// separated by ';' (the number is optional). Invalid entries are skipped: a label that is
        /// not a plain folder name, a folder that is rooted or leaves %windir% (".."), a number
        /// outside 1 to MaxFilesLimit. Returns (label, folder, files) in the order of the setting.
        /// </summary>
        static public List<Tuple<String, String, int>> ParseExtraFolders(String setting)
        {
            List<Tuple<String, String, int>> result = new List<Tuple<String, String, int>>();
            if (String.IsNullOrWhiteSpace(setting)) return result;
            foreach (String entry in setting.Split(';'))
            {
                String[] parts = entry.Split('|');
                if (parts.Length < 2 || parts.Length > 3) continue;
                String label = parts[0].Trim();
                String folder = parts[1].Trim().TrimEnd('\\', '/');
                int files = DefaultMaxFiles;
                if (parts.Length == 3 && parts[2].Trim().Length > 0
                    && (!int.TryParse(parts[2].Trim(), out files) || files < 1 || files > MaxFilesLimit))
                    continue;
                if (!IsPlainName(label) || !IsRelativeFolder(folder)) continue;
                result.Add(Tuple.Create(label, folder, files));
            }
            return result;
        }

        static bool IsPlainName(String name)
        {
            return name.Length > 0 && name != "." && name != ".."
                && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        static bool IsRelativeFolder(String folder)
        {
            if (folder.Length == 0 || folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            if (folder.Contains(":") || Path.IsPathRooted(folder)) return false;
            return folder.Split('\\', '/').All(IsPlainName);
        }

        /// <summary>Copies the logs; returns the local folder. Throws when the computer cannot be reached.</summary>
        static public String Fetch(String computer, out int count)
        {
            computer = computer.Trim();
            String target = Path.Combine(ScheduleRunner.LogDir, computer, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            String admin = @"\\" + computer + @"\ADMIN$";
            count = 0;
            IEnumerable<Tuple<String, String, int>> sources = Sources.Concat(ParseExtraFolders(UserSettings.Current.ExtraLogFolders));
            using (new NetworkShare(admin, UpdateRunner.UpdateCredential()))
            {
                if (!Directory.Exists(admin)) throw new IOException(Localization.F("RemoteLogs.NoAccess", "{0} cannot be reached.", admin));
                foreach (Tuple<String, String, int> source in sources)
                {
                    String dir = Path.Combine(admin, source.Item2);
                    List<FileInfo> files;
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        files = new DirectoryInfo(dir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(source.Item3).ToList();
                    }
                    catch (UnauthorizedAccessException) { continue; }
                    if (files.Count == 0) continue;
                    String destination = Path.Combine(target, source.Item1);
                    Directory.CreateDirectory(destination);
                    foreach (FileInfo f in files)
                    {
                        try
                        {
                            // A log being written is read with sharing (File.Copy would fail on it)
                            using (FileStream input = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (FileStream output = File.Create(Path.Combine(destination, f.Name)))
                                input.CopyTo(output);
                            count++;
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
            Directory.CreateDirectory(target);
            return target;
        }
    }
}
