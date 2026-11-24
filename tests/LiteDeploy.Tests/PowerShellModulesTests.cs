// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PowerShellModulesTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-24
// Purpose: Tests of the PowerShell7 and PSGalleryModule modules: their headers
//          (parameters, Install, instances) and their helpers, dot-sourced in
//          Windows PowerShell (the functions only, nothing is installed):
//          architecture, MSI options, detection of the MSI entry, Gallery
//          feed, package entries, signer organisation. Fictitious values only.
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
    public class PowerShellModulesTests
    {
        static string RepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "repository root not found");
            return dir;
        }

        static string ModulePath(string name)
        {
            return Path.Combine(RepoRoot(), "modules", name + ".ps1");
        }

        // Dot-sources the module in Windows PowerShell and runs the commands; their output, trimmed
        static string Run(string module, string commands)
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(exe)) Assert.Inconclusive("Windows PowerShell not available");
            string script = "$ProgressPreference = 'SilentlyContinue'\n. '" + ModulePath(module).Replace("'", "''") + "'\n" + commands;
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
                string text = errors.Trim();
                if (text.Length > 0 && !text.StartsWith("#< CLIXML", StringComparison.Ordinal))
                    Assert.Fail("PowerShell errors: " + text);
                return output.Replace("\r\n", "\n").Trim();
            }
        }

        // ---- Headers ------------------------------------------------------------------

        [TestMethod]
        public void PowerShell7_Header()
        {
            UpdateModule m = UpdateModules.Read(ModulePath("PowerShell7"));
            Assert.IsNotNull(m);
            Assert.AreEqual("PowerShell 7 (MSI, machine)", m.Label);
            Assert.AreEqual(0, m.Processes.Length, "pwsh is closed by the module, only before its own install");
            Assert.IsTrue(m.Install, "the MSI can be installed where it is missing");
            Assert.IsFalse(m.Instances, "one PowerShell 7 per computer");
            Assert.IsFalse(m.Uninstall, "never removed by LiteDeploy");
            CollectionAssert.AreEqual(new[] { "Version", "Download", "ClosePwsh", "MsiProperties" }, m.Parameters.Select(p => p.Name).ToArray());
            Assert.IsFalse(m.Parameters.Any(p => p.Required || p.Secret), "every parameter has a default, none is secret");
            Assert.AreEqual("yes", m.Parameters.Single(p => p.Name == "Download").Default);
            Assert.AreEqual("yes", m.Parameters.Single(p => p.Name == "ClosePwsh").Default);

            string text = File.ReadAllText(ModulePath("PowerShell7"));
            StringAssert.Contains(text, "[ValidateSet('Check', 'Update', 'Install')]");
            StringAssert.Contains(text, "$signer = 'Microsoft Corporation'");
            StringAssert.Contains(text, @"Join-Path $env:windir 'LiteDeploy\work\PowerShell7'", "work folder under %windir%");
            Assert.IsFalse(text.Contains("Invoke-Expression"), "no Invoke-Expression");
            Assert.IsFalse(text.Contains("http://"), "HTTPS only");
            Assert.IsFalse(text.Contains("winget.exe") || text.Contains("Get-Winget"), "the MSI line is not followed through winget");
        }

        [TestMethod]
        public void PSGalleryModule_Header()
        {
            UpdateModule m = UpdateModules.Read(ModulePath("PSGalleryModule"));
            Assert.IsNotNull(m);
            Assert.AreEqual("PowerShell Gallery module (machine)", m.Label);
            Assert.AreEqual(0, m.Processes.Length, "a new version goes side by side: nothing to close");
            Assert.IsTrue(m.Install);
            Assert.IsTrue(m.Instances, "one row per Gallery module");
            Assert.IsFalse(m.Uninstall);
            CollectionAssert.AreEqual(new[] { "Name", "Publisher", "Version", "Download", "ModulesFolder" }, m.Parameters.Select(p => p.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "Publisher" }, m.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray(),
                "the signer of the module must always be named");
            Assert.IsFalse(m.Parameters.Any(p => p.Secret));
            Assert.AreEqual("WindowsPowerShell", m.Parameters.Single(p => p.Name == "ModulesFolder").Default);

            string text = File.ReadAllText(ModulePath("PSGalleryModule"));
            StringAssert.Contains(text, "[ValidateSet('Check', 'Update', 'Install')]");
            StringAssert.Contains(text, "Test-FileCatalog", "a catalog-signed module is checked against its catalog");
            StringAssert.Contains(text, "SHA512", "the hash of the Gallery is checked");
            StringAssert.Contains(text, @"LiteDeploy\work\PSGalleryModule", "work folder under %windir%");
            foreach (string forbidden in new[] { "Invoke-Expression", "Install-Module", "Update-Module", "Install-PackageProvider", "http://", "-SkipPublisherCheck" })
                Assert.IsFalse(text.Contains(forbidden), "PSGalleryModule must not use " + forbidden);
        }

        // ---- PowerShell7 helpers ----------------------------------------------------------

        [TestMethod]
        public void PowerShell7_Architecture()
        {
            string commands = @"$a = Get-PwshArchitecture 'AMD64' ''
$b = Get-PwshArchitecture 'x86' 'AMD64'
$c = Get-PwshArchitecture 'ARM64' ''
$d = Get-PwshArchitecture 'x86' ''
$e = try { Get-PwshArchitecture 'IA64' ''; 'accepted' } catch { 'refused' }
""$a|$b|$c|$d|$e""";
            Assert.AreEqual("x64|x64|arm64|x86|refused", Run("PowerShell7", commands));
        }

        [TestMethod]
        public void PowerShell7_ReleaseVersion_NoPreview()
        {
            string commands = @"$a = Get-ReleaseVersion 'v7.6.10'
$b = Get-ReleaseVersion '7.4.6'
$c = try { Get-ReleaseVersion 'v7.7.0-preview.5'; 'accepted' } catch { 'refused' }
$d = try { Get-ReleaseVersion '7.6'; 'accepted' } catch { 'refused' }
$e = (ConvertTo-Version '7.6.10.0') -eq (ConvertTo-Version '7.6.10')
$f = Get-MsiFileName '7.6.10' 'x64'
""$a|$b|$c|$d|$e|$f""";
            Assert.AreEqual("7.6.10|7.4.6|refused|refused|True|PowerShell-7.6.10-win-x64.msi", Run("PowerShell7", commands));
        }

        [TestMethod]
        public void PowerShell7_MsiProperties_KnownNamesAndBinaryValuesOnly()
        {
            string commands = @"$a = Get-MsiPropertyArguments '  add_path=1   USE_MU=0 ENABLE_MU=0 '
$b = Get-MsiPropertyArguments ''
$c = try { Get-MsiPropertyArguments 'INSTALLFOLDER=1'; 'accepted' } catch { 'refused' }
$d = try { Get-MsiPropertyArguments 'ADD_PATH=yes'; 'accepted' } catch { 'refused' }
$e = try { Get-MsiPropertyArguments 'ADD_PATH=1 /x'; 'accepted' } catch { 'refused' }
""$a|<$b>|$c|$d|$e""";
            Assert.AreEqual("ADD_PATH=1 USE_MU=0 ENABLE_MU=0|<>|refused|refused|refused", Run("PowerShell7", commands));
        }

        [TestMethod]
        public void PowerShell7_SelectEntry_ArchitectureFirst_PreviewIgnored()
        {
            string commands = @"$entries = @(
    [pscustomobject]@{ KeyName = '{00000000-0000-0000-0000-000000000001}'; DisplayName = 'PowerShell 7-preview-x64'; DisplayVersion = '7.7.0.5'; InstallLocation = '' },
    [pscustomobject]@{ KeyName = '{00000000-0000-0000-0000-000000000002}'; DisplayName = 'PowerShell 7-x86'; DisplayVersion = '7.6.10.0'; InstallLocation = '' },
    [pscustomobject]@{ KeyName = '{00000000-0000-0000-0000-000000000003}'; DisplayName = 'PowerShell 7-x64'; DisplayVersion = '7.4.6.0'; InstallLocation = '' },
    [pscustomobject]@{ KeyName = 'Example'; DisplayName = 'Example PowerShell 7-x64 add-in'; DisplayVersion = '9.9'; InstallLocation = '' })
$a = (Select-PwshEntry $entries 'x64').DisplayVersion
$b = (Select-PwshEntry $entries 'arm64').DisplayVersion
$c = $null -eq (Select-PwshEntry @($entries[0], $entries[3]) 'x64')
""$a|$b|$c""";
            Assert.AreEqual("7.4.6.0|7.6.10.0|True", Run("PowerShell7", commands));
        }

        [TestMethod]
        public void PowerShell7_EntryArchitecture_KeptForTheUpdate()
        {
            string commands = @"$a = Get-EntryArchitecture ([pscustomobject]@{ DisplayName = 'PowerShell 7-x86' })
$b = Get-EntryArchitecture ([pscustomobject]@{ DisplayName = 'PowerShell 7-arm64' })
$c = $null -eq (Get-EntryArchitecture ([pscustomobject]@{ DisplayName = 'PowerShell 7-preview-x64' }))
# An x86 PowerShell 7 alone on an x64 computer: the x86 entry is chosen, and its architecture kept
$only = @([pscustomobject]@{ KeyName = 'a'; DisplayName = 'PowerShell 7-x86'; DisplayVersion = '7.4.6.0'; InstallLocation = '' })
$d = Get-EntryArchitecture (Select-PwshEntry $only 'x64')
""$a|$b|$c|$d""";
            Assert.AreEqual("x86|arm64|True|x86", Run("PowerShell7", commands));
        }

        [TestMethod]
        public void PowerShell7_UnmanagedCopy_PwshWithoutMsi()
        {
            string commands = @"$pf = Join-Path ([IO.Path]::GetTempPath()) ('ld-test-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $pf 'PowerShell\7') | Out-Null
    $before = Test-UnmanagedCopy $pf
    Set-Content -LiteralPath (Join-Path $pf 'PowerShell\7\pwsh.exe') -Value 'x' -Encoding ASCII
    $after = Test-UnmanagedCopy $pf
    ""$before|$after""
} finally { Remove-Item -LiteralPath $pf -Recurse -Force -ErrorAction SilentlyContinue }";
            Assert.AreEqual("False|True", Run("PowerShell7", commands));
        }

        [TestMethod]
        public void PowerShell7_MicrosoftSubject()
        {
            string commands = @"$a = Test-MicrosoftSubject 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
$b = Test-MicrosoftSubject 'CN=Example, O=Microsoft Corporation Example Ltd, C=US'
$c = Test-MicrosoftSubject 'CN=Example, O=""Example, Microsoft Corporation"", C=US'
""$a|$b|$c""";
            Assert.AreEqual("True|False|False", Run("PowerShell7", commands));
        }

        // ---- PSGalleryModule helpers -------------------------------------------------------

        [TestMethod]
        public void PSGallery_ModuleNameAndVersion()
        {
            string commands = @"$n = @('PSWindowsUpdate', 'Example.Module-2_x', '..\Example', 'Ex*', 'a..b', '') | ForEach-Object { Test-ModuleName $_ }
$v = @('2.2.1.5', '1.0', '1.0.0-beta1', '1', 'x') | ForEach-Object { [string](ConvertTo-ModuleVersion $_) }
($n -join ',') + '|' + ($v -join ',')";
            Assert.AreEqual("True,True,False,False,False,False|2.2.1.5,1.0.0.0,,,", Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_ModulesRoot()
        {
            string commands = @"$a = Get-ModulesRoot '' 'C:\Program Files'
$b = Get-ModulesRoot 'PowerShell' 'C:\Program Files'
$c = try { Get-ModulesRoot 'C:\Temp' 'C:\Program Files'; 'accepted' } catch { 'refused' }
""$a|$b|$c""";
            Assert.AreEqual(@"C:\Program Files\WindowsPowerShell\Modules|C:\Program Files\PowerShell\Modules|refused", Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_Feed_LatestStableListed_OrTheVersionAsked()
        {
            string commands = @"function Entry($v, $pre, $pub) {
    ""<entry><content type='application/zip' src='https://www.powershellgallery.com/api/v2/package/ExampleModule/$v'/>"" +
    ""<m:properties><d:Version>$v</d:Version><d:IsPrerelease>$pre</d:IsPrerelease><d:Published>$pub</d:Published>"" +
    ""<d:PackageHash>QUJD</d:PackageHash><d:PackageHashAlgorithm>SHA512</d:PackageHashAlgorithm></m:properties></entry>""
}
$xml = ""<feed xmlns='http://www.w3.org/2005/Atom' xmlns:d='http://schemas.microsoft.com/ado/2007/08/dataservices' xmlns:m='http://schemas.microsoft.com/ado/2007/08/dataservices/metadata'>"" +
    (Entry '1.2.0' 'false' '2026-01-10T00:00:00') + (Entry '1.10.0' 'false' '2026-03-10T00:00:00') +
    (Entry '2.0.0-beta1' 'true' '2026-05-10T00:00:00') + (Entry '1.11.0' 'false' '1900-01-01T00:00:00') +
    ""<link rel='next' href='https://www.powershellgallery.com/api/v2/FindPackagesById?skip=100'/></feed>""
$feed = Read-GalleryFeed $xml
$latest = Select-GalleryRelease $feed.Entries ''
$asked = Select-GalleryRelease $feed.Entries '1.2'
$unlisted = Select-GalleryRelease $feed.Entries '1.11.0'
$none = $null -eq (Select-GalleryRelease $feed.Entries '2.0.0')
""$($feed.Entries.Count)|$($latest.Version)|$($latest.Url)|$($latest.HashAlgorithm)|$($asked.Version)|$($unlisted.Version)|$none|$(Test-GalleryUrl $feed.Next)""";
            Assert.AreEqual("4|1.10.0|https://www.powershellgallery.com/api/v2/package/ExampleModule/1.10.0|SHA512|1.2.0|1.11.0|True|True",
                Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_GalleryUrl_HttpsAndGalleryHostOnly()
        {
            string commands = @"(@('https://www.powershellgallery.com/api/v2/package/X/1.0', 'https://cdn.powershellgallery.com/packages/x.1.0.nupkg',
  'http://www.powershellgallery.com/api/v2', 'https://www.powershellgallery.com.example.com/x', 'https://example.com/x', 'not a url') |
  ForEach-Object { [string](Test-GalleryUrl $_) }) -join ','";
            Assert.AreEqual("True,True,False,False,False,False", Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_EntryPath_PackageFilesSkipped()
        {
            string commands = @"(@('ExampleModule.psd1', 'en-US/about_Example%20Module.help.txt', '[Content_Types].xml', '_rels/.rels',
  'package/services/metadata/core-properties/x.psmdcp', 'ExampleModule.nuspec', 'docs/', 'lib/Other.nuspec') |
  ForEach-Object { '<' + (Get-EntryPath $_) + '>' }) -join ''";
            string result = Run("PSGalleryModule", commands);
            Assert.AreEqual("<ExampleModule.psd1><en-US/about_Example Module.help.txt><><><><><><lib/Other.nuspec>", result);
        }

        [TestMethod]
        public void PSGallery_ExpandNupkg_RefusesAnEntryThatLeavesTheFolder()
        {
            string commands = @"Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$dir = Join-Path ([IO.Path]::GetTempPath()) ('ld-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
function New-Package([string] $Path, [string[]] $Names) {
    $zip = [IO.Compression.ZipFile]::Open($Path, 'Create')
    try { foreach ($n in $Names) { $w = New-Object IO.StreamWriter($zip.CreateEntry($n).Open()); $w.Write('x'); $w.Close() } } finally { $zip.Dispose() }
}
try {
    New-Package (Join-Path $dir 'good.nupkg') @('ExampleModule.psd1', 'Public/Get-Example.ps1', '[Content_Types].xml', 'ExampleModule.nuspec')
    $out = Join-Path $dir 'good'; New-Item -ItemType Directory -Path $out | Out-Null
    Expand-Nupkg (Join-Path $dir 'good.nupkg') $out
    $files = (Get-ChildItem -LiteralPath $out -Recurse -File -Name | Sort-Object) -join ','
    New-Package (Join-Path $dir 'bad.nupkg') @('ExampleModule.psd1', '../../escape.ps1')
    $bad = Join-Path $dir 'bad'; New-Item -ItemType Directory -Path $bad | Out-Null
    $refused = try { Expand-Nupkg (Join-Path $dir 'bad.nupkg') $bad; 'accepted' } catch { 'refused' }
    $escaped = Test-Path (Join-Path $dir 'escape.ps1')
    ""$files|$refused|$escaped""
} finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }";
            Assert.AreEqual(@"ExampleModule.psd1,Public\Get-Example.ps1|refused|False", Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_UnsignedModule_Refused()
        {
            string commands = @"$dir = Join-Path ([IO.Path]::GetTempPath()) ('ld-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $dir 'ExampleModule.psd1') -Value ""@{ ModuleVersion = '1.0.0' }"" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $dir 'readme.txt') -Value 'text' -Encoding ASCII
    $why = Get-ModuleRefusal $dir 'ExampleModule' 'Example Author Ltd'
    if ($why -like 'ExampleModule.psd1: no valid signature*') { 'refused' } else { ""<$why>"" }
} finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }";
            Assert.AreEqual("refused", Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_SubjectOrganisation()
        {
            string commands = @"$s = 'CN=Example Author, O=Example Author Ltd, L=Example, C=US'
$a = Test-SubjectOrganisation $s 'Example Author Ltd'
$b = Test-SubjectOrganisation $s 'Other Vendor Inc.; Example Author Ltd'
$c = Test-SubjectOrganisation $s 'Example Author'
$d = Test-SubjectOrganisation 'CN=Example Author' 'Example Author'
$e = @(Get-PublisherNames ' Example Author Ltd ; ; Other Vendor Inc. ').Count
$f = Test-SubjectOrganisation 'CN=Example Author, L=Example, C=CA' 'Other Vendor Inc.; Example Author'
$g = Test-SubjectOrganisation 'CN=Example Author Two, C=CA' 'Example Author'
""$a|$b|$c|$d|$e|$f|$g""";
            // CN= counts only when the subject has no O= (a publisher signing as a person): $c stays refused
            Assert.AreEqual("True|True|False|True|2|True|False", Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_MachineVersions_BothModulesFolders()
        {
            string commands = @"$pf = Join-Path ([IO.Path]::GetTempPath()) ('ld-test-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($p in 'WindowsPowerShell\Modules\ExampleModule\1.2.0', 'PowerShell\Modules\ExampleModule\1.10.0', 'PowerShell\Modules\ExampleModule\2.0.0') {
        $dir = Join-Path $pf $p
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        if ($p -notlike '*2.0.0') { Set-Content -LiteralPath (Join-Path $dir 'ExampleModule.psd1') -Value '@{}' -Encoding ASCII }
    }
    $all = @(Get-MachineVersions $pf 'ExampleModule')
    $none = @(Get-MachineVersions $pf 'OtherModule').Count
    ""$($all.Count)|$($all[0].Text)|$(Split-Path -Leaf (Split-Path -Parent $all[0].Root))|$($all[1].Text)|$none""
} finally { Remove-Item -LiteralPath $pf -Recurse -Force -ErrorAction SilentlyContinue }";
            // 2.0.0 has no manifest: not a version; the highest one is the one of the PowerShell 7 folder
            Assert.AreEqual("2|1.10.0|PowerShell|1.2.0|0", Run("PSGalleryModule", commands));
        }

        [TestMethod]
        public void PSGallery_RequiredModules()
        {
            string commands = @"$m = @{ RequiredModules = @('ExampleBase', @{ ModuleName = 'ExampleTools'; ModuleVersion = '2.1' }, @{ ModuleName = 'ExamplePinned'; RequiredVersion = '1.0.3' }) }
(Get-RequiredModules $m | ForEach-Object { $_.Name + '=' + $_.Version }) -join ','";
            Assert.AreEqual("ExampleBase=,ExampleTools=2.1,ExamplePinned=1.0.3", Run("PSGalleryModule", commands));
        }
    }
}
