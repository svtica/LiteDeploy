// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UninstallPolicy.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Rules of the explicit Uninstall action (uninstall mode of the grid,
//          docs/modules.md): a package is uninstalled only on a computer where
//          the check says it is installed (up to date or outdated), a module
//          only when its header declares "# Uninstall: yes", and never a
//          protected package (LiteSuite itself, security agents, Windows and
//          runtime components: a fixed list). Never part of Update checked nor
//          of the schedules. Used by LiteDeploy (what the menu offers) and by
//          the worker (what it does). Pure logic, compiled into both programs
//          (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy
{
    /// <summary>What the worker does with one package of an uninstall run, after its check.</summary>
    internal enum UninstallDecision
    {
        /// <summary>The check failed: already reported as an error, nothing removed.</summary>
        CheckFailed,
        /// <summary>A module whose header does not declare "# Uninstall: yes": refused.</summary>
        NotSupported,
        /// <summary>A package of the protected list: refused.</summary>
        Protected,
        /// <summary>Not installed: nothing to do.</summary>
        NotInstalled,
        /// <summary>Installed: uninstall it.</summary>
        Uninstall
    }

    internal static class UninstallPolicy
    {
        /// <summary>Header key of a module that can remove its product: "# Uninstall: yes".</summary>
        public const string HeaderKey = "Uninstall";
        /// <summary>Worker command of the uninstall run.</summary>
        public const string Command = "uninstall";
        /// <summary>Value of "-Action" given to a module.</summary>
        public const string ModuleAction = "Uninstall";

        /// <summary>
        /// Parts of package Ids (winget Id or module name, case ignored) that are never uninstalled: LiteSuite,
        /// security agents, and the components other software and Windows rely on.
        /// </summary>
        public static readonly string[] ProtectedParts =
        {
            "LiteDeploy", "LiteTask", "LiteSuite", "LitePM",
            "TrendMicro", "Trend.", "Fortinet", "FortiClient", "Defender", "Sophos", "CrowdStrike", "SentinelOne",
            "Microsoft.Edge", "Microsoft.AppInstaller", "Microsoft.WindowsAppRuntime", "Microsoft.VCRedist",
            "Microsoft.VCLibs", "Microsoft.DotNet", "Microsoft.UI.Xaml", "Microsoft.WindowsTerminal"
        };

        /// <summary>True when one of the lines (the first 20 of the script) is "# Uninstall: yes" (case ignored).</summary>
        public static bool DeclaresUninstall(IEnumerable<string> firstLines)
        {
            if (firstLines == null) return false;
            string prefix = "# " + HeaderKey + ":";
            foreach (string raw in firstLines)
            {
                if (raw == null) continue;
                string line = raw.Trim().TrimStart('﻿').Trim();
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (line.Substring(prefix.Length).Trim().Equals(InstallPolicy.HeaderYes, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>True for a package that is never uninstalled (ProtectedParts).</summary>
        public static bool IsProtected(UpdatePackage package)
        {
            if (package == null) return true;
            string id = package.Id ?? "";
            return ProtectedParts.Any(p => id.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The decision of the worker for one package, from the state its check reported.</summary>
        public static UninstallDecision Decide(UpdatePackage package, bool moduleDeclaresUninstall, string status)
        {
            if (IsProtected(package)) return UninstallDecision.Protected;
            if (status == WorkerProtocol.StatusError || status == null) return UninstallDecision.CheckFailed;
            if (package.IsModule && !moduleDeclaresUninstall) return UninstallDecision.NotSupported;
            if (status == WorkerProtocol.StatusNotInstalled) return UninstallDecision.NotInstalled;
            return UninstallDecision.Uninstall;
        }

        /// <summary>Whether LiteDeploy offers to uninstall the package on a computer whose last check reported <paramref name="status"/>.</summary>
        public static bool CanOffer(UpdatePackage package, string status, bool moduleDeclaresUninstall)
        {
            if (package == null) return false;
            return (status == WorkerProtocol.StatusUpToDate || status == WorkerProtocol.StatusOutdated)
                && Decide(package, moduleDeclaresUninstall, status) == UninstallDecision.Uninstall;
        }

        public static string NotSupportedMessage(string moduleName)
        {
            return "Uninstall refused: the module " + moduleName + " does not declare \"# " + HeaderKey + ": " + InstallPolicy.HeaderYes + "\" in its header.";
        }

        public static string ProtectedMessage(string id)
        {
            return "Uninstall refused: " + id + " is in the protected list of LiteDeploy (LiteSuite, security agents, Windows and runtime components).";
        }

        /// <summary>Above this many computers, the confirmation asks to type the number of computers.</summary>
        public const int TypedConfirmationAbove = 5;

        /// <summary>The typed answer confirms <paramref name="count"/> computers: the number itself, spaces ignored.</summary>
        public static bool IsTypedConfirmation(string typed, int count)
        {
            return typed != null && typed.Trim() == count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Minutes without an uninstall action after which the uninstall mode turns itself off.</summary>
        public const int ModeIdleMinutes = 15;
    }
}
