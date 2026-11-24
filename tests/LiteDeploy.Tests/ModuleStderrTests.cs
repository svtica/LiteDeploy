// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleStderrTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-24
// Purpose: Shipped modules run with $ErrorActionPreference = 'Stop': in Windows
//          PowerShell 5.1 every line a program writes to standard error (2>&1)
//          then stops the script ("java -version" writes its version there).
//          Each 2>&1 of a module must follow, within a few lines, a local
//          $ErrorActionPreference = 'Continue'.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class ModuleStderrTests
    {
        [TestMethod]
        public void ShippedModules_RedirectStandardErrorOnlyWithContinue()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "repository root not found");
            List<string> wrong = new List<string>();
            foreach (string path in Directory.GetFiles(Path.Combine(dir, "modules"), "*.ps1"))
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.IndexOf("2>&1", StringComparison.Ordinal) < 0) continue;
                    bool guarded = false;
                    for (int j = Math.Max(0, i - 4); j < i; j++)
                        if (lines[j].Contains("$ErrorActionPreference = 'Continue'")) guarded = true;
                    if (!guarded) wrong.Add(Path.GetFileName(path) + ":" + (i + 1));
                }
            }
            Assert.AreEqual(0, wrong.Count, "2>&1 without a local $ErrorActionPreference = 'Continue': " + string.Join(", ", wrong));
        }
    }
}
