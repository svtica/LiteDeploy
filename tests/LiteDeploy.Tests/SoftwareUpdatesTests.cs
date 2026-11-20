// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareUpdatesTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Unit tests of the software updates: worker protocol, package list,
//          winget table reading (English and French headers, progress noise),
//          worker arguments, the state file (written only on change), the
//          extra log folders setting, the explicit Install action (header,
//          decision, worker and winget arguments), module Ids, instances
//          ("module:Name/Instance") and the shipped modules.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        public void Winget_OneSource_NoSourceColumn_ThreeColumns()
        {
            // winget limited to one source (--source winget) has no Source column: up to date gives 3 columns
            const string output =
                "Nom                       ID        Version\r\n" +
                "----------------------------------------------\r\n" +
                "7-Zip 26.06 (x64 edition) 7zip.7zip 26.06.00.0\r\n";
            WingetListRow row = WingetListParser.FindRow(output, "7zip.7zip");
            Assert.IsNotNull(row, "installed, not \"not installed\"");
            Assert.AreEqual("26.06.00.0", row.Version);
            Assert.IsNull(row.Available);
            WingetListRow found;
            Assert.AreEqual(WorkerProtocol.StatusUpToDate, WingetListParser.Classify(0, output, "7zip.7zip", out found));
        }

        [TestMethod]
        public void Winget_OneSource_AvailableAsFourthColumn()
        {
            // An update available, no Source column: the fourth column is Available (it was read as Source)
            const string output =
                "Nom                    ID                  Version Disponible\r\n" +
                "-------------------------------------------------------------\r\n" +
                "Notepad++ (64-bit x64) Notepad++.Notepad++ 8.6.4   8.6.9\r\n";
            WingetListRow row = WingetListParser.FindRow(output, "Notepad++.Notepad++");
            Assert.AreEqual("8.6.9", row.Available);
            WingetListRow found;
            Assert.AreEqual(WorkerProtocol.StatusOutdated, WingetListParser.Classify(0, output, "Notepad++.Notepad++", out found));

            // A fourth column holding the source name is not a version, whatever its header says
            const string unknownHeader =
                "Name           Id           Version Herkunft\r\n" +
                "--------------------------------------------\r\n" +
                "VLC media      VideoLAN.VLC 3.0.21  winget\r\n";
            Assert.IsNull(WingetListParser.FindRow(unknownHeader, "VideoLAN.VLC").Available);
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
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "remove", "--package", "A.B" }, out command, out packages, out timeout, out unsigned));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "install" }, out command, out packages, out timeout, out unsigned), "install needs a package");
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
            Assert.IsNull(p.InstanceName, "a plain module has no instance");
            Assert.AreEqual("Adobe-Reader", p.SettingsKey);
            Assert.IsNull(UpdatePackage.Parse("module:..\\evil"));
            Assert.IsNull(UpdatePackage.Parse("module:.hidden"));
            Assert.IsNull(UpdatePackage.Parse("module:"));
            Assert.IsFalse(UpdatePackage.Parse("7zip.7zip").IsModule);
            Assert.IsNull(UpdatePackage.Parse("7zip.7zip").InstanceName);
        }

        [TestMethod]
        public void Module_InstanceIds_ParseAndValidate()
        {
            UpdatePackage p = UpdatePackage.Parse("module:CachedInstaller/ContosoExampleApp | | Contoso Example App");
            Assert.IsNotNull(p);
            Assert.IsTrue(p.IsModule);
            Assert.AreEqual("CachedInstaller", p.ModuleName);
            Assert.AreEqual("ContosoExampleApp", p.InstanceName);
            Assert.AreEqual("CachedInstaller/ContosoExampleApp", p.SettingsKey);
            Assert.AreEqual("Contoso Example App", p.DisplayName);
            Assert.AreEqual("module:CachedInstaller/ContosoExampleApp||Contoso Example App", p.ToArgument());
            Assert.AreEqual("module:CachedInstaller/Example_App-2.x", UpdatePackage.ModuleId("CachedInstaller", "Example_App-2.x"));
            Assert.AreEqual("module:CachedInstaller", UpdatePackage.ModuleId("CachedInstaller", null));

            foreach (string bad in new[] { "module:a/", "module:/b", "module:a/b/c", "module:a/.b", "module:a/..", "module:a/b c",
                                           @"module:a/b\c", "module:a/b:c", "module:a/b@c", "module:a/café", "module:a/" + new string('x', 65) })
                Assert.IsFalse(UpdatePackage.IsValidId(bad), bad);
            Assert.IsTrue(UpdatePackage.IsValidId("module:a/" + new string('x', 64)));
            Assert.IsTrue(UpdatePackage.IsValidInstanceName("ContosoExampleApp"));
            Assert.IsFalse(UpdatePackage.IsValidInstanceName(null));
            // A winget Id never takes the instance form
            Assert.IsFalse(UpdatePackage.IsValidId("Vendor.App/Instance"));
        }

        [TestMethod]
        public void Module_PlainIds_Unchanged()
        {
            // Ids that were valid before instances keep their meaning
            foreach (string id in new[] { "module:AdobeReader", "module:Adobe-Reader", "module:My.Module_2", "module:Modulé" })
            {
                UpdatePackage p = UpdatePackage.Parse(id + " | a, b | Label");
                Assert.IsNotNull(p, id);
                Assert.AreEqual(id.Substring("module:".Length), p.ModuleName, id);
                Assert.IsNull(p.InstanceName, id);
                Assert.AreEqual(id + "|a,b|Label", p.ToArgument(), id);
                Assert.AreEqual(p.ModuleName + ModuleParameters.FileExtension, ModuleParameters.FileName(p), id);
            }
            Assert.IsNull(ModuleParameters.FileName(UpdatePackage.Parse("7zip.7zip")));
        }

        [TestMethod]
        public void Module_Instances_OwnParameterFileAndRow()
        {
            UpdatePackage a = UpdatePackage.Parse("module:CachedInstaller/ContosoExampleApp");
            UpdatePackage b = UpdatePackage.Parse("module:CachedInstaller/OtherExampleApp");
            UpdatePackage plain = UpdatePackage.Parse("module:CachedInstaller");
            Assert.AreEqual("CachedInstaller@ContosoExampleApp.params", ModuleParameters.FileName(a));
            Assert.AreEqual("CachedInstaller@OtherExampleApp.params", ModuleParameters.FileName(b));
            Assert.AreEqual("CachedInstaller.params", ModuleParameters.FileName(plain));
            // Same script for every instance
            Assert.AreEqual(ModuleRunner.ScriptPath(plain), ModuleRunner.ScriptPath(a));
            Assert.AreNotEqual(ModuleRunner.ParametersPath(a), ModuleRunner.ParametersPath(b));

            // The worker keeps both instances (distinct Ids)
            string command; List<UpdatePackage> packages; int timeout; bool unsigned;
            Assert.IsNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", a.ToArgument(), "--package", b.ToArgument(), "--package", a.ToArgument() },
                out command, out packages, out timeout, out unsigned));
            CollectionAssert.AreEqual(new[] { a.Id, b.Id }, packages.Select(x => x.Id).ToArray());
        }

        [TestMethod]
        public void Module_InstancesHeader()
        {
            string[] header = { "# LiteDeploy-Module", "# Label: Example", "# Instances: yes" };
            Assert.IsTrue(ModuleParameters.DeclaresInstances(header));
            Assert.IsFalse(ModuleParameters.DeclaresInstances(new[] { "# LiteDeploy-Module", "# Instances: no" }));
            Assert.IsFalse(ModuleParameters.DeclaresInstances(null));

            UpdateModule m = UpdateModules.Parse("Example", header, "x.ps1");
            Assert.IsTrue(m.Instances);
            UpdatePackage p = m.ToPackage("ContosoExampleApp", "Contoso | Example App");
            Assert.AreEqual("module:Example/ContosoExampleApp||Contoso   Example App", p.ToArgument());
            Assert.AreEqual("ContosoExampleApp", m.ToPackage("ContosoExampleApp", null).Label, "label: the instance name by default");
            Assert.IsNull(m.ToPackage("bad name", null));

            UpdateModule single = UpdateModules.Parse("Single", new[] { "# LiteDeploy-Module" }, "x.ps1");
            Assert.IsFalse(single.Instances);
            Assert.IsNull(single.ToPackage("ContosoExampleApp", null), "no instance for a module that does not declare it");
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

        static UpdateModule FakeModule(string name, string dir)
        {
            return new UpdateModule { Name = name, Label = name, Path = Path.Combine(dir, name + ".ps1") };
        }

        [TestMethod]
        public void ModuleScan_LaterFolderWins_UserOverSharedOverShipped()
        {
            Dictionary<string, UpdateModule[]> folders = new Dictionary<string, UpdateModule[]>
            {
                { "shipped", new[] { FakeModule("AdobeReader", "shipped"), FakeModule("OracleJava", "shipped"), FakeModule("OnePassword", "shipped") } },
                { "shared", new[] { FakeModule("adobereader", "shared"), FakeModule("OnePassword", "shared"), FakeModule("InHouse", "shared") } },
                { "user", new[] { FakeModule("OnePassword", "user") } },
            };
            List<string> skipped = new List<string>();
            List<UpdateModule> list = UpdateModules.Scan(new[] { "shipped", "shared", "user" }, d => folders[d], skipped);

            CollectionAssert.AreEqual(new[] { "adobereader", "InHouse", "OnePassword", "OracleJava" }, list.Select(m => m.Name).ToArray());
            Assert.AreEqual(Path.Combine("shared", "adobereader.ps1"), list[0].Path);
            Assert.AreEqual(Path.Combine("shared", "InHouse.ps1"), list[1].Path);
            Assert.AreEqual(Path.Combine("user", "OnePassword.ps1"), list[2].Path);
            Assert.AreEqual(Path.Combine("shipped", "OracleJava.ps1"), list[3].Path);
            Assert.AreEqual(0, skipped.Count);
        }

        [TestMethod]
        public void ModuleScan_MissingOrUnreachableFolder_IsSkipped()
        {
            List<string> skipped = new List<string>();
            List<UpdateModule> list = UpdateModules.Scan(new[] { "shipped", "missing", "unreachable", "empty" }, d =>
            {
                if (d == "missing") return null;
                if (d == "unreachable") throw new IOException("The network path was not found.");
                if (d == "empty") return new UpdateModule[0];
                return new[] { FakeModule("OracleJava", d), null };
            }, skipped);

            CollectionAssert.AreEqual(new[] { "OracleJava" }, list.Select(m => m.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "missing", "unreachable" }, skipped);
        }

        [TestMethod]
        public void ModuleScan_RealFolders_EmptyAndMissing()
        {
            string root = Path.Combine(Path.GetTempPath(), "litedeploy-modules-" + Guid.NewGuid().ToString("N"));
            string shipped = Path.Combine(root, "shipped"), empty = Path.Combine(root, "empty"), missing = Path.Combine(root, "missing");
            try
            {
                Directory.CreateDirectory(shipped);
                Directory.CreateDirectory(empty);
                File.WriteAllLines(Path.Combine(shipped, "Example.ps1"), new[] { "# LiteDeploy-Module", "# Label: Example" });
                File.WriteAllLines(Path.Combine(shipped, "NotAModule.ps1"), new[] { "Write-Output 1" });
                List<string> skipped = new List<string>();
                Func<string, IEnumerable<UpdateModule>> readDir = d => Directory.Exists(d) ? Directory.GetFiles(d, "*.ps1").Select(UpdateModules.Read) : null;

                List<UpdateModule> list = UpdateModules.Scan(new[] { shipped, empty, missing }, readDir, skipped);

                CollectionAssert.AreEqual(new[] { "Example" }, list.Select(m => m.Name).ToArray());
                CollectionAssert.AreEqual(new[] { missing }, skipped);
                Assert.AreEqual(0, UpdateModules.Scan(new[] { empty, missing }, readDir, null).Count);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void ModuleDirs_SharedFolderBetweenShippedAndUser()
        {
            UserSettings saved = UserSettings.Current;
            try
            {
                string shared = Path.Combine(Path.GetTempPath(), "litedeploy-shared-modules");
                typeof(UserSettings).GetProperty("Current").SetValue(null, new UserSettings { SharedModulesFolder = "  " + shared + " " });
                CollectionAssert.AreEqual(new[] { UpdateModules.ShippedDir, shared, UpdateModules.UserDir }, UpdateModules.Dirs);
                typeof(UserSettings).GetProperty("Current").SetValue(null, new UserSettings());
                CollectionAssert.AreEqual(new[] { UpdateModules.ShippedDir, UpdateModules.UserDir }, UpdateModules.Dirs);
            }
            finally
            {
                typeof(UserSettings).GetProperty("Current").SetValue(null, saved);
            }
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
            CollectionAssert.IsSubsetOf(new[] { "AdobeReader.ps1", "CachedInstaller.ps1", "MsixProvision.ps1", "OnePassword.ps1", "OracleJava.ps1", "PrinterDrivers.ps1", "TeamsBootstrapper.ps1" },
                files.Select(Path.GetFileName).ToArray());
            foreach (string path in files)
            {
                Assert.IsTrue(File.ReadAllBytes(path).All(b => b < 0x80), path + " must be ASCII only");
                Assert.IsNotNull(UpdateModules.Read(path), path + " is not a module");
            }
        }

        [TestMethod]
        public void ShippedModules_FollowTheContract()
        {
            String resultFunction = ResultFunction(File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "modules", "Template-Module.ps1")));
            Assert.IsNotNull(resultFunction, "template: Write-LiteDeployResult");
            foreach (string path in Directory.GetFiles(Path.Combine(RepoRoot(), "modules"), "*.ps1"))
            {
                string[] lines = File.ReadAllLines(path);
                string[] header = lines.Take(20).Select(l => l.Trim()).ToArray();
                Assert.AreEqual(UpdateModules.Marker, header[0], path + ": first line");
                Assert.IsTrue(header.Any(l => l.StartsWith("# Label:", StringComparison.Ordinal) && l.Length > "# Label:".Length + 1), path + ": # Label:");
                Assert.IsTrue(header.Any(l => l.StartsWith("# Processes:", StringComparison.Ordinal)), path + ": # Processes:");
                string text = String.Join("\n", lines);
                // Check and Update at least ('Install' is optional, for modules that declare "# Install: yes")
                foreach (string required in new[] { ".SYNOPSIS", ".DESCRIPTION", ".PARAMETER Action", "[ValidateSet('Check', 'Update'", "$ErrorActionPreference = 'Stop'" })
                    StringAssert.Contains(text, required, path);
                // Install in the ValidateSet exactly when the header declares "# Install: yes"
                StringAssert.Contains(text, InstallPolicy.DeclaresInstall(header)
                    ? "[ValidateSet('Check', 'Update', 'Install')]"
                    : "[ValidateSet('Check', 'Update')]", path);
                Assert.AreEqual(resultFunction, ResultFunction(lines), path + ": Write-LiteDeployResult must be the one of the template");
            }
        }

        [TestMethod]
        public void ShippedModules_PrinterDriversAndTeams_Processes()
        {
            UpdateModule drivers = UpdateModules.Read(Path.Combine(RepoRoot(), "modules", "PrinterDrivers.ps1"));
            Assert.AreEqual(0, drivers.Processes.Length, "nothing to close for the printer drivers");
            Assert.AreEqual("module:PrinterDrivers||Printer drivers (shared cache)", drivers.ToPackage().ToArgument());
            UpdateModule teams = UpdateModules.Read(Path.Combine(RepoRoot(), "modules", "TeamsBootstrapper.ps1"));
            CollectionAssert.AreEqual(new[] { "ms-teams" }, teams.Processes);
        }

        [TestMethod]
        public void ShippedModules_MsixProvision_Header()
        {
            string path = Path.Combine(RepoRoot(), "modules", "MsixProvision.ps1");
            UpdateModule msix = UpdateModules.Read(path);
            Assert.AreEqual(0, msix.Processes.Length, "nothing to close");
            Assert.AreEqual("MSIX package (provisioned)", msix.Label);
            Assert.IsTrue(msix.Instances, "one package row per MSIX package");
            CollectionAssert.AreEqual(new[] { "PackageName", "Folder", "File", "Dependencies", "License", "Features", "Publisher", "WingetId" },
                msix.Parameters.Select(p => p.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "PackageName", "Folder" }, msix.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray());
            Assert.IsFalse(msix.Parameters.Any(p => p.Secret));
            Assert.IsTrue(msix.Parameters.Where(p => !p.Required).All(p => p.Default == ""), "optional parameters default to empty");
            string[] header = File.ReadLines(path).Take(20).Select(l => l.Trim()).ToArray();
            CollectionAssert.Contains(header, "# Install: yes");
            string text = File.ReadAllText(path);
            Assert.IsFalse(text.Contains("Invoke-Expression"), "no Invoke-Expression");
            StringAssert.Contains(text, @"Join-Path $env:windir ""LiteDeploy\work\MsixProvision\$packageName""", "work folder under %windir%");
        }

        [TestMethod]
        public void ShippedModules_CachedInstaller_Header()
        {
            UpdateModule m = UpdateModules.Read(Path.Combine(RepoRoot(), "modules", "CachedInstaller.ps1"));
            Assert.IsNotNull(m);
            Assert.IsTrue(m.Instances, "one package row per product");
            Assert.AreEqual(0, m.Processes.Length, "per-product processes: CloseProcesses parameter");
            CollectionAssert.AreEqual(new[] { "Product", "File", "Type", "Arguments", "DetectName", "ProductCode", "Version", "Publisher", "CloseProcesses", "WingetId" },
                m.Parameters.Select(p => p.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "File", "Publisher" }, m.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray());
            Assert.IsFalse(m.Parameters.Any(p => p.Secret));
            string text = File.ReadAllText(Path.Combine(RepoRoot(), "modules", "CachedInstaller.ps1"));
            StringAssert.Contains(text, "[ValidateSet('Check', 'Update', 'Install')]");
            StringAssert.Contains(text, "# Install: yes");
        }

        [TestMethod]
        public void ShippedModules_ParseInWindowsPowerShell()
        {
            String exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(exe)) Assert.Inconclusive("Windows PowerShell not available");
            foreach (string path in Directory.GetFiles(Path.Combine(RepoRoot(), "modules"), "*.ps1"))
            {
                String command = "$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('" + path.Replace("'", "''") + "', [ref]$null, [ref]$e); $e | ForEach-Object { $_.ToString() }; exit @($e).Count";
                // -EncodedCommand: no quoting of the command line
                ProcessStartInfo psi = new ProcessStartInfo(exe, "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)))
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    String output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    Assert.AreEqual(0, p.ExitCode, path + ": " + output);
                }
            }
        }

        [TestMethod]
        public void Module_EmptyProcessesLine_ClosesNothing()
        {
            UpdateModule m = UpdateModules.Parse("Example", new[] { "# LiteDeploy-Module", "# Label: Example", "# Processes:" }, "x.ps1");
            Assert.AreEqual(0, m.Processes.Length);
        }

        // Lines of "function Write-LiteDeployResult" up to its closing brace; null when absent
        static string ResultFunction(string[] lines)
        {
            int start = Array.FindIndex(lines, l => l.StartsWith("function Write-LiteDeployResult", StringComparison.Ordinal));
            if (start < 0) return null;
            int end = Array.FindIndex(lines, start, l => l == "}");
            return end < 0 ? null : String.Join("\n", lines.Skip(start).Take(end - start + 1));
        }

        [TestMethod]
        public void ModuleTemplate_IsAsciiAndAModule()
        {
            string path = Path.Combine(RepoRoot(), "docs", "modules", "Template-Module.ps1");
            Assert.IsTrue(File.ReadAllBytes(path).All(b => b < 0x80), "the template must be ASCII only");
            Assert.IsNotNull(UpdateModules.Read(path));
        }

        [TestMethod]
        public void ModuleTemplate_ShowsTheInstallAction()
        {
            string path = Path.Combine(RepoRoot(), "docs", "modules", "Template-Module.ps1");
            Assert.IsTrue(UpdateModules.Read(path).Install, "the template declares # Install: yes");
            string text = File.ReadAllText(path);
            StringAssert.Contains(text, "[ValidateSet('Check', 'Update', 'Install')]");
            StringAssert.Contains(text, "$Action -eq 'Install'");
        }

        // ---- Install action -----------------------------------------------------------

        [TestMethod]
        public void Install_Header_YesOnly_DefaultNo()
        {
            Assert.IsTrue(InstallPolicy.DeclaresInstall(new[] { "# LiteDeploy-Module", "# Install: yes" }));
            Assert.IsTrue(InstallPolicy.DeclaresInstall(new[] { "﻿# install:  YES  " }));
            Assert.IsFalse(InstallPolicy.DeclaresInstall(new[] { "# LiteDeploy-Module", "# Label: Example" }), "default: no");
            Assert.IsFalse(InstallPolicy.DeclaresInstall(new[] { "# Install: no" }));
            Assert.IsFalse(InstallPolicy.DeclaresInstall(new[] { "# Install: yes please" }));
            Assert.IsFalse(InstallPolicy.DeclaresInstall(new[] { "# Install:" }));
            Assert.IsFalse(InstallPolicy.DeclaresInstall(new[] { "#Install: yes" }));
            Assert.IsFalse(InstallPolicy.DeclaresInstall(null));
        }

        [TestMethod]
        public void Install_ModuleHeader_ParsedWithLabelProcessesAndParameters()
        {
            UpdateModule m = UpdateModules.Parse("Example", new[]
            {
                "# LiteDeploy-Module",
                "# Label: Example product",
                "# Processes: example",
                "# Install: yes",
                "# Parameter: Environment default=Production Name of the environment"
            }, "x.ps1");
            Assert.IsTrue(m.Install);
            Assert.AreEqual("Example product", m.Label);
            CollectionAssert.AreEqual(new[] { "example" }, m.Processes);
            Assert.AreEqual(1, m.Parameters.Count);
            Assert.IsFalse(UpdateModules.Parse("Example", new[] { "# LiteDeploy-Module", "# Label: Example" }, "x.ps1").Install);
        }

        [TestMethod]
        public void Install_ModuleHeader_OnlyTheFirst20Lines()
        {
            string path = Path.GetTempFileName();
            try
            {
                List<string> lines = new List<string> { "# LiteDeploy-Module" };
                lines.AddRange(Enumerable.Repeat("# filler", 19));
                lines.Add("# Install: yes");
                File.WriteAllLines(path, lines);
                UpdateModule m = UpdateModules.Read(path);
                Assert.IsNotNull(m);
                Assert.IsFalse(m.Install, "line 21 is not part of the header");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void Install_Decision()
        {
            // winget package: the module declaration does not matter
            Assert.AreEqual(InstallDecision.Install, InstallPolicy.Decide(false, false, WorkerProtocol.StatusNotInstalled));
            Assert.AreEqual(InstallDecision.AlreadyInstalled, InstallPolicy.Decide(false, false, WorkerProtocol.StatusUpToDate));
            Assert.AreEqual(InstallDecision.AlreadyInstalled, InstallPolicy.Decide(false, false, WorkerProtocol.StatusOutdated));
            Assert.AreEqual(InstallDecision.CheckFailed, InstallPolicy.Decide(false, false, WorkerProtocol.StatusError));
            Assert.AreEqual(InstallDecision.CheckFailed, InstallPolicy.Decide(false, false, null));
            // module: only with "# Install: yes"
            Assert.AreEqual(InstallDecision.Install, InstallPolicy.Decide(true, true, WorkerProtocol.StatusNotInstalled));
            Assert.AreEqual(InstallDecision.NotSupported, InstallPolicy.Decide(true, false, WorkerProtocol.StatusNotInstalled));
            Assert.AreEqual(InstallDecision.NotSupported, InstallPolicy.Decide(true, false, WorkerProtocol.StatusUpToDate));
            Assert.AreEqual(InstallDecision.AlreadyInstalled, InstallPolicy.Decide(true, true, WorkerProtocol.StatusOutdated));
            Assert.AreEqual(InstallDecision.CheckFailed, InstallPolicy.Decide(true, true, WorkerProtocol.StatusError));

            UpdatePackage winget = UpdatePackage.Parse("Example.Product");
            UpdatePackage module = UpdatePackage.Parse("module:Example");
            Assert.IsTrue(InstallPolicy.CanOffer(winget, WorkerProtocol.StatusNotInstalled, false));
            Assert.IsFalse(InstallPolicy.CanOffer(winget, WorkerProtocol.StatusOutdated, false));
            Assert.IsFalse(InstallPolicy.CanOffer(winget, null, false), "never checked: not offered");
            Assert.IsTrue(InstallPolicy.CanOffer(module, WorkerProtocol.StatusNotInstalled, true));
            Assert.IsFalse(InstallPolicy.CanOffer(module, WorkerProtocol.StatusNotInstalled, false));
            Assert.IsFalse(InstallPolicy.CanOffer(null, WorkerProtocol.StatusNotInstalled, true));
            StringAssert.Contains(InstallPolicy.NotSupportedMessage("Example"), "# Install: yes");
        }

        [TestMethod]
        public void Install_Candidates_SelectedNotInstalledNotPausedDeclared()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Product", "status", "notinstalled"),
                WorkerProtocol.Format("type", "package", "id", "module:Example", "status", "notinstalled"),
                WorkerProtocol.Format("type", "package", "id", "module:Other", "status", "notinstalled")), null, DateTime.Now);
            store.Apply("PC02", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Product", "status", "uptodate", "installed", "1.0"),
                WorkerProtocol.Format("type", "package", "id", "module:Example", "status", "notinstalled")), null, DateTime.Now);
            store.Apply("PC03", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Product", "status", "notinstalled")), null, DateTime.Now);
            store.GetOrAdd("PC03").Paused = true;
            List<UpdatePackage> packages = new List<UpdatePackage>
            {
                UpdatePackage.Parse("Example.Product | example | Example product"),
                UpdatePackage.Parse("module:Example"),
                UpdatePackage.Parse("module:Other"),
                UpdatePackage.Parse("Never.Checked")
            };

            List<KeyValuePair<UpdatePackage, List<string>>> c = UpdateRunner.InstallCandidates(
                new[] { "PC01", "PC02", "PC03", "pc01", "PC04" }, packages, store.Find, new[] { "example" });

            CollectionAssert.AreEqual(new[] { "Example.Product", "module:Example" }, c.Select(x => x.Key.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "PC01" }, c[0].Value, "PC02 has it, PC03 is paused, PC04 never checked");
            CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, c[1].Value);
            Assert.AreEqual(0, UpdateRunner.InstallCandidates(new[] { "PC02" }, packages.Take(1), store.Find, null).Count);
        }

        [TestMethod]
        public void PackageChoices_UpdateInstallOrState()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Editor", "status", "outdated", "installed", "1.0", "available", "2.0"),
                WorkerProtocol.Format("type", "package", "id", "Example.Viewer", "status", "notinstalled"),
                WorkerProtocol.Format("type", "package", "id", "Example.Player", "status", "uptodate", "installed", "3.0")), null, DateTime.Now);
            store.Apply("PC02", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Editor", "status", "uptodate", "installed", "2.0"),
                WorkerProtocol.Format("type", "package", "id", "Example.Viewer", "status", "outdated", "installed", "1.0", "available", "1.1"),
                WorkerProtocol.Format("type", "package", "id", "Example.Player", "status", "uptodate", "installed", "3.0")), null, DateTime.Now);
            store.Apply("PC03", Messages(
                WorkerProtocol.Format("type", "package", "id", "Example.Editor", "status", "outdated", "installed", "1.0", "available", "2.0")), null, DateTime.Now);
            store.GetOrAdd("PC03").Paused = true;
            List<UpdatePackage> packages = new[] { "Example.Editor", "Example.Viewer", "Example.Player", "Example.New" }.Select(UpdatePackage.Parse).ToList();

            List<PackageChoice> c = UpdateRunner.PackageChoices(new[] { "PC01", "PC02", "PC03", "PC04" }, packages, store.Find, null);
            Assert.AreEqual(4, c.Count, "every software of the profile, in its order");
            CollectionAssert.AreEqual(new[] { "PC01" }, c[0].Update, "outdated; PC03 is paused");
            Assert.AreEqual(1, c[0].UpToDate);
            CollectionAssert.AreEqual(new[] { "PC02" }, c[1].Update);
            CollectionAssert.AreEqual(new[] { "PC01" }, c[1].Install);
            Assert.AreEqual(0, c[2].Update.Count + c[2].Install.Count);
            Assert.AreEqual(2, c[2].UpToDate);
            Assert.AreEqual(1, c[2].NotChecked, "PC04 never checked");
            Assert.AreEqual(3, c[3].NotChecked);
        }

        [TestMethod]
        public void Install_WorkerArguments()
        {
            string command; List<UpdatePackage> packages; int timeout; bool unsigned;
            Assert.IsNull(Worker.Program.ParseArguments(new[] { "INSTALL", "--package", "Example.Product|example|Example product", "--timeout", "15" },
                out command, out packages, out timeout, out unsigned));
            Assert.AreEqual(InstallPolicy.Command, command);
            CollectionAssert.AreEqual(new[] { "Example.Product" }, packages.Select(p => p.Id).ToArray());

            Assert.IsNull(Worker.Program.ParseArguments(new[] { "install", "--package", "module:Example||", "--allow-unsigned-modules" },
                out command, out packages, out timeout, out unsigned));
            Assert.IsTrue(packages[0].IsModule);
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "install", "--package", "bad id" }, out command, out packages, out timeout, out unsigned));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "install", "--all" }, out command, out packages, out timeout, out unsigned));

            // LiteDeploy passes only the package chosen
            string args = UpdateRunner.BuildWorkerArguments(InstallPolicy.Command, new List<UpdatePackage> { UpdatePackage.Parse("Example.Product | example | Example product") });
            Assert.AreEqual("install --package \"Example.Product|example|Example product\" --timeout 15", args);
        }

        [TestMethod]
        public void Install_WingetArguments()
        {
            Assert.AreEqual("install --id Example.Product -e --source winget --scope machine --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                WingetClient.InstallArguments("Example.Product"));
            foreach (string bad in new[] { null, "", "Bad Id", "A.B\" & calc", "module:Example" })
            {
                try { WingetClient.InstallArguments(bad); Assert.Fail("accepted: " + bad); }
                catch (ArgumentException) { }
            }
        }

        [TestMethod]
        public void Install_WingetOutcome_SameRuleAsUpgrade()
        {
            Assert.AreEqual(WorkerProtocol.ResultInstalled, WingetClient.Outcome(0, WorkerProtocol.ResultInstalled));
            Assert.AreEqual(WorkerProtocol.ResultUpgraded, WingetClient.Outcome(0, WorkerProtocol.ResultUpgraded));
            Assert.AreEqual(WorkerProtocol.ResultSkipped, WingetClient.Outcome(WingetClient.NoApplicableUpgrade, WorkerProtocol.ResultInstalled));
            Assert.AreEqual(WorkerProtocol.ResultFailed, WingetClient.Outcome(unchecked((int)0x80190193), WorkerProtocol.ResultInstalled));
            Assert.AreEqual(WorkerProtocol.ResultFailed, WingetClient.Outcome(-1, WorkerProtocol.ResultInstalled));
        }

        [TestMethod]
        public void Install_ModuleActions_AndSchedulesNeverInstall()
        {
            Assert.IsTrue(ModuleRunner.IsValidAction("Check"));
            Assert.IsTrue(ModuleRunner.IsValidAction("Update"));
            Assert.IsTrue(ModuleRunner.IsValidAction("Install"));
            Assert.IsFalse(ModuleRunner.IsValidAction("install; calc"));
            Assert.IsFalse(ModuleRunner.IsValidAction(null));

            Assert.IsTrue(ScheduleRunner.IsScheduleCommand("scan"));
            Assert.IsTrue(ScheduleRunner.IsScheduleCommand("upgrade"));
            Assert.IsFalse(ScheduleRunner.IsScheduleCommand(InstallPolicy.Command));
            Assert.IsFalse(ScheduleRunner.IsScheduleCommand(null));
        }

        [TestMethod]
        public void Install_State_RecordsTheInstallResult()
        {
            UpdateStateStore store = new UpdateStateStore();
            DateTime t1 = new DateTime(2026, 11, 17, 9, 0, 0);
            store.Apply("PC01", Messages(
                WorkerProtocol.Format("type", "package", "id", "A.B", "status", "notinstalled"),
                WorkerProtocol.Format("type", "upgrade", "id", "A.B", "result", "installed", "code", "0x00000000", "message", "OK"),
                WorkerProtocol.Format("type", "package", "id", "A.B", "status", "uptodate", "installed", "1.0")), null, t1);
            PackageUpdateState p = store.Find("PC01").Find("A.B");
            Assert.AreEqual("uptodate", p.Status);
            Assert.AreEqual(WorkerProtocol.ResultInstalled, p.LastResult);
            Assert.AreEqual(t1, p.LastUpgrade);
            Assert.IsNull(store.Find("PC01").LastError);
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
            UserSettings s = new UserSettings { Theme = Theme.Dark, AllowUnsignedModules = true, UpdateCacheShare = @"\\server\share\cache", ExtraLogFolders = @"tool|Temp\Tool|10" };
            UserSettings c = s.Clone();
            c.Language = Localization.French;
            Assert.AreEqual(Theme.Dark, c.Theme);
            Assert.IsTrue(c.AllowUnsignedModules);
            Assert.AreEqual(@"\\server\share\cache", c.UpdateCacheShare);
            Assert.AreEqual(@"tool|Temp\Tool|10", c.ExtraLogFolders);
            Assert.AreNotEqual(c.Language, s.Language, "the copy is independent");
        }

        [TestMethod]
        public void Cache_PassedToModulesOnly_AndValidated()
        {
            List<UpdatePackage> modules = new List<UpdatePackage> { UpdatePackage.Parse("module:AdobeReader") };
            string args = UpdateRunner.BuildWorkerArguments("upgrade", modules, false, @"\\server\share\cache\");
            StringAssert.Contains(args, @"--cache ""\\server\share\cache""");
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("upgrade", new List<UpdatePackage> { UpdatePackage.Parse("A.B") }, false, @"\\server\share").Contains("--cache"));

            Assert.IsTrue(Worker.Program.IsValidCachePath(@"\\server\share\cache"));
            Assert.IsFalse(Worker.Program.IsValidCachePath(@"C:\cache"));
            Assert.IsFalse(Worker.Program.IsValidCachePath(@"\\server\share\..\x"));
            Assert.IsFalse(Worker.Program.IsValidCachePath(@"\\server\share\a"" & calc"));
        }

        // ---- Extra log folders ------------------------------------------------------

        [TestMethod]
        public void ExtraLogFolders_EmptyByDefault()
        {
            Assert.AreEqual("", new UserSettings().ExtraLogFolders);
            Assert.AreEqual(0, RemoteLogs.ParseExtraFolders(null).Count);
            Assert.AreEqual(0, RemoteLogs.ParseExtraFolders("").Count);
            Assert.AreEqual(0, RemoteLogs.ParseExtraFolders("  ; ").Count);
        }

        [TestMethod]
        public void ExtraLogFolders_ParsesEntries()
        {
            List<Tuple<string, string, int>> folders = RemoteLogs.ParseExtraFolders(@" tool | Temp\Tool\ | 10 ;other|Logs\Other|3");
            Assert.AreEqual(2, folders.Count);
            Assert.AreEqual(Tuple.Create("tool", @"Temp\Tool", 10), folders[0]);
            Assert.AreEqual(Tuple.Create("other", @"Logs\Other", 3), folders[1]);
        }

        [TestMethod]
        public void ExtraLogFolders_NumberIsOptional()
        {
            Assert.AreEqual(RemoteLogs.DefaultMaxFiles, RemoteLogs.ParseExtraFolders(@"tool|Temp\Tool")[0].Item3);
            Assert.AreEqual(RemoteLogs.DefaultMaxFiles, RemoteLogs.ParseExtraFolders(@"tool|Temp\Tool|")[0].Item3);
        }

        [TestMethod]
        public void ExtraLogFolders_SkipsInvalidEntries()
        {
            string[] invalid =
            {
                @"Temp\Tool",                   // no label
                @"|Temp\Tool|5",                // empty label
                @"a\b|Temp\Tool|5",             // label is not a plain folder name
                @"..|Temp\Tool|5",
                @"tool||5",                     // empty folder
                @"tool|C:\Temp|5",              // drive
                @"tool|\Temp\Tool|5",           // rooted
                @"tool|..\Temp|5",              // leaves %windir%
                @"tool|Temp\..\..\x|5",
                @"tool|Temp\Tool|0",            // number out of range
                @"tool|Temp\Tool|101",
                @"tool|Temp\Tool|ten",
                @"tool|Temp\Tool|5|extra",      // too many fields
            };
            foreach (string entry in invalid)
                Assert.AreEqual(0, RemoteLogs.ParseExtraFolders(entry).Count, entry);

            List<Tuple<string, string, int>> kept = RemoteLogs.ParseExtraFolders(@"tool|..\x|5;good|Temp\Good|5");
            Assert.AreEqual(1, kept.Count, "a bad entry does not drop the others");
            Assert.AreEqual("good", kept[0].Item1);
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
