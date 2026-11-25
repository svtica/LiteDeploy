// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteToolsTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Remote tools of the computer actions: TeamViewer ID read by the
//          worker (registry value to text), quick commands (fixed table,
//          worker arguments, output), command prompt (PsExec and WinRM
//          arguments, password never in clear text), Royal TS export (CSV
//          quoting, RoyalJSON), targets of an action (checked, else
//          selected) and the TeamViewer ID kept in updates.xml.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using LiteDeploy.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class RemoteToolsTests
    {
        // ---- TeamViewer ID ---------------------------------------------------------------

        [TestMethod]
        public void TeamViewerId_FromDword()
        {
            Assert.AreEqual("123456789", ComputerInfo.FormatTeamViewerId(123456789));
            // A DWORD above Int32.MaxValue is read as a negative Int32: taken as unsigned
            Assert.AreEqual("3000000000", ComputerInfo.FormatTeamViewerId(unchecked((int)3000000000u)));
            Assert.AreEqual("1234567890", ComputerInfo.FormatTeamViewerId(1234567890L));
        }

        [TestMethod]
        public void TeamViewerId_FromStringAndInvalidValues()
        {
            Assert.AreEqual("987654321", ComputerInfo.FormatTeamViewerId("987 654 321"));
            Assert.IsNull(ComputerInfo.FormatTeamViewerId(0));
            Assert.IsNull(ComputerInfo.FormatTeamViewerId(null));
            Assert.IsNull(ComputerInfo.FormatTeamViewerId(""));
            Assert.IsNull(ComputerInfo.FormatTeamViewerId("abc"));
            Assert.IsNull(ComputerInfo.FormatTeamViewerId("-5"));
            Assert.IsNull(ComputerInfo.FormatTeamViewerId(new byte[] { 1, 2 }));
        }

        static Func<string, string, object> Registry(Dictionary<string, object> values)
        {
            return (key, name) => { object v; return values.TryGetValue(key + "|" + name, out v) ? v : null; };
        }

        [TestMethod]
        public void TeamViewerId_KeysInOrder()
        {
            Func<string, IEnumerable<string>> none = k => new string[0];
            // Not installed
            Assert.IsNull(ComputerInfo.FindTeamViewerId(Registry(new Dictionary<string, object>()), none));
            // 32-bit installation only
            Assert.AreEqual("111222333", ComputerInfo.FindTeamViewerId(Registry(new Dictionary<string, object>
            {
                { @"SOFTWARE\WOW6432Node\TeamViewer|ClientID", 111222333 }
            }), none));
            // The 64-bit key first; ClientIDOfTSUser when there is no ClientID
            Assert.AreEqual("444555666", ComputerInfo.FindTeamViewerId(Registry(new Dictionary<string, object>
            {
                { @"SOFTWARE\TeamViewer|ClientIDOfTSUser", 444555666 },
                { @"SOFTWARE\WOW6432Node\TeamViewer|ClientID", 111222333 }
            }), none));
            Assert.AreEqual("777888999", ComputerInfo.FindTeamViewerId(Registry(new Dictionary<string, object>
            {
                { @"SOFTWARE\TeamViewer|ClientID", 777888999 },
                { @"SOFTWARE\TeamViewer|ClientIDOfTSUser", 444555666 }
            }), none));
        }

        [TestMethod]
        public void TeamViewerId_OlderVersionSubkeys_NewestFirst()
        {
            Func<string, IEnumerable<string>> subKeys = k => k == @"SOFTWARE\TeamViewer" ? new[] { "Version9", "Version12", "Other" } : new string[0];
            Assert.AreEqual("120000000", ComputerInfo.FindTeamViewerId(Registry(new Dictionary<string, object>
            {
                { @"SOFTWARE\TeamViewer\Version9|ClientID", 90000000 },
                { @"SOFTWARE\TeamViewer\Version12|ClientID", 120000000 },
                { @"SOFTWARE\TeamViewer\Other|ClientID", 5 }
            }), subKeys));
        }

        // ---- updates.xml ---------------------------------------------------------------------

        static WorkerMessage Info(params string[] keyValues)
        {
            return WorkerProtocol.Parse(WorkerProtocol.Format(new[] { "type", "info" }.Concat(keyValues).ToArray()));
        }

        [TestMethod]
        public void State_TeamViewerId_FromInfo()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", new List<WorkerMessage> { Info("teamviewer", "123456789") }, null, DateTime.Now);
            Assert.AreEqual("123456789", store.Find("PC01").TeamViewerId);
            // An older worker says nothing: kept
            store.Apply("PC01", new List<WorkerMessage> { Info("os", "Windows 11") }, null, DateTime.Now);
            Assert.AreEqual("123456789", store.Find("PC01").TeamViewerId);
            // Empty: TeamViewer removed
            store.Apply("PC01", new List<WorkerMessage> { Info("teamviewer", "") }, null, DateTime.Now);
            Assert.IsNull(store.Find("PC01").TeamViewerId);
        }

        [TestMethod]
        public void State_TeamViewerId_RoundTripAndBackwardCompatible()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.GetOrAdd("PC01").TeamViewerId = "123456789";
            store.GetOrAdd("PC02");
            String xml = store.Serialize();
            StringAssert.Contains(xml, "TeamViewerId=\"123456789\"");
            UpdateStateStore read = (UpdateStateStore)new XmlSerializer(typeof(UpdateStateStore)).Deserialize(new StringReader(xml));
            Assert.AreEqual("123456789", read.Find("PC01").TeamViewerId);
            Assert.IsNull(read.Find("PC02").TeamViewerId);
            Assert.AreEqual(xml, read.Serialize());

            // A file written before the attribute existed
            UpdateStateStore old = (UpdateStateStore)new XmlSerializer(typeof(UpdateStateStore)).Deserialize(new StringReader(
                "<LiteDeployUpdates><Computer Name=\"PC03\" Paused=\"true\" /></LiteDeployUpdates>"));
            Assert.IsTrue(old.Find("PC03").Paused);
            Assert.IsNull(old.Find("PC03").TeamViewerId);
        }

        [TestMethod]
        public void Row_CarriesTheTeamViewerId()
        {
            ComputerRow row = ComputerRow.Build("PC01", null, new ComputerUpdateState { Name = "PC01", TeamViewerId = "123456789" }, null, null);
            Assert.AreEqual("123456789", row.TeamViewerId);
        }

        [TestMethod]
        public void TeamViewer_ConnectArguments()
        {
            Assert.AreEqual("-i 123456789", RemoteActions.TeamViewerArguments("123 456 789"));
            Assert.AreEqual("-i 42", RemoteActions.TeamViewerArguments("42\" --other"));
            try { RemoteActions.TeamViewerArguments(""); Assert.Fail("an empty ID must be refused"); }
            catch (ArgumentException) { }
        }

        // ---- Quick commands --------------------------------------------------------------------

        [TestMethod]
        public void QuickCommands_TableIsFixedAndValid()
        {
            Assert.AreEqual(QuickCommands.All.Count, QuickCommands.All.Select(c => c.Id.ToLowerInvariant()).Distinct().Count(), "identifiers are unique");
            foreach (QuickCommand c in QuickCommands.All)
            {
                Assert.IsTrue(c.Steps.Count > 0, c.Id);
                Assert.IsFalse(String.IsNullOrWhiteSpace(ComputerActions.QuickCommandText(c)), c.Id);
                Assert.AreNotEqual(c.Label, null, c.Id);
                foreach (QuickCommandStep s in c.Steps)
                {
                    // A program of System32, by its file name only
                    Assert.IsTrue(s.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), c.Id);
                    Assert.AreEqual(Path.GetFileName(s.FileName), s.FileName, c.Id);
                }
            }
            Assert.IsTrue(QuickCommands.Find(QuickCommands.Restart).Confirm);
            Assert.IsTrue(QuickCommands.Find(QuickCommands.RestartSpooler).Confirm);
            Assert.IsFalse(QuickCommands.Find(QuickCommands.FlushDns).Confirm);
            Assert.IsTrue(QuickCommands.Find(QuickCommands.StartTeamViewer).Confirm, "it opens remote access to the computer");
            Assert.IsTrue(QuickCommands.Find(QuickCommands.EnableRemoteDesktop).Confirm, "it opens remote access to the computer");
            Assert.IsTrue(QuickCommands.Find(QuickCommands.DisableRemoteDesktop).Confirm, "it can cut an open session");
            Assert.IsFalse(QuickCommands.Find(QuickCommands.DisableSleep).Confirm);
        }

        [TestMethod]
        public void QuickCommands_Arguments()
        {
            Assert.AreEqual("quick gpupdate", QuickCommands.WorkerArguments("gpupdate"));
            Assert.AreEqual("quick gpupdate-computer", QuickCommands.WorkerArguments("GPUPDATE-Computer"));
            Assert.AreEqual("quick flushdns", QuickCommands.WorkerArguments(QuickCommands.FlushDns));
            Assert.AreEqual("gpupdate.exe /force", QuickCommands.Find(QuickCommands.GroupPolicy).CommandText);
            Assert.AreEqual("gpupdate.exe /target:computer /force", QuickCommands.Find(QuickCommands.GroupPolicyComputer).CommandText);
            Assert.AreEqual("ipconfig.exe /flushdns", QuickCommands.Find(QuickCommands.FlushDns).CommandText);
            Assert.AreEqual("net.exe stop spooler /y & net.exe start spooler", QuickCommands.Find(QuickCommands.RestartSpooler).CommandText);
            StringAssert.StartsWith(QuickCommands.Find(QuickCommands.Restart).Steps[0].Arguments, "/r /t 300 ");
            Assert.AreEqual("shutdown.exe /a", QuickCommands.Find(QuickCommands.CancelRestart).CommandText);
            Assert.AreEqual("quick start-teamviewer", QuickCommands.WorkerArguments(QuickCommands.StartTeamViewer));
            Assert.AreEqual("sc.exe config TeamViewer start= auto & sc.exe start TeamViewer", QuickCommands.Find(QuickCommands.StartTeamViewer).CommandText);
            Assert.AreEqual(@"reg.exe add ""HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server"" /v fDenyTSConnections /t REG_DWORD /d 0 /f"
                + " & netsh.exe advfirewall firewall set rule group=\"@FirewallAPI.dll,-28752\" new enable=Yes",
                QuickCommands.Find(QuickCommands.EnableRemoteDesktop).CommandText);
            StringAssert.Contains(QuickCommands.Find(QuickCommands.DisableRemoteDesktop).Steps[0].Arguments, "/d 1 /f");
            Assert.AreEqual("powercfg.exe /change standby-timeout-ac 0 & powercfg.exe /change hibernate-timeout-ac 0 & powercfg.exe /hibernate off",
                QuickCommands.Find(QuickCommands.DisableSleep).CommandText);
        }

        [TestMethod]
        public void QuickCommands_AcceptedExitCodes()
        {
            QuickCommand tv = QuickCommands.Find(QuickCommands.StartTeamViewer);
            // Already running: success; service missing (1060) or disabled (1058): failure
            Assert.IsTrue(tv.Steps[1].IsSuccess(0));
            Assert.IsTrue(tv.Steps[1].IsSuccess(1056));
            Assert.IsFalse(tv.Steps[1].IsSuccess(1058));
            Assert.IsFalse(tv.Steps[0].IsSuccess(1060), "configuring a missing service stops the command");
            Assert.IsFalse(QuickCommands.Find(QuickCommands.FlushDns).Steps[0].IsSuccess(1));
        }

        [TestMethod]
        public void QuickCommands_NoFreeText()
        {
            Assert.IsNull(QuickCommands.Find("cmd /c del"));
            Assert.IsNull(QuickCommands.Find(null));
            try { QuickCommands.WorkerArguments("gpupdate & calc"); Assert.Fail("unknown identifiers must be refused"); }
            catch (ArgumentException) { }
        }

        [TestMethod]
        public void QuickCommands_Output()
        {
            List<WorkerMessage> messages = WorkerProtocol.ParseAll(String.Join("\n",
                WorkerProtocol.Format("type", QuickCommands.TypeOutput, "text", "> ipconfig.exe /flushdns"),
                WorkerProtocol.Format("type", QuickCommands.TypeOutput, "text", "Successfully flushed the DNS Resolver Cache."),
                WorkerProtocol.Format("type", QuickCommands.TypeResult, "id", "flushdns", "code", "0")));
            String text = ComputerActions.FormatQuickOutput("PC01", messages, null);
            StringAssert.StartsWith(text, "== PC01 ==\n> ipconfig.exe /flushdns\nSuccessfully flushed");
            StringAssert.EndsWith(text, "Done.");

            messages = WorkerProtocol.ParseAll(WorkerProtocol.Format("type", QuickCommands.TypeResult, "id", "restart-spooler", "code", "2"));
            StringAssert.EndsWith(ComputerActions.FormatQuickOutput("PC01", messages, null), "Exit code 2.");
            StringAssert.Contains(ComputerActions.FormatQuickOutput("PC02", new List<WorkerMessage>(), "WinRM: access denied"), "WinRM: access denied");
        }

        // ---- Command prompt --------------------------------------------------------------------

        [TestMethod]
        public void CommandPrompt_PsExec_AsSystem()
        {
            String args = PsExecCommandBuilder.BuildArguments(RemoteConsole.PsExecOptions("PC01", null, null));
            StringAssert.StartsWith(args, "-accepteula -nobanner ");
            StringAssert.Contains(args, "PC01 -h");
            StringAssert.Contains(args, " -n 20");
            int system = args.IndexOf(" -s", StringComparison.Ordinal);
            Assert.IsTrue(system > 0 && args.IndexOf("cmd.exe", StringComparison.Ordinal) > system, args);
            Assert.IsFalse(args.Contains(" -u ") || args.Contains(" -p "), args);
            Assert.IsFalse(args.Contains(" -c") || args.Contains(" -d"), "no copy, waits for the console: " + args);
        }

        [TestMethod]
        public void CommandPrompt_PsExec_CredentialMaskedForDisplay()
        {
            PsExecOptions o = RemoteConsole.PsExecOptions("PC01", @"EXAMPLE\admin1", "Secret-1");
            String display = PsExecCommandBuilder.DisplayArguments(o);
            Assert.IsFalse(display.Contains("Secret-1"), display);
            StringAssert.Contains(display, PsExecCommandBuilder.MaskedPassword);
            String args = PsExecCommandBuilder.BuildArguments(o);
            StringAssert.Contains(args, " -u ");
            StringAssert.Contains(args, " -s");
        }

        [TestMethod]
        public void CommandPrompt_WinRm_Command()
        {
            String command = RemoteConsole.PowerShellCommand("PC01", null, null);
            StringAssert.Contains(command, "Enter-PSSession -ComputerName 'PC01'");
            Assert.IsFalse(command.Contains("-Credential"));
            // Values stay data
            StringAssert.Contains(RemoteConsole.PowerShellCommand("PC0'1", null, null), "-ComputerName 'PC0''1'");

            String withCredential = RemoteConsole.PowerShellCommand("PC01", @"EXAMPLE\admin1", "01000000d08c9ddf");
            StringAssert.Contains(withCredential, "ConvertTo-SecureString '01000000d08c9ddf'");
            StringAssert.Contains(withCredential, @"PSCredential('EXAMPLE\admin1'");
            StringAssert.Contains(withCredential, "-Credential $c");

            String arguments = RemoteConsole.PowerShellArguments(withCredential);
            StringAssert.Contains(arguments, "-NoExit");
            String encoded = arguments.Substring(arguments.IndexOf("-EncodedCommand ", StringComparison.Ordinal) + "-EncodedCommand ".Length);
            Assert.AreEqual(withCredential, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
        }

        [TestMethod]
        public void CommandPrompt_WinRm_PasswordProtected()
        {
            String hex = RemoteConsole.ProtectForPowerShell("Secret-1");
            Assert.IsFalse(hex.Contains("Secret"));
            Assert.IsTrue(hex.All(Uri.IsHexDigit), hex);
            byte[] blob = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
            byte[] clear = System.Security.Cryptography.ProtectedData.Unprotect(blob, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            Assert.AreEqual("Secret-1", Encoding.Unicode.GetString(clear));
        }

        // ---- Royal TS ---------------------------------------------------------------------------

        [TestMethod]
        public void RoyalTs_CsvQuoting()
        {
            Assert.AreEqual("PC01", RoyalTsExport.CsvField("PC01"));
            Assert.AreEqual("", RoyalTsExport.CsvField(null));
            Assert.AreEqual("\"a,b\"", RoyalTsExport.CsvField("a,b"));
            Assert.AreEqual("\"a;b\"", RoyalTsExport.CsvField("a;b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", RoyalTsExport.CsvField("say \"hi\""));
            Assert.AreEqual("\"two\nlines\"", RoyalTsExport.CsvField("two\nlines"));
            Assert.AreEqual("\" padded\"", RoyalTsExport.CsvField(" padded"));
        }

        [TestMethod]
        public void RoyalTs_Csv()
        {
            String csv = RoyalTsExport.ToCsv(new[]
            {
                new RoyalTsConnection { Name = "PC01", ComputerName = "PC01", Description = "Windows 11 Pro, 24H2", Path = "Workstations/Laptops" },
                new RoyalTsConnection { Name = "SRV01", ComputerName = "SRV01", Description = "", Path = "" }
            });
            Assert.AreEqual("Name,ComputerName,Description,Path\r\n" +
                            "PC01,PC01,\"Windows 11 Pro, 24H2\",Workstations/Laptops\r\n" +
                            "SRV01,SRV01,,\r\n", csv);
        }

        [TestMethod]
        public void RoyalTs_FromRow_OsAndOu()
        {
            ComputerRow row = new ComputerRow { Name = "PC01", Os = "Windows 11 Pro", OsVersion = "10.0 (26100)", Ou = "OU=Laptops,OU=Workstations,DC=example,DC=com" };
            RoyalTsConnection c = RoyalTsExport.FromRow("PC01", row);
            Assert.AreEqual("PC01", c.Name);
            Assert.AreEqual("PC01", c.ComputerName);
            Assert.AreEqual("Windows 11 Pro (10.0 (26100))", c.Description);
            Assert.AreEqual("Workstations/Laptops", c.Path);
            RoyalTsConnection unknown = RoyalTsExport.FromRow("PC04", null);
            Assert.AreEqual("", unknown.Description);
            Assert.AreEqual("", unknown.Path);
        }

        [TestMethod]
        public void RoyalTs_Json()
        {
            String bs = new String((char)0x5C, 1);
            Assert.AreEqual("\"a" + bs + "\"b" + bs + bs + "c" + bs + "n" + bs + "u0001\"", RoyalTsExport.JsonString("a\"b" + bs + "c\n\u0001"));
            String json = RoyalTsExport.ToRoyalJson("Export", new[]
            {
                new RoyalTsConnection { Name = "PC01", ComputerName = "PC01", Description = "Windows 11", Path = "Workstations/Laptops" },
                new RoyalTsConnection { Name = "PC02", ComputerName = "PC02", Description = "", Path = "" }
            });
            StringAssert.Contains(json, "\"Name\": \"Export\"");
            StringAssert.Contains(json, "{ \"Type\": \"RemoteDesktopConnection\", \"Name\": \"PC01\", \"ComputerName\": \"PC01\", \"Description\": \"Windows 11\", \"Path\": \"Workstations/Laptops\" },");
            StringAssert.Contains(json, "{ \"Type\": \"RemoteDesktopConnection\", \"Name\": \"PC02\", \"ComputerName\": \"PC02\" }");
            Assert.AreEqual("{\r\n  \"Name\": \"Empty\",\r\n  \"Objects\": []\r\n}\r\n", RoyalTsExport.ToRoyalJson("Empty", new RoyalTsConnection[0]));
        }

        [TestMethod]
        public void RoyalTs_Save_EncodingByFormat()
        {
            String dir = Path.Combine(Path.GetTempPath(), "LiteDeployTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                RoyalTsConnection[] list = { new RoyalTsConnection { Name = "PÉ01", ComputerName = "PÉ01", Description = "", Path = "" } };
                String csv = Path.Combine(dir, "a.csv"), json = Path.Combine(dir, "a.rjson");
                RoyalTsExport.Save(csv, list);
                RoyalTsExport.Save(json, list);
                byte[] csvBytes = File.ReadAllBytes(csv), jsonBytes = File.ReadAllBytes(json);
                Assert.IsTrue(csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF, "CSV: UTF-8 with BOM");
                Assert.AreEqual((byte)'{', jsonBytes[0], "RoyalJSON: no BOM");
                StringAssert.Contains(File.ReadAllText(csv, Encoding.UTF8), "PÉ01,PÉ01");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // ---- Targets ------------------------------------------------------------------------

        [TestMethod]
        public void Targets_CheckedElseSelected()
        {
            CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, ComputerActions.Targets(new[] { "PC02", "PC01", "pc01", " " }, new[] { "PC04" }));
            CollectionAssert.AreEqual(new[] { "PC04" }, ComputerActions.Targets(new String[0], new[] { "PC04" }));
            CollectionAssert.AreEqual(new[] { "PC04" }, ComputerActions.Targets(null, new[] { " PC04 ", "" }));
            Assert.AreEqual(0, ComputerActions.Targets(null, null).Count);
        }

        [TestMethod]
        public void Windows_ConfirmationAboveFive()
        {
            Assert.IsFalse(ComputerActions.NeedsWindowConfirmation(1));
            Assert.IsFalse(ComputerActions.NeedsWindowConfirmation(ComputerActions.MaxWindowsWithoutQuestion));
            Assert.IsTrue(ComputerActions.NeedsWindowConfirmation(ComputerActions.MaxWindowsWithoutQuestion + 1));
        }

        // ---- Progress window ----------------------------------------------------------------

        [TestMethod]
        public void RemoteRun_ElapsedAndStatus()
        {
            Localization.Initialize(Localization.English);
            Assert.AreEqual("0:05", RemoteRunProgress.Elapsed(TimeSpan.FromSeconds(5)));
            Assert.AreEqual("2:03", RemoteRunProgress.Elapsed(TimeSpan.FromSeconds(123)));
            Assert.AreEqual("1:00:01", RemoteRunProgress.Elapsed(TimeSpan.FromSeconds(3601)));
            Assert.AreEqual("0:00", RemoteRunProgress.Elapsed(TimeSpan.FromSeconds(-3)));
            Assert.AreEqual("3 of 10 computer(s) done, 4 under way (0:42)...", RemoteRunProgress.StatusText(3, 4, 10, TimeSpan.FromSeconds(42), false, false));
            StringAssert.StartsWith(RemoteRunProgress.StatusText(3, 2, 10, TimeSpan.Zero, true, false), "Cancelling: waiting for 2 ");
            Assert.AreEqual("Cancelled: 3 of 10 computer(s) done (0:42).", RemoteRunProgress.StatusText(3, 0, 10, TimeSpan.FromSeconds(42), true, true));
            Assert.AreEqual("Done: 10 computer(s) (0:42).", RemoteRunProgress.StatusText(10, 0, 10, TimeSpan.FromSeconds(42), false, true));
        }

        [TestMethod]
        public void RemoteRun_CancelledComputersAreNamed()
        {
            Localization.Initialize(Localization.English);
            String[] all = RemoteRunProgress.Complete(new[] { "PC01", "PC02", "PC03" }, new[] { "== PC01 ==\nDone.", null, null });
            Assert.AreEqual(3, all.Length);
            Assert.AreEqual("== PC01 ==\nDone.", all[0]);
            Assert.AreEqual("== PC02 ==\nNot run (cancelled).", all[1]);
            StringAssert.StartsWith(all[2], "== PC03 ==");
        }
    }
}
