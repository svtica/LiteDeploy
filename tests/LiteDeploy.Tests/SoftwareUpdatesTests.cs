// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareUpdatesTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Unit tests of the software updates: worker protocol, package list,
//          winget table reading (English and French headers, progress noise),
//          worker arguments and the state file (written only on change).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDeploy.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class SoftwareUpdatesTests
    {
        // ---- Protocol ---------------------------------------------------------------

        [TestMethod]
        public void Protocol_RoundTrip_EscapesTabsAndSpecialCharacters()
        {
            string line = WorkerProtocol.Format("type", "package", "id", "Notepad++.Notepad++", "message", "a\tb=c d\r\nend", "skipped", null);
            Assert.IsFalse(line.Contains("skipped"));
            WorkerMessage m = WorkerProtocol.Parse(line);
            Assert.AreEqual("package", m.Type);
            Assert.AreEqual("Notepad++.Notepad++", m["id"]);
            Assert.AreEqual("a\tb=c d\r\nend", m["message"]);
            Assert.IsNull(m["skipped"]);
        }

        [TestMethod]
        public void Protocol_ParseAll_IgnoresOtherLines()
        {
            string output = "PsExec v2.43 - Execute processes remotely\r\nConnecting to PC01...\r\n"
                + WorkerProtocol.Format("type", "start", "computer", "PC01") + "\r\n"
                + "LDW1 not a message\r\nLDW1\tnotype=1\r\n"
                + WorkerProtocol.Format("type", "done", "failed", "0") + "\r\n";
            List<WorkerMessage> all = WorkerProtocol.ParseAll(output);
            CollectionAssert.AreEqual(new[] { "start", "done" }, all.Select(m => m.Type).ToArray());
        }

        // ---- Package list -----------------------------------------------------------

        [TestMethod]
        public void Package_Parse_IdProcessesLabel()
        {
            UpdatePackage p = UpdatePackage.Parse(" 7zip.7zip | 7zFM, 7zG | 7-Zip | ^7-Zip ");
            Assert.AreEqual("7zip.7zip", p.Id);
            CollectionAssert.AreEqual(new[] { "7zFM", "7zG" }, p.Processes);
            Assert.AreEqual("7-Zip", p.DisplayName);
            Assert.AreEqual("7zip.7zip|7zFM,7zG|7-Zip", p.ToArgument());
            Assert.AreEqual("Notepad++.Notepad++", UpdatePackage.Parse("Notepad++.Notepad++").DisplayName);
        }

        [TestMethod]
        public void Package_Parse_RejectsCommentsBlanksAndUnsafeIds()
        {
            Assert.IsNull(UpdatePackage.Parse("# comment"));
            Assert.IsNull(UpdatePackage.Parse("   "));
            Assert.IsNull(UpdatePackage.Parse("Bad Id | x"));
            Assert.IsNull(UpdatePackage.Parse("Evil\" & calc | x"));
            // An unsafe process name is dropped, the package kept
            CollectionAssert.AreEqual(new[] { "ok" }, UpdatePackage.Parse("A.B | ok, bad\"name").Processes);
        }

        [TestMethod]
        public void Package_ParseFile_SkipsDuplicates()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[] { "# list", "A.B | x | First", "a.b | y | Duplicate", "C.D" });
                List<UpdatePackage> list = UpdatePackage.ParseFile(path);
                CollectionAssert.AreEqual(new[] { "A.B", "C.D" }, list.Select(p => p.Id).ToArray());
                Assert.AreEqual("First", list[0].Label);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ShippedPackageList_IsValid()
        {
            string path = Path.Combine(RepoRoot(), "updates", "packages.txt");
            List<UpdatePackage> list = UpdatePackage.ParseFile(path);
            Assert.IsTrue(list.Count >= 1);
            Assert.AreEqual(list.Count, File.ReadAllLines(path).Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")));
        }

        // ---- winget table -----------------------------------------------------------

        const string ListOutdated =
            "   - \r   \\ \r   | \r\r\n" +
            "Name                   Id                  Version Available Source\r\n" +
            "-------------------------------------------------------------------\r\n" +
            "Notepad++ (64-bit x64) Notepad++.Notepad++ 8.6.4   8.6.9     winget\r\n";

        [TestMethod]
        public void Winget_Outdated_ReadsInstalledAndAvailable()
        {
            WingetListRow row = WingetListParser.FindRow(ListOutdated, "notepad++.notepad++");
            Assert.IsNotNull(row);
            Assert.AreEqual("Notepad++ (64-bit x64)", row.Name);
            Assert.AreEqual("8.6.4", row.Version);
            Assert.AreEqual("8.6.9", row.Available);
        }

        [TestMethod]
        public void Winget_UpToDate_FrenchHeaders_NoAvailableColumn()
        {
            // The header words are not used: the columns are taken by position
            const string output =
                "Nom                Id        Version Source\r\n" +
                "-------------------------------------------\r\n" +
                "7-Zip 24.08 (x64)  7zip.7zip 24.08   winget\r\n";
            WingetListRow row = WingetListParser.FindRow(output, "7zip.7zip");
            Assert.IsNotNull(row);
            Assert.AreEqual("24.08", row.Version);
            Assert.IsNull(row.Available);
        }

        [TestMethod]
        public void Winget_NoTableOrOtherPackage_ReturnsNull()
        {
            Assert.IsNull(WingetListParser.FindRow("No installed package found matching input criteria.", "A.B"));
            Assert.IsNull(WingetListParser.FindRow(ListOutdated, "Other.Package"));
            Assert.IsNull(WingetListParser.FindRow(null, "A.B"));
        }

        [TestMethod]
        public void Winget_Describe_KnownCodes()
        {
            Assert.AreEqual("0x8A150014", WingetClient.Hex(WingetClient.PackageNotInstalled));
            StringAssert.Contains(WingetClient.Describe(unchecked((int)0x80190193)), "403");
        }

        // ---- Worker arguments -------------------------------------------------------

        [TestMethod]
        public void Worker_ParseArguments_ScanWithPackages()
        {
            string command; List<UpdatePackage> packages; int timeout;
            string error = Worker.Program.ParseArguments(new[] { "scan", "--package", "A.B|p1,p2|Label", "--package", "C.D||", "--timeout", "20" },
                out command, out packages, out timeout);
            Assert.IsNull(error);
            Assert.AreEqual("scan", command);
            CollectionAssert.AreEqual(new[] { "A.B", "C.D" }, packages.Select(p => p.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "p1", "p2" }, packages[0].Processes);
            Assert.AreEqual(20, timeout);
        }

        [TestMethod]
        public void Worker_ParseArguments_Errors()
        {
            string command; List<UpdatePackage> packages; int timeout;
            Assert.IsNotNull(Worker.Program.ParseArguments(new string[0], out command, out packages, out timeout));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "install", "--package", "A.B" }, out command, out packages, out timeout));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "scan" }, out command, out packages, out timeout));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "scan", "--package", "bad id" }, out command, out packages, out timeout));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "scan", "--package", "A.B", "--timeout", "0" }, out command, out packages, out timeout));
        }

        [TestMethod]
        public void Runner_WorkerArguments_RoundTripThroughWindowsQuoting()
        {
            List<UpdatePackage> list = new List<UpdatePackage>
            {
                UpdatePackage.Parse("7zip.7zip | 7zFM, 7zG | 7-Zip"),
                UpdatePackage.Parse("VideoLAN.VLC | vlc | VLC media player")
            };
            string args = UpdateRunner.BuildWorkerArguments("upgrade", list);
            Assert.AreEqual("upgrade --package \"7zip.7zip|7zFM,7zG|7-Zip\" --package \"VideoLAN.VLC|vlc|VLC media player\" --timeout 15", args);

            PsExecOptions o = UpdateRunner.BuildOptions("PC01", "scan", list);
            string psexec = PsExecCommandBuilder.BuildArguments(o);
            StringAssert.Contains(psexec, "\\\\PC01");
            StringAssert.Contains(psexec, " -c");
            StringAssert.Contains(psexec, " -f");
            StringAssert.Contains(psexec, " -s");
            Assert.IsFalse(psexec.Contains(" -u "), psexec);
        }

        // ---- State file -------------------------------------------------------------

        static List<WorkerMessage> Messages(params string[] lines)
        {
            return lines.Select(WorkerProtocol.Parse).ToList();
        }

        [TestMethod]
        public void State_Apply_RecordsVersionsAndResults()
        {
            UpdateStateStore store = new UpdateStateStore();
            DateTime t1 = new DateTime(2026, 11, 12, 9, 0, 0);
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "A.B", "status", "outdated", "installed", "1.0", "available", "1.1"),
                WorkerProtocol.Format("type", "upgrade", "id", "A.B", "result", "upgraded", "code", "0x00000000", "message", "OK"),
                WorkerProtocol.Format("type", "package", "id", "A.B", "status", "uptodate", "installed", "1.1")), null, t1);

            PackageUpdateState p = store.Find("pc01").Find("a.b");
            Assert.AreEqual("uptodate", p.Status);
            Assert.AreEqual("1.1", p.Installed);
            Assert.IsNull(p.Available);
            Assert.AreEqual("upgraded", p.LastResult);
            Assert.AreEqual(t1, p.LastUpgrade);
            Assert.AreEqual(t1, store.Find("PC01").LastChange);
            Assert.IsNull(store.Find("PC01").LastError);
        }

        [TestMethod]
        public void State_UnreachableComputer_KeepsLastVersions()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(WorkerProtocol.Format("type", "package", "id", "A.B", "status", "uptodate", "installed", "1.0")), null, DateTime.Now);
            store.Apply("PC01", new List<WorkerMessage>(), "PsExec exit code 53: The network path was not found.", DateTime.Now);
            Assert.AreEqual("1.0", store.Find("PC01").Find("A.B").Installed);
            StringAssert.Contains(store.Find("PC01").LastError, "network path");
        }

        [TestMethod]
        public void State_SkippedUpgrade_KeepsLastRealResult()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(WorkerProtocol.Format("type", "upgrade", "id", "A.B", "result", "failed", "code", "0x80190193", "message", "403")), null, DateTime.Now);
            store.Apply("PC01", Messages(WorkerProtocol.Format("type", "upgrade", "id", "A.B", "result", "skipped", "message", "Already up to date")), null, DateTime.Now);
            Assert.AreEqual("failed", store.Find("PC01").Find("A.B").LastResult);
        }

        [TestMethod]
        public void State_File_WrittenOnlyWhenSomethingChanged()
        {
            string dir = Path.Combine(Path.GetTempPath(), "litedeploy-state-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "updates.xml");
            try
            {
                List<WorkerMessage> scan = Messages(WorkerProtocol.Format("type", "package", "id", "A.B", "status", "uptodate", "installed", "1.0"));
                UpdateStateStore store = new UpdateStateStore();
                store.Apply("PC01", scan, null, new DateTime(2026, 11, 12, 9, 0, 0));
                Assert.IsTrue(store.SaveIfChanged(path), "first save");

                // Same result an hour later: no change, the file is not rewritten
                UpdateStateStore reloaded = UpdateStateStore.Load(path);
                reloaded.Apply("PC01", scan, null, new DateTime(2026, 11, 12, 10, 0, 0));
                Assert.IsFalse(reloaded.SaveIfChanged(path), "unchanged");
                Assert.AreEqual(new DateTime(2026, 11, 12, 9, 0, 0), UpdateStateStore.Load(path).Find("PC01").LastChange);

                // A new version: written
                reloaded.Apply("PC01", Messages(WorkerProtocol.Format("type", "package", "id", "A.B", "status", "outdated", "installed", "1.0", "available", "1.2")), null, new DateTime(2026, 11, 12, 11, 0, 0));
                Assert.IsTrue(reloaded.SaveIfChanged(path), "changed");
                Assert.AreEqual("1.2", UpdateStateStore.Load(path).Find("PC01").Find("A.B").Available);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        [TestMethod]
        public void State_UnreadableFile_StartsEmpty()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "not xml");
                Assert.AreEqual(0, UpdateStateStore.Load(path).Computers.Count);
            }
            finally { File.Delete(path); }
        }

        static string RepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "repository root not found");
            return dir;
        }
    }
}
