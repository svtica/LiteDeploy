// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteLogs.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Retrieves the logs of a computer into %LOCALAPPDATA%\LiteDeploy\logs\
//          <computer>\<date>: logs of the update modules (%windir%\LiteDeploy\
//          work), the newest winget logs of SYSTEM, and the logs of the
//          MAJ-Logiciels tool (%windir%\Temp\winget-upgrade), for the computers
//          it still handles. Through \\computer\ADMIN$, with the account of
//          the software updates.
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
        /// <summary>Source folders (relative to ADMIN$, that is %windir%) and how many of the newest files to take.</summary>
        static readonly Tuple<String, String, int>[] Sources =
        {
            Tuple.Create("modules", @"LiteDeploy\work", 20),
            Tuple.Create("winget", @"System32\config\systemprofile\AppData\Local\Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\DiagOutputDir", 5),
            Tuple.Create("maj-logiciels", @"Temp\winget-upgrade", 10)
        };

        /// <summary>Copies the logs; returns the local folder. Throws when the computer cannot be reached.</summary>
        static public String Fetch(String computer, out int count)
        {
            computer = computer.Trim();
            String target = Path.Combine(ScheduleRunner.LogDir, computer, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            String admin = @"\\" + computer + @"\ADMIN$";
            count = 0;
            using (new NetworkShare(admin, UpdateRunner.UpdateCredential()))
            {
                if (!Directory.Exists(admin)) throw new IOException(Localization.F("RemoteLogs.NoAccess", "{0} cannot be reached.", admin));
                foreach (Tuple<String, String, int> source in Sources)
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
