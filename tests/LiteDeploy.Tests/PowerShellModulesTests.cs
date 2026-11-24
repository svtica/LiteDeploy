// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PowerShellModulesTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-24
// Purpose: Unit tests of the helpers of the PowerShell7 and PSGalleryModule
//          modules (dot-sourced in Windows PowerShell: the functions only,
//          nothing is installed): versions, choice of the MSI or package of
//          the cache, LTS version of the metadata, MSI properties, signer of
//          a certificate subject, paths of the entries of a package and the
//          feed of the PowerShell Gallery. Fictitious names only.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class PowerShellModulesTests
    {
        static string Run(string moduleFile, string commands)
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(exe)) Assert.Inconclusive("Windows PowerShell not available");
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "repository root not found");
            string module = Path.Combine(dir, "modules", moduleFile);
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

        // ---- PowerShell7 ------------------------------------------------------------------

        [TestMethod]
        public void PowerShell7_Versions_ThreePartsNoPreview()
        {
            string commands = @"$a = ConvertTo-PwshVersion 'v7.6.10'
$b = ConvertTo-PwshVersion '7.6.10.0'
$c = ConvertTo-PwshVersion '7.7.0-preview.5'
""$a|$b|<$c>""";
            Assert.AreEqual("7.6.10|7.6.10|<>", Run("PowerShell7.ps1", commands));
        }

        [TestMethod]
        public void PowerShell7_CacheMsi_NewestOrPinned_OfTheArchitecture()
        {
            string commands = @"$names = 'PowerShell-7.4.6-win-x64.msi', 'PowerShell-7.6.10-win-x64.msi', 'PowerShell-7.6.11-win-arm64.msi', 'Example.msi', 'PowerShell-7.6.2-win-x64.msi'
$a = (Select-CacheMsi $names 'x64' $null).Name
$b = (Select-CacheMsi $names 'x64' ([version]'7.4.6')).Name
$c = Select-CacheMsi $names 'x86' $null
$d = Select-CacheMsi $names 'x64' ([version]'7.5.0')
""$a|$b|<$c>|<$d>""";
            Assert.AreEqual("PowerShell-7.6.10-win-x64.msi|PowerShell-7.4.6-win-x64.msi|<>|<>", Run("PowerShell7.ps1", commands));
        }

        [TestMethod]
        public void PowerShell7_LtsVersion_HighestOfTheList()
        {
            string commands = @"$m = '{""StableReleaseTag"":""v7.7.0"",""ReleaseTag"":""v7.6.10"",""LTSReleaseTag"":[""v7.4.13"",""v7.6.10""]}' | ConvertFrom-Json
$one = '{""LTSReleaseTag"":""v7.6.2""}' | ConvertFrom-Json
$none = '{""StableReleaseTag"":""v7.7.0""}' | ConvertFrom-Json
""$(Get-LtsVersion $m)|$(Get-LtsVersion $one)|<$(Get-LtsVersion $none)>|$(Get-MsiName ([version]'7.6.10') 'x64')""";
            Assert.AreEqual("7.6.10|7.6.2|<>|PowerShell-7.6.10-win-x64.msi", Run("PowerShell7.ps1", commands));
        }

        [TestMethod]
        public void PowerShell7_MsiProperties_NameValueOnly()
        {
            string commands = @"$ok = Get-MsiProperties '  ADD_PATH=1   USE_MU=0 ENABLE_MU=0 '
$empty = Get-MsiProperties ''
$refused = foreach ($t in '/x {00000000-0000-0000-0000-000000000000}', 'A=""b c""', 'add_path=1', 'A=1;calc') { try { $null = Get-MsiProperties $t; 'accepted' } catch { 'refused' } }
""$ok|<$empty>|$($refused -join ',')""";
            Assert.AreEqual("ADD_PATH=1 USE_MU=0 ENABLE_MU=0|<>|refused,refused,refused,refused", Run("PowerShell7.ps1", commands));
        }

        [TestMethod]
        public void PowerShell7_MicrosoftSubject_ExactOrganisation()
        {
            string commands = @"$a = Test-MicrosoftSubject 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
$b = Test-MicrosoftSubject 'CN=Example, O=Microsoft Corporation Example, C=US'
$c = Test-MicrosoftSubject 'CN=Microsoft Corporation, O=Contoso Ltd, C=US'
""$a|$b|$c""";
            Assert.AreEqual("True|False|False", Run("PowerShell7.ps1", commands));
        }

        // ---- PSGalleryModule --------------------------------------------------------------

        [TestMethod]
        public void PSGalleryModule_Names_AndStableVersions()
        {
            string commands = @"$n = foreach ($x in 'ExampleModule', 'Example.Module-2_x', 'a''b', 'x.', '..\x', '') { Test-ModuleName $x }
$v = ConvertTo-ModuleVersion '2.2.1.5'
$pre = ConvertTo-ModuleVersion '1.0.0-beta1'
""$($n -join ',')|$v|<$pre>""";
            Assert.AreEqual("True,True,False,False,False,False|2.2.1.5|<>", Run("PSGalleryModule.ps1", commands));
        }

        [TestMethod]
        public void PSGalleryModule_Signer_OrganisationElseCommonName()
        {
            string commands = @"$a = Test-SubjectPublisher 'CN=Example Dev, O=""Contoso, Ltd"", C=CA' '""Contoso, Ltd""'
$b = Test-SubjectPublisher 'CN=Jane Example' 'Contoso Ltd; Jane Example'
$c = Test-SubjectPublisher 'CN=Jane Example, O=Fabrikam Inc.' 'Jane Example'
$d = Test-SubjectPublisher 'CN=Example, O=Contoso Ltd' 'contoso ltd'
$e = Test-SubjectPublisher 'CN=Example, O=Contoso Ltd Example' 'Contoso Ltd'
""$a|$b|$c|$d|$e""";
            Assert.AreEqual("True|True|False|False|False", Run("PSGalleryModule.ps1", commands));
        }

        [TestMethod]
        public void PSGalleryModule_PackageEntries_NuGetFilesSkipped_TraversalRefused()
        {
            string commands = @"$kept = (Get-PackageEntryPath 'ExampleModule.psd1') + '|' + (Get-PackageEntryPath 'en-US/about%20Example.help.txt')
$skipped = @('_rels/.rels', '[Content_Types].xml', 'ExampleModule.nuspec', 'package/services/metadata/x.psmdcp', 'lib/') | Where-Object { Get-PackageEntryPath $_ }
$refused = foreach ($x in '../x.ps1', 'a/../../x.ps1', '/x.ps1', 'C:/x.ps1', 'a%2F..%2F..%2Fx.ps1') { try { $null = Get-PackageEntryPath $x; 'accepted' } catch { 'refused' } }
""$kept|$(@($skipped).Count)|$($refused -join ',')""";
            Assert.AreEqual(@"ExampleModule.psd1|en-US\about Example.help.txt|0|refused,refused,refused,refused,refused", Run("PSGalleryModule.ps1", commands));
        }

        [TestMethod]
        public void PSGalleryModule_GalleryFeed_LatestStable_Pinned_NextPage()
        {
            string commands = @"$feed = @'
<?xml version=""1.0"" encoding=""utf-8""?>
<feed xml:base=""https://www.powershellgallery.com/api/v2"" xmlns=""http://www.w3.org/2005/Atom"" xmlns:d=""http://schemas.microsoft.com/ado/2007/08/dataservices"" xmlns:m=""http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"">
<entry><id>a</id><m:properties><d:Id>ExampleModule</d:Id><d:Version>2.1.0</d:Version><d:IsPrerelease m:type=""Edm.Boolean"">false</d:IsPrerelease><d:PackageHash>AAA=</d:PackageHash><d:PackageHashAlgorithm>SHA512</d:PackageHashAlgorithm></m:properties></entry>
<entry><id>b</id><m:properties><d:Id>ExampleModule</d:Id><d:Version>2.10.0</d:Version><d:IsPrerelease m:type=""Edm.Boolean"">false</d:IsPrerelease><d:PackageHash>BBB=</d:PackageHash><d:PackageHashAlgorithm>SHA512</d:PackageHashAlgorithm></m:properties></entry>
<entry><id>c</id><m:properties><d:Id>ExampleModule</d:Id><d:Version>3.0.0-beta1</d:Version><d:IsPrerelease m:type=""Edm.Boolean"">true</d:IsPrerelease><d:PackageHash>CCC=</d:PackageHash><d:PackageHashAlgorithm>SHA512</d:PackageHashAlgorithm></m:properties></entry>
<link rel=""next"" href=""https://www.powershellgallery.com/api/v2/FindPackagesById?id='ExampleModule'&amp;$skip=100"" />
</feed>
'@
$e = @(ConvertFrom-GalleryFeed $feed)
$latest = Select-GalleryEntry $e $null
$pinned = Select-GalleryEntry $e ([version]'2.1.0')
$missing = Select-GalleryEntry $e ([version]'9.0')
""$($e.Count)|$($latest.Version)/$($latest.Hash)|$($pinned.Hash)|<$missing>|$(Get-FeedNextLink $feed)""";
            Assert.AreEqual("3|2.10.0/BBB=|AAA=|<>|https://www.powershellgallery.com/api/v2/FindPackagesById?id='ExampleModule'&$skip=100",
                Run("PSGalleryModule.ps1", commands));
        }

        [TestMethod]
        public void PSGalleryModule_CacheNupkg_NewestOrPinned_OfThatModule()
        {
            string commands = @"$names = 'examplemodule.2.1.0.nupkg', 'ExampleModule.2.10.0.nupkg', 'ExampleModuleX.9.0.0.nupkg', 'ExampleModule.3.0.0-beta1.nupkg'
$a = (Select-CacheNupkg $names 'ExampleModule' $null).Name
$b = (Select-CacheNupkg $names 'ExampleModule' ([version]'2.1.0')).Name
$c = Select-CacheNupkg $names 'OtherModule' $null
""$a|$b|<$c>""";
            Assert.AreEqual("ExampleModule.2.10.0.nupkg|examplemodule.2.1.0.nupkg|<>", Run("PSGalleryModule.ps1", commands));
        }

        [TestMethod]
        public void PSGalleryModule_RequiredModules_NamesAndVersions()
        {
            string commands = @"$r = Get-RequiredModules @{ RequiredModules = @('ExampleA', @{ ModuleName = 'ExampleB'; ModuleVersion = '1.2' }, @{ ModuleName = 'ExampleC'; RequiredVersion = '3.0.1' }) }
($r | ForEach-Object { ""$($_.Name)/$($_.Version)"" }) -join ','";
            Assert.AreEqual("ExampleA/,ExampleB/1.2,ExampleC/3.0.1", Run("PSGalleryModule.ps1", commands));
        }
    }
}
