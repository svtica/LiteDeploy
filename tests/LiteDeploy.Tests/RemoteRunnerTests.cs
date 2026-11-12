// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteRunnerTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: WinRM method: values passed to the script as data, script complete,
//          ASCII and parsed by Windows PowerShell; failure messages; the
//          worker's --output option.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class RemoteRunnerTests
    {
        [TestMethod]
        public void Quote_KeepsValuesAsData()
        {
            Assert.AreEqual("'PC-01'", RemoteRunner.Quote("PC-01"));
            Assert.AreEqual("'a''b'", RemoteRunner.Quote("a'b"));
            Assert.AreEqual("'a''b'", RemoteRunner.Quote("a’b"));
            Assert.AreEqual("'$(calc)'", RemoteRunner.Quote("$(calc)"));
            Assert.AreEqual("''", RemoteRunner.Quote(null));
        }

        [TestMethod]
        public void Script_IsCompleteAndAscii()
        {
            String script = RemoteRunner.BuildScript("PC-01", "scan --package \"Mozilla.Firefox|firefox|Firefox\"", true, 20);
            Assert.IsFalse(script.Contains("%COMPUTER%") || script.Contains("%ARGS%") || script.Contains("%ID%") || script.Contains("%TIMEOUT%") || script.Contains("%CRED%"));
            StringAssert.Contains(script, "$computer = 'PC-01'");
            StringAssert.Contains(script, "$workerArgs = 'scan --package \"Mozilla.Firefox|firefox|Firefox\"'");
            StringAssert.Contains(script, "$timeout = 20");
            StringAssert.Contains(script, "if ($true)");
            StringAssert.Contains(script, "Unregister-ScheduledTask");
            Assert.IsTrue(script.All(c => c < 128), "ASCII only");
            Assert.IsTrue(RemoteRunner.BuildScript("PC-01", "info", false, 2).Contains("if ($false)"));
        }

        [TestMethod]
        public void Script_ParsesInWindowsPowerShell()
        {
            String exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(exe)) Assert.Inconclusive("Windows PowerShell not available");
            String file = Path.Combine(Path.GetTempPath(), "LiteDeploy-remote-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(file, RemoteRunner.BuildScript("PC-01", "info", true, 2), Encoding.ASCII);
            try
            {
                String command = "$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('" + file + "', [ref]$null, [ref]$e); $e | ForEach-Object { $_.ToString() }; exit @($e).Count";
                ProcessStartInfo psi = new ProcessStartInfo(exe, "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "\\\"") + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    String output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    Assert.AreEqual(0, p.ExitCode, output);
                }
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void Failure_PsExecFileNotFound_SuggestsWinRm()
        {
            String text = RemoteRunner.DescribeFailure(RemoteRunner.PsExec, 2, "PsExec v2.43\nConnecting to PC-01...\nThe system cannot find the file specified.\n");
            StringAssert.Contains(text, "PsExec");
            StringAssert.Contains(text, "The system cannot find the file specified.");
            StringAssert.Contains(text, "WinRM");
            String winrm = RemoteRunner.DescribeFailure(RemoteRunner.WinRm, 1001, "WinRM: Connecting to remote server PC-01 failed\n");
            StringAssert.Contains(winrm, "WinRM: Connecting to remote server PC-01 failed");
            Assert.IsNotNull(RemoteRunner.OemEncoding());
        }

        [TestMethod]
        public void Worker_OutputOption_IsRemovedAndWritten()
        {
            String file = Path.Combine(Path.GetTempPath(), "LiteDeploy-out-" + Guid.NewGuid().ToString("N") + ".txt");
            String error;
            String[] rest = Worker.Program.TakeOutputOption(new[] { "scan", "--output", file, "--package", "A|b|C" }, out error);
            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { "scan", "--package", "A|b|C" }, rest);
            Assert.IsTrue(File.Exists(file));
            Worker.Program.TakeOutputOption(new[] { "info", "--output" }, out error);
            Assert.IsNotNull(error);
        }
    }
}
