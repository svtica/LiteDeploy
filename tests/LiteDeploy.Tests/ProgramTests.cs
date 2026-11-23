// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProgramTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Unit tests of "Uninstall another program": how an entry of the
//          Uninstall keys is classified (MSI, quiet EXE, not removable,
//          hidden), the command line split, the allowed folders of an
//          uninstaller, the msiexec arguments, the exit codes, the worker
//          arguments and the merged list of several computers. Fictitious
//          values only.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class ProgramTests
    {
        static readonly string[] Roots = { @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows" };
        const string Code = "{00000000-0000-0000-0000-000000000001}";

        static ProgramKind Kind(ProgramEntry e)
        {
            string reason;
            return ProgramPolicy.Classify(e, Roots, out reason);
        }

        [TestMethod]
        public void Classify_MsiQuietExeAndRefusals()
        {
            Assert.AreEqual(ProgramKind.Msi, Kind(new ProgramEntry { Key = Code, Name = "Example App", WindowsInstaller = true }));
            Assert.AreEqual(ProgramKind.QuietExe, Kind(new ProgramEntry { Key = "ExampleApp", Name = "Example App",
                QuietUninstallString = @"""C:\Program Files\Example\uninst.exe"" /S" }));
            string reason;
            Assert.AreEqual(ProgramKind.NotRemovable, ProgramPolicy.Classify(new ProgramEntry { Key = "ExampleApp", Name = "Example App",
                QuietUninstallString = @"""C:\Users\Public\uninst.exe"" /S" }, Roots, out reason));
            Assert.AreEqual("location", reason);
            Assert.AreEqual(ProgramKind.NotRemovable, ProgramPolicy.Classify(new ProgramEntry { Key = "ExampleApp", Name = "Example App" }, Roots, out reason));
            Assert.AreEqual("noquiet", reason, "an EXE without quiet command");
            Assert.AreEqual(ProgramKind.NotRemovable, ProgramPolicy.Classify(new ProgramEntry { Key = Code, Name = "Example App", WindowsInstaller = true, NoRemove = true }, Roots, out reason));
            Assert.AreEqual("noremove", reason);
            Assert.AreEqual(ProgramKind.NotRemovable, ProgramPolicy.Classify(new ProgramEntry { Key = Code, Name = "Sophos Endpoint Agent", WindowsInstaller = true }, Roots, out reason));
            Assert.AreEqual("protected", reason);
            Assert.AreEqual(ProgramKind.NotRemovable, Kind(new ProgramEntry { Key = Code, Name = "Microsoft Visual C++ 2022 X64 Minimum Runtime", WindowsInstaller = true }));
            Assert.AreEqual(ProgramKind.NotRemovable, Kind(new ProgramEntry { Key = "ExampleApp", Name = "Example App", WindowsInstaller = true }), "MSI flag without a product code key");
        }

        [TestMethod]
        public void Classify_Hidden()
        {
            Assert.AreEqual(ProgramKind.Hidden, Kind(new ProgramEntry { Key = Code, Name = "", WindowsInstaller = true }));
            Assert.AreEqual(ProgramKind.Hidden, Kind(new ProgramEntry { Key = Code, Name = "Example App", WindowsInstaller = true, SystemComponent = true }));
            Assert.AreEqual(ProgramKind.Hidden, Kind(new ProgramEntry { Key = "KB0000001", Name = "Update for Example App", IsUpdate = true }));
            Assert.AreEqual(ProgramKind.Hidden, Kind(null));
        }

        [TestMethod]
        public void SplitCommand_QuotedAndUnquoted()
        {
            CollectionAssert.AreEqual(new[] { @"C:\Program Files\Example\uninst.exe", "/S" }, ProgramPolicy.SplitCommand(@"""C:\Program Files\Example\uninst.exe"" /S"));
            CollectionAssert.AreEqual(new[] { @"C:\Program Files\Example App\unins000.exe", "/VERYSILENT /NORESTART" },
                ProgramPolicy.SplitCommand(@"C:\Program Files\Example App\unins000.exe /VERYSILENT /NORESTART"));
            CollectionAssert.AreEqual(new[] { @"C:\Windows\example.exe", "" }, ProgramPolicy.SplitCommand(@"C:\Windows\example.exe"));
            Assert.IsNull(ProgramPolicy.SplitCommand(@"""C:\Program Files\Example\uninst.cmd"" /S"), "not an exe");
            Assert.IsNull(ProgramPolicy.SplitCommand("MsiExec.bat /X"));
            Assert.IsNull(ProgramPolicy.SplitCommand(@"""C:\unterminated.exe /S"));
            Assert.IsNull(ProgramPolicy.SplitCommand(null));
        }

        [TestMethod]
        public void AllowedExecutable_ProgramFilesAndWindowsOnly()
        {
            Assert.IsTrue(ProgramPolicy.IsAllowedExecutable(@"C:\Program Files\Example\uninst.exe", Roots));
            Assert.IsTrue(ProgramPolicy.IsAllowedExecutable(@"c:\program files (x86)\Example\uninst.exe", Roots));
            Assert.IsTrue(ProgramPolicy.IsAllowedExecutable(@"C:\Windows\Installer\example.exe", Roots));
            Assert.IsFalse(ProgramPolicy.IsAllowedExecutable(@"C:\Program Files\..\Users\Public\x.exe", Roots));
            Assert.IsFalse(ProgramPolicy.IsAllowedExecutable(@"C:\Program FilesEvil\x.exe", Roots), "a sibling folder");
            Assert.IsFalse(ProgramPolicy.IsAllowedExecutable(@"C:\Users\Public\x.exe", Roots));
            Assert.IsFalse(ProgramPolicy.IsAllowedExecutable(@"x.exe", Roots));
            Assert.IsFalse(ProgramPolicy.IsAllowedExecutable(null, Roots));
        }

        [TestMethod]
        public void MsiArguments_ExitCodes_Keys()
        {
            Assert.AreEqual("/x " + Code + @" /qn /norestart /l*v ""C:\w\u.log""", ProgramPolicy.MsiArguments(Code, @"C:\w\u.log"));
            try { ProgramPolicy.MsiArguments("ExampleApp", @"C:\w\u.log"); Assert.Fail(); } catch (ArgumentException) { }
            bool restart;
            Assert.IsTrue(ProgramPolicy.IsSuccess(0, out restart) && !restart);
            Assert.IsTrue(ProgramPolicy.IsSuccess(3010, out restart) && restart);
            Assert.IsTrue(ProgramPolicy.IsSuccess(1641, out restart) && restart);
            Assert.IsFalse(ProgramPolicy.IsSuccess(1603, out restart));
            Assert.IsTrue(ProgramPolicy.IsValidKey("Example App_is1"));
            Assert.IsFalse(ProgramPolicy.IsValidKey(@"a\b"));
            Assert.IsFalse(ProgramPolicy.IsValidKey(""));
            Assert.IsFalse(ProgramPolicy.IsValidKey(new string('x', 256)));
        }

        [TestMethod]
        public void UninstallArguments_RoundTrip()
        {
            string text = ProgramPolicy.UninstallArguments("32", "Example App_is1", "Example App é", true, true);
            StringAssert.StartsWith(text, "uninstall-program --view 32 --key \"Example App_is1\" --name ");
            StringAssert.EndsWith(text, "--allow-servers --allow-domain-controllers");
            string[] args = { "uninstall-program", "--view", "32", "--key", "Example App_is1", "--name", ProgramPolicy.Encode("Example App é"), "--allow-servers" };
            string view, key, name;
            bool servers, dcs;
            Assert.IsNull(ProgramPolicy.ParseUninstall(args, out view, out key, out name, out servers, out dcs));
            Assert.AreEqual("32", view);
            Assert.AreEqual("Example App_is1", key);
            Assert.AreEqual("Example App é", name);
            Assert.IsTrue(servers);
            Assert.IsFalse(dcs);
            Assert.IsNotNull(ProgramPolicy.ParseUninstall(new[] { "uninstall-program", "--view", "16", "--key", "K", "--name", ProgramPolicy.Encode("x") }, out view, out key, out name, out servers, out dcs));
            Assert.IsNotNull(ProgramPolicy.ParseUninstall(new[] { "uninstall-program", "--view", "64", "--key", "K" }, out view, out key, out name, out servers, out dcs), "name missing");
            Assert.IsNotNull(ProgramPolicy.ParseUninstall(new[] { "uninstall-program", "--view", "64", "--key", "K", "--name", "%%%" }, out view, out key, out name, out servers, out dcs), "not base64");
            try { ProgramPolicy.UninstallArguments("64", "a\"b", "x", false, false); Assert.Fail(); } catch (ArgumentException) { }
            Assert.AreEqual(ServerDecision.RefusedServer, ServerPolicy.Decide(ProgramPolicy.UninstallCommand, ServerPolicy.ProductServer, false, false));
            Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(ProgramPolicy.ListCommand, ServerPolicy.ProductServer, false, false), "the list is read only");
        }

        [TestMethod]
        public void Merge_OneRowPerProgram_StricterKind()
        {
            Func<string, string, string, string, string> line = (key, name, version, kind) => WorkerProtocol.Format("type", "program", "view", "64", "key", key,
                "name", name, "version", version, "publisher", "Example Corp", "kind", kind, "reason", kind == "notremovable" ? "noquiet" : "");
            List<KeyValuePair<string, IList<WorkerMessage>>> lists = new List<KeyValuePair<string, IList<WorkerMessage>>>
            {
                new KeyValuePair<string, IList<WorkerMessage>>("PC01", new[] { line(Code, "Example App", "1.0", "msi"), line("Other_is1", "Other App", "2.0", "quietexe") }
                    .Select(WorkerProtocol.Parse).ToList()),
                new KeyValuePair<string, IList<WorkerMessage>>("PC02", new[] { line(Code, "Example App", "1.1", "msi"), line("Other_is1", "Other App", "2.0", "notremovable") }
                    .Select(WorkerProtocol.Parse).ToList())
            };
            List<ProgramRow> rows = ProgramInventory.Merge(lists);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("Example App", rows[0].Name);
            CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, rows[0].Computers);
            CollectionAssert.AreEqual(new[] { "1.0", "1.1" }, rows[0].Versions.ToList());
            Assert.IsTrue(rows[0].Removable);
            Assert.IsFalse(rows[1].Removable, "not removable on one computer: not offered");
            Assert.AreEqual(1, ProgramInventory.Filter(rows, "other corp").Count);
            Assert.AreEqual(2, ProgramInventory.Filter(rows, " ").Count);
        }
    }
}
