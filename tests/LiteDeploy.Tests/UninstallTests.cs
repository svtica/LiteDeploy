// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UninstallTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Unit tests of the Uninstall action (uninstall mode): header of the
//          modules, protected packages, decision of the worker, what the menu
//          offers, winget arguments, typed confirmation, server guard, never
//          run by a schedule, worker arguments, and the CachedInstaller header.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class UninstallTests
    {
        static List<WorkerMessage> Messages(params string[] lines)
        {
            return lines.Select(WorkerProtocol.Parse).ToList();
        }

        [TestMethod]
        public void DeclaresUninstall_OnlyYes()
        {
            Assert.IsTrue(UninstallPolicy.DeclaresUninstall(new[] { "# LiteDeploy-Module", "# uninstall: YES" }));
            Assert.IsTrue(UninstallPolicy.DeclaresUninstall(new[] { "﻿# Uninstall: yes" }));
            Assert.IsFalse(UninstallPolicy.DeclaresUninstall(new[] { "# Uninstall: no" }));
            Assert.IsFalse(UninstallPolicy.DeclaresUninstall(new[] { "# Install: yes" }));
            Assert.IsFalse(UninstallPolicy.DeclaresUninstall(null));
        }

        [TestMethod]
        public void IsProtected_LiteSuiteSecurityAndRuntimes()
        {
            foreach (string id in new[] { "svtica.LiteDeploy", "module:LiteTask", "TrendMicro.Agent", "Fortinet.FortiClientVPN", "Microsoft.Edge",
                "Microsoft.VCRedist.2015+.x64", "Microsoft.DotNet.DesktopRuntime.8", "Microsoft.AppInstaller" })
                Assert.IsTrue(UninstallPolicy.IsProtected(UpdatePackage.Parse(id)), id);
            foreach (string id in new[] { "Example.Product", "module:CachedInstaller/ExampleApp", "Oracle.JavaRuntimeEnvironment" })
                Assert.IsFalse(UninstallPolicy.IsProtected(UpdatePackage.Parse(id)), id);
            Assert.IsTrue(UninstallPolicy.IsProtected(null));
        }

        [TestMethod]
        public void Decide_EveryCase()
        {
            UpdatePackage winget = UpdatePackage.Parse("Example.Product");
            UpdatePackage module = UpdatePackage.Parse("module:Example");
            Assert.AreEqual(UninstallDecision.Uninstall, UninstallPolicy.Decide(winget, false, WorkerProtocol.StatusUpToDate));
            Assert.AreEqual(UninstallDecision.Uninstall, UninstallPolicy.Decide(winget, false, WorkerProtocol.StatusOutdated));
            Assert.AreEqual(UninstallDecision.NotInstalled, UninstallPolicy.Decide(winget, false, WorkerProtocol.StatusNotInstalled));
            Assert.AreEqual(UninstallDecision.CheckFailed, UninstallPolicy.Decide(winget, false, WorkerProtocol.StatusError));
            Assert.AreEqual(UninstallDecision.CheckFailed, UninstallPolicy.Decide(winget, false, null));
            Assert.AreEqual(UninstallDecision.NotSupported, UninstallPolicy.Decide(module, false, WorkerProtocol.StatusUpToDate));
            Assert.AreEqual(UninstallDecision.Uninstall, UninstallPolicy.Decide(module, true, WorkerProtocol.StatusUpToDate));
            Assert.AreEqual(UninstallDecision.Protected, UninstallPolicy.Decide(UpdatePackage.Parse("svtica.LiteDeploy"), true, WorkerProtocol.StatusUpToDate));
        }

        [TestMethod]
        public void CanOffer_InstalledOnly()
        {
            UpdatePackage winget = UpdatePackage.Parse("Example.Product");
            Assert.IsTrue(UninstallPolicy.CanOffer(winget, WorkerProtocol.StatusUpToDate, false));
            Assert.IsTrue(UninstallPolicy.CanOffer(winget, WorkerProtocol.StatusOutdated, false));
            Assert.IsFalse(UninstallPolicy.CanOffer(winget, WorkerProtocol.StatusNotInstalled, false));
            Assert.IsFalse(UninstallPolicy.CanOffer(winget, WorkerProtocol.StatusError, false));
            Assert.IsFalse(UninstallPolicy.CanOffer(null, WorkerProtocol.StatusUpToDate, false));
        }

        [TestMethod]
        public void Candidates_InstalledNotPausedDeclaredNotProtected()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Product", "status", "uptodate", "installed", "1.0"),
                WorkerProtocol.Format("type", "package", "id", "module:Example", "status", "outdated", "installed", "1.0"),
                WorkerProtocol.Format("type", "package", "id", "module:Other", "status", "uptodate", "installed", "1.0"),
                WorkerProtocol.Format("type", "package", "id", "Microsoft.Edge", "status", "uptodate", "installed", "1.0")), null, DateTime.Now);
            store.Apply("PC02", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Product", "status", "notinstalled"),
                WorkerProtocol.Format("type", "package", "id", "module:Example", "status", "uptodate", "installed", "1.0")), null, DateTime.Now);
            store.Apply("PC03", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Product", "status", "uptodate", "installed", "1.0")), null, DateTime.Now);
            store.GetOrAdd("PC03").Paused = true;
            List<UpdatePackage> packages = new List<UpdatePackage>
            {
                UpdatePackage.Parse("Example.Product | example | Example product"),
                UpdatePackage.Parse("module:Example"),
                UpdatePackage.Parse("module:Other"),
                UpdatePackage.Parse("Microsoft.Edge"),
                UpdatePackage.Parse("Never.Checked")
            };
            List<KeyValuePair<UpdatePackage, List<string>>> c = UpdateRunner.UninstallCandidates(
                new[] { "PC01", "PC02", "PC03", "pc01", "PC04" }, packages, store.Find, new[] { "example" });
            CollectionAssert.AreEqual(new[] { "Example.Product", "module:Example" }, c.Select(x => x.Key.Id).ToArray(), "Other does not declare it, Edge is protected");
            CollectionAssert.AreEqual(new[] { "PC01" }, c[0].Value, "PC02 does not have it, PC03 is paused, PC04 never checked");
            CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, c[1].Value);
        }

        [TestMethod]
        public void WingetArguments_MachineScopeSilent()
        {
            Assert.AreEqual("uninstall --id Example.Product -e --source winget --scope machine --silent --accept-source-agreements --disable-interactivity",
                WingetClient.UninstallArguments("Example.Product"));
            foreach (string bad in new[] { null, "", "module:Example", "Example Product", "Ex\"ample" })
            {
                try { WingetClient.UninstallArguments(bad); Assert.Fail("accepted: " + bad); }
                catch (ArgumentException) { }
            }
        }

        [TestMethod]
        public void TypedConfirmation_TheNumberOnly()
        {
            Assert.IsTrue(UninstallPolicy.IsTypedConfirmation("12", 12));
            Assert.IsTrue(UninstallPolicy.IsTypedConfirmation(" 6 ", 6));
            Assert.IsFalse(UninstallPolicy.IsTypedConfirmation("11", 12));
            Assert.IsFalse(UninstallPolicy.IsTypedConfirmation("yes", 6));
            Assert.IsFalse(UninstallPolicy.IsTypedConfirmation(null, 6));
        }

        [TestMethod]
        public void Guards_ServersAndSchedules()
        {
            Assert.AreEqual(ServerDecision.RefusedServer, ServerPolicy.Decide(UninstallPolicy.Command, ServerPolicy.ProductServer, false, false));
            Assert.AreEqual(ServerDecision.RefusedDomainController, ServerPolicy.Decide(UninstallPolicy.Command, ServerPolicy.ProductDomainController, true, false));
            Assert.IsFalse(ScheduleRunner.IsScheduleCommand(UninstallPolicy.Command), "a schedule never uninstalls");
            Assert.IsTrue(LiteDeploy.Worker.ModuleRunner.IsValidAction(UninstallPolicy.ModuleAction));
        }

        [TestMethod]
        public void WorkerArguments_UninstallAccepted()
        {
            string command;
            List<UpdatePackage> packages;
            int timeout;
            bool unsigned;
            Assert.IsNull(LiteDeploy.Worker.Program.ParseArguments(new[] { "uninstall", "--package", "Example.Product" }, out command, out packages, out timeout, out unsigned));
            Assert.AreEqual(UninstallPolicy.Command, command);
            Assert.AreEqual("Example.Product", packages.Single().Id);
        }

        [TestMethod]
        public void CachedInstaller_DeclaresUninstall()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "repository root not found");
            List<string> header = File.ReadLines(Path.Combine(dir, "modules", "CachedInstaller.ps1")).Take(20).ToList();
            Assert.IsTrue(UninstallPolicy.DeclaresUninstall(header));
            Assert.IsTrue(InstallPolicy.DeclaresInstall(header));
        }
    }
}
