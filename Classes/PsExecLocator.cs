// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PsExecLocator.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Finds PsExec: LiteDeploy folder first, then the tools folder of
//          LiteTask.
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.Linq;

namespace LiteDeploy
{
    /// <summary>
    /// Finds the PsExec executable LiteDeploy drives. PsExec (Sysinternals) is
    /// not redistributed with LiteDeploy: it is taken, in this order, from
    /// - the LiteDeploy folder (PsExec.exe, then PsExec64.exe);
    /// - the parent folder (PsExec64.exe, then PsExec.exe): when LiteDeploy is
    ///   installed by LiteTask (Tools menu) it lives in LiteTaskData\tools\LiteDeploy\
    ///   and LiteTask extracts its own PsExec64.exe to LiteTaskData\tools\;
    /// - %LOCALAPPDATA%\LiteDeploy\tools\PsExec64.exe, where PsExecDownloader saves it
    ///   when the LiteDeploy folder is read-only.
    /// </summary>
    static public class PsExecLocator
    {
        /// <summary>Full path of the PsExec executable, or null when not found.</summary>
        static public String Path { get; private set; }

        /// <summary>Process name of the PsExec executable (PsExec or PsExec64).</summary>
        static public String ProcessName
        {
            get
            {
                return Path == null ? "PsExec" : System.IO.Path.GetFileNameWithoutExtension(Path);
            }
        }

        static public String Find(String appDir)
        {
            String parent = Directory.GetParent(appDir.TrimEnd('\\')) == null ? null : Directory.GetParent(appDir.TrimEnd('\\')).FullName;

            String[] candidates = parent == null
                ? new String[] { System.IO.Path.Combine(appDir, "PsExec.exe"), System.IO.Path.Combine(appDir, "PsExec64.exe") }
                : new String[] { System.IO.Path.Combine(appDir, "PsExec.exe"), System.IO.Path.Combine(appDir, "PsExec64.exe"),
                                 System.IO.Path.Combine(parent, "PsExec64.exe"), System.IO.Path.Combine(parent, "PsExec.exe") };

            String downloaded = System.IO.Path.Combine(PsExecDownloader.UserToolsDir, PsExecDownloader.FileName);
            candidates = candidates.Concat(new[] { downloaded }).ToArray();

            Path = null;

            foreach (String candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    Path = candidate;
                    break;
                }
            }

            return Path;
        }
    }
}
