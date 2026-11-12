// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MarkOfTheWebTests.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Unit tests of MarkOfTheWeb on real files (NTFS alternate data
//          streams), on .NET Framework as well as .NET.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteSuite.Tests
{
    [TestClass]
    public class MarkOfTheWebTests
    {
        string folder;

        [TestInitialize]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "litesuite-motw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }

        // Writes the Zone.Identifier stream with cmd.exe: .NET Framework cannot open a stream path
        static void Mark(string file)
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c echo [ZoneTransfer]>\"" + file + ":Zone.Identifier\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                Assert.AreEqual(0, p.ExitCode);
            }
        }

        string NewFile(string name)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, "content");
            return path;
        }

        [TestMethod]
        public void HasMark_And_RemoveFromFile()
        {
            string file = NewFile("tool.exe");
            Assert.IsFalse(MarkOfTheWeb.HasMark(file));
            Assert.IsFalse(MarkOfTheWeb.RemoveFromFile(file));

            Mark(file);
            Assert.IsTrue(MarkOfTheWeb.HasMark(file));
            Assert.IsTrue(MarkOfTheWeb.RemoveFromFile(file));
            Assert.IsFalse(MarkOfTheWeb.HasMark(file));
            Assert.AreEqual("content", File.ReadAllText(file));
        }

        [TestMethod]
        public void RemoveFromDirectory_CountsUnblockedFiles()
        {
            Mark(NewFile("a.exe"));
            Mark(NewFile(Path.Combine("sub", "b.ps1")));
            NewFile("c.txt");

            Assert.AreEqual(1, MarkOfTheWeb.RemoveFromDirectory(folder, false));
            Assert.AreEqual(1, MarkOfTheWeb.RemoveFromDirectory(folder));
            Assert.AreEqual(0, MarkOfTheWeb.RemoveFromDirectory(folder));
        }

        [TestMethod]
        public void MissingFileOrFolder_IsHarmless()
        {
            Assert.IsFalse(MarkOfTheWeb.HasMark(Path.Combine(folder, "missing.exe")));
            Assert.IsFalse(MarkOfTheWeb.HasMark(null));
            Assert.AreEqual(0, MarkOfTheWeb.RemoveFromDirectory(Path.Combine(folder, "missing")));
        }
    }
}
