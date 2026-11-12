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
            string command; List<UpdatePackage> packages; int timeout; bool unsigned;
            string error = Worker.Program.ParseArguments(new[] { "scan", "--package", "A.B|p1,p2|Label", "--package", "C.D||", "--timeout", "20" },
                out command, out packages, out timeout, out unsigned);
            Assert.IsNull(error);
            Assert.IsFalse(unsigned);
            Assert.AreEqual("scan", command);
            CollectionAssert.AreEqual(new[] { "A.B", "C.D" }, packages.Select(p => p.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "p1", "p2" }, packages[0].Processes);
            Assert.AreEqual(20, timeout);
        }

        [TestMethod]
        public void Worker_ParseArguments_Errors()
        {
            string command; List<UpdatePackage> packages; int timeout; bool unsigned;
            Assert.IsNotNull(Worker.Program.ParseArguments(new string[0], out command, out packages, out timeout, out unsigned));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "install", "--package", "A.B" }, out command, out packages, out timeout, out unsigned));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "scan" }, out command, out packages, out timeout, out unsigned));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "scan", "--package", "bad id" }, out command, out packages, out timeout, out unsigned));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "scan", "--package", "A.B", "--timeout", "0" }, out command, out packages, out timeout, out unsigned));
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

        // ---- Update modules ---------------------------------------------------------

        [TestMethod]
        public void Module_Ids_AreSafeFileNames()
        {
            UpdatePackage p = UpdatePackage.Parse("module:Adobe-Reader | Acrobat, AcroRd32 | Adobe Acrobat Reader");
            Assert.IsTrue(p.IsModule);
            Assert.AreEqual("Adobe-Reader", p.ModuleName);
            Assert.IsNull(UpdatePackage.Parse("module:..\\evil"));
            Assert.IsNull(UpdatePackage.Parse("module:a/b"));
            Assert.IsNull(UpdatePackage.Parse("module:.hidden"));
            Assert.IsNull(UpdatePackage.Parse("module:"));
            Assert.IsFalse(UpdatePackage.Parse("7zip.7zip").IsModule);
        }

        [TestMethod]
        public void Module_Header_LabelAndProcesses()
        {
            UpdateModule m = UpdateModules.Parse("AdobeReader", new[]
            {
                "\uFEFF# LiteDeploy-Module",
                "# Label: Adobe Acrobat Reader",
                "# Processes: Acrobat, AcroRd32, bad\"name",
                "param([string] $Action)"
            }, "x.ps1");
            Assert.AreEqual("Adobe Acrobat Reader", m.Label);
            CollectionAssert.AreEqual(new[] { "Acrobat", "AcroRd32" }, m.Processes);
            Assert.AreEqual("module:AdobeReader|Acrobat,AcroRd32|Adobe Acrobat Reader", m.ToPackage().ToArgument());
        }

        [TestMethod]
        public void Module_WithoutMarkerOrBadName_IsIgnored()
        {
            Assert.IsNull(UpdateModules.Parse("Script", new[] { "# Label: x", "Write-Output 1" }, "x.ps1"));
            Assert.IsNull(UpdateModules.Parse("bad name", new[] { "# LiteDeploy-Module" }, "x.ps1"));
        }

        [TestMethod]
        public void Module_SignatureRule()
        {
            Assert.IsTrue(ModuleRunner.MayRun(LiteSuite.UpdateSignatureVerdict.Accepted, false));
            Assert.IsFalse(ModuleRunner.MayRun(LiteSuite.UpdateSignatureVerdict.RejectedUnsigned, false));
            Assert.IsFalse(ModuleRunner.MayRun(LiteSuite.UpdateSignatureVerdict.AcceptedUnsignedDevBuild, false));
            Assert.IsTrue(ModuleRunner.MayRun(LiteSuite.UpdateSignatureVerdict.RejectedUnsigned, true));
            // Never, even when unsigned modules are allowed
            Assert.IsFalse(ModuleRunner.MayRun(LiteSuite.UpdateSignatureVerdict.RejectedTampered, true));
            Assert.IsFalse(ModuleRunner.MayRun(LiteSuite.UpdateSignatureVerdict.RejectedPublisherMismatch, true));
            Assert.IsFalse(ModuleRunner.MayRun(LiteSuite.UpdateSignatureVerdict.RejectedInvalidSignature, true));
        }

        [TestMethod]
        public void Module_Interpret_ResultLineAndExitCode()
        {
            string line = WorkerProtocol.Format("type", "module", "status", "outdated", "installed", "1.0", "available", "1.1");
            ModuleResult r = ModuleRunner.Interpret(0, "noise\r\n" + line + "\r\n", "");
            Assert.AreEqual("outdated", r.Status);
            Assert.AreEqual("1.1", r.Available);

            ModuleResult restart = ModuleRunner.Interpret(3010, WorkerProtocol.Format("type", "module", "status", "uptodate", "installed", "1.1"), "");
            Assert.AreEqual("uptodate", restart.Status);
            StringAssert.Contains(restart.Message, "Restart");

            Assert.AreEqual("error", ModuleRunner.Interpret(1, line, "boom").Status);
            Assert.AreEqual("error", ModuleRunner.Interpret(0, "no result line", "").Status);
            Assert.AreEqual("error", ModuleRunner.Interpret(0, WorkerProtocol.Format("type", "module", "status", "weird"), "").Status);
        }

        [TestMethod]
        public void Worker_ParseArguments_AllowUnsignedModules()
        {
            string command; List<UpdatePackage> packages; int timeout; bool unsigned;
            Assert.IsNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "module:Example||", "--allow-unsigned-modules" },
                out command, out packages, out timeout, out unsigned));
            Assert.IsTrue(unsigned);
            Assert.IsTrue(packages[0].IsModule);

            string args = UpdateRunner.BuildWorkerArguments("upgrade", packages, true);
            StringAssert.Contains(args, "--allow-unsigned-modules");
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("upgrade", new List<UpdatePackage> { UpdatePackage.Parse("A.B") }, true).Contains("unsigned"));
        }

        [TestMethod]
        public void ShippedModules_AreAsciiModules()
        {
            string[] files = Directory.GetFiles(Path.Combine(RepoRoot(), "modules"), "*.ps1");
            CollectionAssert.IsSubsetOf(new[] { "AdobeReader.ps1", "OnePassword.ps1", "OracleJava.ps1" }, files.Select(Path.GetFileName).ToArray());
            foreach (string path in files)
            {
                Assert.IsTrue(File.ReadAllBytes(path).All(b => b < 0x80), path + " must be ASCII only");
                Assert.IsNotNull(UpdateModules.Read(path), path + " is not a module");
            }
        }

        [TestMethod]
        public void ModuleTemplate_IsAsciiAndAModule()
        {
            string path = Path.Combine(RepoRoot(), "docs", "modules", "Template-Module.ps1");
            Assert.IsTrue(File.ReadAllBytes(path).All(b => b < 0x80), "the template must be ASCII only");
            Assert.IsNotNull(UpdateModules.Read(path));
        }

        // ---- Pause, business hours, cache, live ----------------------------------------

        [TestMethod]
        public void BusinessHours_WeekdaysBetweenStartAndEnd()
        {
            UserSettings s = new UserSettings { BusinessHoursStart = "07:00", BusinessHoursEnd = "17:00" };
            Assert.IsTrue(s.IsBusinessHours(new DateTime(2026, 11, 12, 9, 0, 0)));     // Thursday
            Assert.IsFalse(s.IsBusinessHours(new DateTime(2026, 11, 12, 17, 0, 0)));
            Assert.IsFalse(s.IsBusinessHours(new DateTime(2026, 11, 12, 6, 59, 0)));
            Assert.IsFalse(s.IsBusinessHours(new DateTime(2026, 11, 14, 10, 0, 0)));    // Saturday
            Assert.IsFalse(new UserSettings { BusinessHoursStart = "bad" }.IsBusinessHours(new DateTime(2026, 11, 12, 9, 0, 0)));
        }

        [TestMethod]
        public void Settings_Clone_KeepsEveryValue()
        {
            UserSettings s = new UserSettings { Theme = Theme.Dark, AllowUnsignedModules = true, UpdateCacheShare = @"\\srv\cache" };
            UserSettings c = s.Clone();
            c.Language = Localization.French;
            Assert.AreEqual(Theme.Dark, c.Theme);
            Assert.IsTrue(c.AllowUnsignedModules);
            Assert.AreEqual(@"\\srv\cache", c.UpdateCacheShare);
            Assert.AreNotEqual(c.Language, s.Language, "the copy is independent");
        }

        [TestMethod]
        public void Cache_PassedToModulesOnly_AndValidated()
        {
            List<UpdatePackage> modules = new List<UpdatePackage> { UpdatePackage.Parse("module:AdobeReader") };
            string args = UpdateRunner.BuildWorkerArguments("upgrade", modules, false, @"\\srv\deploy$\cache\");
            StringAssert.Contains(args, @"--cache ""\\srv\deploy$\cache""");
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("upgrade", new List<UpdatePackage> { UpdatePackage.Parse("A.B") }, false, @"\\srv\c").Contains("--cache"));

            Assert.IsTrue(Worker.Program.IsValidCachePath(@"\\srv\deploy$\cache"));
            Assert.IsFalse(Worker.Program.IsValidCachePath(@"C:\cache"));
            Assert.IsFalse(Worker.Program.IsValidCachePath(@"\\srv\..\x"));
            Assert.IsFalse(Worker.Program.IsValidCachePath(@"\\srv\a"" & calc"));
        }

        [TestMethod]
        public void State_PauseAndNightOnly_AreSaved()
        {
            string dir = Path.Combine(Path.GetTempPath(), "litedeploy-pause-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "updates.xml");
            try
            {
                UpdateStateStore store = new UpdateStateStore();
                ComputerUpdateState c = store.GetOrAdd("PC01");
                c.Paused = true;
                c.PauseReason = "Salle de formation";
                c.NightOnly = true;
                Assert.IsTrue(store.SaveIfChanged(path));
                ComputerUpdateState loaded = UpdateStateStore.Load(path).Find("pc01");
                Assert.IsTrue(loaded.Paused);
                Assert.IsTrue(loaded.NightOnly);
                Assert.AreEqual("Salle de formation", loaded.PauseReason);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
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
