// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateSummaryTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Unit tests of the one-line-per-computer view of Software Updates
//          (ComputerSummary), of "not installed is not an error" in the worker
//          (WingetListParser.Classify), of the credential offer before a run
//          and of the Active Directory search of the computer list.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using LiteDeploy.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class UpdateSummaryTests
    {
        static List<WorkerMessage> Messages(params string[] lines)
        {
            return lines.Select(WorkerProtocol.Parse).ToList();
        }

        static readonly List<UpdatePackage> Packages = new List<UpdatePackage>
        {
            UpdatePackage.Parse("A.A||App A"),
            UpdatePackage.Parse("B.B||App B"),
            UpdatePackage.Parse("C.C||App C"),
        };

        // ---- Worker: a package that is not installed is not an error ----------------

        [TestMethod]
        public void Classify_NotInstalledCodes_AreNotErrors()
        {
            WingetListRow row;
            Assert.AreEqual(WorkerProtocol.StatusNotInstalled, WingetListParser.Classify(WingetClient.PackageNotInstalled, "", "A.B", out row));
            Assert.AreEqual(WorkerProtocol.StatusNotInstalled, WingetListParser.Classify(WingetClient.NoPackageFound, "", "A.B", out row));
            // Success without the package: no table, or a table of another package
            Assert.AreEqual(WorkerProtocol.StatusNotInstalled, WingetListParser.Classify(0, "No installed package found matching input criteria.", "A.B", out row));
            Assert.AreEqual(WorkerProtocol.StatusNotInstalled, WingetListParser.Classify(0,
                "Name   Id          Version Source\r\n----------------------------------\r\nOther  Other.Pkg   1.0     winget\r\n", "A.B", out row));
            Assert.IsNull(row);
        }

        [TestMethod]
        public void Classify_OtherCodes_AreErrors_AndRowsAreRead()
        {
            WingetListRow row;
            Assert.AreEqual(WorkerProtocol.StatusError, WingetListParser.Classify(WingetClient.SourcesUnavailable, "", "A.B", out row));
            Assert.AreEqual(WorkerProtocol.StatusError, WingetListParser.Classify(-1, "", "A.B", out row));
            const string outdated =
                "Name      Id        Version Available Source\r\n" +
                "--------------------------------------------\r\n" +
                "App A     A.B       1.0     1.1       winget\r\n";
            Assert.AreEqual(WorkerProtocol.StatusOutdated, WingetListParser.Classify(0, outdated, "a.b", out row));
            Assert.AreEqual("1.1", row.Available);
        }

        [TestMethod]
        public void FindRow_ShortenedId_Matches()
        {
            const string output =
                "Name          Id                Version Source\r\n" +
                "----------------------------------------------\r\n" +
                "Long product  Vendor.LongProd…  2.0     winget\r\n";
            WingetListRow row = WingetListParser.FindRow(output, "Vendor.LongProductName");
            Assert.IsNotNull(row);
            Assert.AreEqual("2.0", row.Version);
            Assert.IsNull(WingetListParser.FindRow(output, "Other.LongProductName"));
        }

        // ---- One line per computer ---------------------------------------------------

        [TestMethod]
        public void Summary_OutdatedAndNotInstalled_OneLine()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "A.A", "status", "outdated", "installed", "1.0", "available", "1.1"),
                WorkerProtocol.Format("type", "package", "id", "B.B", "status", "notinstalled"),
                WorkerProtocol.Format("type", "package", "id", "C.C", "status", "uptodate", "installed", "3.0")), null, DateTime.Now);

            ComputerSummary s = ComputerSummary.Of(store.Find("PC01"), Packages);
            Assert.AreEqual(ComputerSummaryKind.Outdated, s.Kind);
            CollectionAssert.AreEqual(new[] { "A.A" }, s.Outdated.Select(p => p.Id).ToArray());
            Assert.AreEqual(0, s.Errors.Count, "a package that is not installed is not an error");
            Assert.AreEqual(1, s.NotInstalled);
            Assert.AreEqual(1, s.UpToDate);
        }

        [TestMethod]
        public void Summary_NothingInstalled_IsUpToDate()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "A.A", "status", "notinstalled"),
                WorkerProtocol.Format("type", "package", "id", "B.B", "status", "notinstalled")), null, DateTime.Now);
            Assert.AreEqual(ComputerSummaryKind.UpToDate, ComputerSummary.Of(store.Find("PC01"), Packages).Kind);
        }

        [TestMethod]
        public void Summary_ErrorsUnreachablePausedNotChecked()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "A.A", "status", "error", "message", "winget list 0x8A15000F"),
                WorkerProtocol.Format("type", "package", "id", "B.B", "status", "outdated", "installed", "1", "available", "2")), null, DateTime.Now);
            ComputerSummary s = ComputerSummary.Of(store.Find("PC01"), Packages);
            Assert.AreEqual(ComputerSummaryKind.Errors, s.Kind);
            Assert.AreEqual(1, s.Errors.Count);
            Assert.AreEqual(1, s.Outdated.Count);

            store.Apply("PC02", new List<WorkerMessage>(), "unreachable", DateTime.Now);
            Assert.AreEqual(ComputerSummaryKind.Unreachable, ComputerSummary.Of(store.Find("PC02"), Packages).Kind);

            store.GetOrAdd("PC03").Paused = true;
            Assert.AreEqual(ComputerSummaryKind.Paused, ComputerSummary.Of(store.Find("PC03"), Packages).Kind);

            Assert.AreEqual(ComputerSummaryKind.NotChecked, ComputerSummary.Of(null, Packages).Kind);
        }

        [TestMethod]
        public void Summary_FailedUpdate_CountsOnlyWhileStillOutdated()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "A.A", "status", "outdated", "installed", "1", "available", "2"),
                WorkerProtocol.Format("type", "upgrade", "id", "A.A", "result", "failed", "message", "403")), null, DateTime.Now);
            Assert.AreEqual(ComputerSummaryKind.Errors, ComputerSummary.Of(store.Find("PC01"), Packages).Kind);

            // Updated since (by hand): the old failure is no longer an error
            store.Apply("PC01", Messages(WorkerProtocol.Format("type", "package", "id", "A.A", "status", "uptodate", "installed", "2")), null, DateTime.Now);
            Assert.AreEqual(ComputerSummaryKind.UpToDate, ComputerSummary.Of(store.Find("PC01"), Packages).Kind);
        }

        // ---- Credential offer --------------------------------------------------------

        [TestMethod]
        public void CredentialOffer_OnlyWhenNoneChosen_OncePerSession()
        {
            Assert.AreEqual(CredentialOffer.Add, UpdateRunner.OfferFor(null, 0, false));
            Assert.AreEqual(CredentialOffer.UseStored, UpdateRunner.OfferFor("", 1, false));
            Assert.AreEqual(CredentialOffer.ChooseInOptions, UpdateRunner.OfferFor(null, 3, false));
            Assert.AreEqual(CredentialOffer.None, UpdateRunner.OfferFor("Admin", 0, false));
            Assert.AreEqual(CredentialOffer.None, UpdateRunner.OfferFor(null, 0, true));
        }

        // ---- Active Directory search of the computer list ----------------------------

        [TestMethod]
        public void FindComputers_OptionsAndEmptyKey()
        {
            string[] names = { "PC-001", "PC-002", "LAB-PC-01", "SRV01" };
            Assert.AreEqual(4, ComputerListForm.FindComputers(names, "", true, false, false).Count, "empty key: every computer");
            CollectionAssert.AreEqual(new[] { "PC-001", "PC-002" }, ComputerListForm.FindComputers(names, "pc-", true, false, false));
            CollectionAssert.AreEqual(new[] { "PC-001", "PC-002", "LAB-PC-01" }, ComputerListForm.FindComputers(names, "pc", false, true, false));
            Assert.AreEqual(0, ComputerListForm.FindComputers(names, "pc", true, false, true).Count, "case-sensitive");
            CollectionAssert.AreEqual(new[] { "SRV01" }, ComputerListForm.FindComputers(names, "srv01", false, false, false), "exact name");
        }
    }
}
