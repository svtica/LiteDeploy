// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OracleJavaModuleTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-24
// Purpose: Unit tests of the relaunch helpers of the OracleJava module (dot-
//          sourced in Windows PowerShell: the functions only, nothing runs):
//          arguments of a command line, working folder of a -jar program, and
//          the program to start when the old Java folder is gone. Fictitious
//          paths only.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class OracleJavaModuleTests
    {
        static string Run(string commands)
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(exe)) Assert.Inconclusive("Windows PowerShell not available");
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "repository root not found");
            string module = Path.Combine(dir, "modules", "OracleJava.ps1");
            // No progress records on standard error ("Preparing modules for first use" on a fresh runner)
            string script = "$ProgressPreference = 'SilentlyContinue'\n. '" + module.Replace("'", "''") + "'\n" + commands;
            ProcessStartInfo psi = new ProcessStartInfo(exe, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                string errors = p.StandardError.ReadToEnd();
                p.WaitForExit();
                // A real error is on standard error as text; CLIXML progress records are not errors
                string text = errors.Trim();
                if (text.Length > 0 && !text.StartsWith("#< CLIXML", StringComparison.Ordinal))
                    Assert.Fail("PowerShell errors: " + text);
                return output.Replace("\r\n", "\n").Trim();
            }
        }

        [TestMethod]
        public void CommandArguments_QuotedUnquotedNone()
        {
            string commands = @"$a = Get-CommandArguments '""C:\Java\bin\javaw.exe"" -jar ""C:\Example App\Server.jar""'
$b = Get-CommandArguments 'javaw.exe -jar C:\Example\Server.jar --port 8080'
$c = Get-CommandArguments 'javaw.exe'
""$a|$b|<$c>""";
            Assert.AreEqual(@"-jar ""C:\Example App\Server.jar""|-jar C:\Example\Server.jar --port 8080|<>", Run(commands));
        }

        [TestMethod]
        public void JarFolder_QuotedUnquotedNone()
        {
            string commands = @"$a = Get-JarFolder '-jar ""C:\Example App\Server.jar""'
$b = Get-JarFolder '-Xmx256m -jar C:\Example\Server.jar --port 8080'
$c = $null -eq (Get-JarFolder '-cp lib Example.Main')
""$a|$b|$c""";
            Assert.AreEqual(@"C:\Example App|C:\Example|True", Run(commands));
        }

        [TestMethod]
        public void RelaunchProgram_SamePathWhenItExists_NothingForAnUnknownProgram()
        {
            string commands = @"$exists = [pscustomobject]@{ Name = 'notepad.exe'; Path = ""$env:windir\System32\notepad.exe"" }
$gone = [pscustomobject]@{ Name = 'example-missing.exe'; Path = 'C:\Missing\jre\bin\example-missing.exe' }
(Get-RelaunchProgram $exists) + '|<' + (Get-RelaunchProgram $gone) + '>'";
            string result = Run(commands);
            StringAssert.EndsWith(result, @"notepad.exe|<>", result);
        }
    }
}
