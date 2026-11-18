// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    InstallPolicy.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Rules of the explicit Install action (docs/modules.md): a package
//          is installed only on a computer where the check says "notinstalled",
//          and a module only when its header declares "# Install: yes". Used by
//          LiteDeploy (what the Install menu offers) and by the worker (what it
//          does). Pure logic, compiled into both programs (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace LiteDeploy
{
    /// <summary>What the worker does with one package of an install run, after its check.</summary>
    internal enum InstallDecision
    {
        /// <summary>The check failed: already reported as an error, nothing installed.</summary>
        CheckFailed,
        /// <summary>A module whose header does not declare "# Install: yes": refused.</summary>
        NotSupported,
        /// <summary>Already installed (up to date or outdated): nothing to do.</summary>
        AlreadyInstalled,
        /// <summary>Not installed: install it.</summary>
        Install
    }

    internal static class InstallPolicy
    {
        /// <summary>Header key of a module that can install its product: "# Install: yes".</summary>
        public const string HeaderKey = "Install";
        /// <summary>The only value that enables the Install action.</summary>
        public const string HeaderYes = "yes";
        /// <summary>Worker command of the install run.</summary>
        public const string Command = "install";
        /// <summary>Value of "-Action" given to a module.</summary>
        public const string ModuleAction = "Install";

        /// <summary>
        /// True when one of the lines (the first 20 of the script) is "# Install: yes" (key and
        /// value case-insensitive). Any other value, or no such line: false (default).
        /// </summary>
        public static bool DeclaresInstall(IEnumerable<string> firstLines)
        {
            if (firstLines == null) return false;
            string prefix = "# " + HeaderKey + ":";
            foreach (string raw in firstLines)
            {
                if (raw == null) continue;
                string line = raw.Trim().TrimStart('﻿').Trim();
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (line.Substring(prefix.Length).Trim().Equals(HeaderYes, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// The decision of the worker for one package, from the state its check reported.
        /// <paramref name="moduleDeclaresInstall"/> is ignored for a winget package.
        /// </summary>
        public static InstallDecision Decide(bool isModule, bool moduleDeclaresInstall, string status)
        {
            if (status == WorkerProtocol.StatusError || status == null) return InstallDecision.CheckFailed;
            if (isModule && !moduleDeclaresInstall) return InstallDecision.NotSupported;
            if (status == WorkerProtocol.StatusNotInstalled) return InstallDecision.Install;
            return InstallDecision.AlreadyInstalled;
        }

        /// <summary>
        /// Whether LiteDeploy offers to install <paramref name="package"/> on a computer whose last
        /// check reported <paramref name="status"/>: only "notinstalled", and for a module only when
        /// its header declares "# Install: yes".
        /// </summary>
        public static bool CanOffer(UpdatePackage package, string status, bool moduleDeclaresInstall)
        {
            if (package == null) return false;
            return Decide(package.IsModule, moduleDeclaresInstall, status) == InstallDecision.Install;
        }

        /// <summary>The error of a module that does not declare the Install action.</summary>
        public static string NotSupportedMessage(string moduleName)
        {
            return "Install refused: the module " + moduleName + " does not declare \"# " + HeaderKey + ": " + HeaderYes + "\" in its header.";
        }
    }
}
