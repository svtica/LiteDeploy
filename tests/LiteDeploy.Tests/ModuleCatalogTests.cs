// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleCatalogTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Unit tests of the catalog of module instances (docs/modules.md):
//          JSON parsing, validation of the instance and parameter names, and
//          the import of a product into module-settings.xml.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class ModuleCatalogTests
    {
        const string Example = @"{
  ""module"": ""CachedInstaller"",
  ""products"": [
    {
      ""Instance"": ""ContosoExampleApp"",
      ""Product"": ""Contoso Example App"",
      ""File"": ""CachedInstaller/ExampleVendor/ContosoExampleApp-2.4.1.msi"",
      ""Type"": ""msi"",
      ""DetectName"": ""Contoso Example App*"",
      ""Publisher"": ""ExampleVendor Inc."",
      ""Comment"": null
    },
    {
      ""Instance"": ""ExampleTool"",
      ""File"": ""CachedInstaller/ExampleVendor/ExampleTool-setup.exe"",
      ""Arguments"": ""/S /norestart"",
      ""Version"": 3.2,
      ""Publisher"": ""ExampleVendor, Inc."",
      ""CloseProcesses"": ""exampletool, exampletool-helper""
    }
  ]
}";

        static UpdateModule CachedInstaller()
        {
            return UpdateModules.Parse("CachedInstaller", new[]
            {
                "# LiteDeploy-Module",
                "# Label: Installer from the shared cache",
                "# Instances: yes",
                "# Parameter: Product default= Product",
                "# Parameter: File Path under the cache",
                "# Parameter: Type default= msi or exe",
                "# Parameter: Arguments default= Arguments",
                "# Parameter: DetectName default= Wildcard",
                "# Parameter: ProductCode default= Product code",
                "# Parameter: Version default= Version",
                "# Parameter: Publisher Signer",
                "# Parameter: CloseProcesses default= Processes",
                "# Parameter: Token secret A secret",
            }, "CachedInstaller.ps1");
        }

        [TestMethod]
        public void Parse_ProductsAndValues()
        {
            ModuleCatalog c = ModuleCatalog.Parse(Example);
            Assert.AreEqual(0, c.Errors.Count, String.Join(" | ", c.Errors));
            Assert.AreEqual("CachedInstaller", c.Module);
            CollectionAssert.AreEqual(new[] { "ContosoExampleApp", "ExampleTool" }, c.Entries.Select(e => e.Instance).ToArray());

            ModuleCatalogEntry app = c.Entries[0];
            Assert.AreEqual("Contoso Example App", app.Label);
            Assert.AreEqual("CachedInstaller/ExampleVendor/ContosoExampleApp-2.4.1.msi", app.Values["file"]);
            Assert.AreEqual("ExampleVendor Inc.", app.Values["Publisher"]);
            Assert.IsFalse(app.Values.ContainsKey("Instance"), "the instance is not a parameter");
            Assert.IsFalse(app.Values.ContainsKey("Comment"), "null: no value");

            ModuleCatalogEntry tool = c.Entries[1];
            Assert.AreEqual("ExampleTool", tool.Label, "no Product: the instance name");
            Assert.AreEqual("3.2", tool.Values["Version"], "a number is taken as its text");
            Assert.AreEqual("ExampleVendor, Inc.", tool.Values["Publisher"]);
        }

        [TestMethod]
        public void Parse_DefaultModule_AndInvalidProductsSkipped()
        {
            ModuleCatalog c = ModuleCatalog.Parse(@"{ ""products"": [
                { ""Instance"": ""Good"", ""File"": ""a.msi"", ""bad-name"": ""x"", ""Nested"": { ""a"": ""b"" } },
                { ""Instance"": ""good"", ""File"": ""duplicate.msi"" },
                { ""Instance"": ""../evil"", ""File"": ""b.msi"" },
                { ""File"": ""no-instance.msi"" },
                ""not an object""
            ] }");
            Assert.AreEqual(ModuleCatalog.DefaultModule, c.Module);
            CollectionAssert.AreEqual(new[] { "Good" }, c.Entries.Select(e => e.Instance).ToArray());
            CollectionAssert.AreEqual(new[] { "File" }, c.Entries[0].Values.Keys.ToArray());
            // bad-name, Nested, duplicate, ../evil, no instance, not an object
            Assert.AreEqual(6, c.Errors.Count, String.Join(" | ", c.Errors));
        }

        [TestMethod]
        public void Parse_PSGalleryModuleCatalog_OneInstancePerPowerShellModule()
        {
            // Example of docs/modules.md (PSGalleryModule): fictitious modules and publishers
            ModuleCatalog c = ModuleCatalog.Parse(@"{ ""module"": ""PSGalleryModule"", ""products"": [
                { ""Instance"": ""ExampleModule"", ""Publisher"": ""Contoso Ltd"" },
                { ""Instance"": ""ExampleTools"", ""Name"": ""Example.Tools"", ""Publisher"": ""Fabrikam Inc."", ""Version"": ""3.2.0"" }
            ] }");
            Assert.AreEqual(0, c.Errors.Count, String.Join(" | ", c.Errors));
            Assert.AreEqual("PSGalleryModule", c.Module);
            CollectionAssert.AreEqual(new[] { "ExampleModule", "ExampleTools" }, c.Entries.Select(e => e.Instance).ToArray());
            Assert.AreEqual("Contoso Ltd", c.Entries[0].Values["Publisher"]);
            Assert.AreEqual("Example.Tools", c.Entries[1].Values["Name"]);
            Assert.AreEqual("3.2.0", c.Entries[1].Values["Version"]);
        }

        [TestMethod]
        public void Parse_InvalidJsonOrShape_IsEmptyWithAnError()
        {
            foreach (string json in new[] { "", "not json", "[1, 2]", @"{ ""products"": ""x"" }", @"{ ""module"": ""bad/name"", ""products"": [] }", null })
            {
                ModuleCatalog c = ModuleCatalog.Parse(json);
                Assert.AreEqual(0, c.Entries.Count, json);
                Assert.AreEqual(1, c.Errors.Count, json);
            }
            Assert.AreEqual(0, ModuleCatalog.Parse("\uFEFF{ \"products\": [] }").Errors.Count, "BOM accepted");
        }

        [TestMethod]
        public void Apply_SetsTheInstanceValues_NeverASecret()
        {
            UpdateModule m = CachedInstaller();
            ModuleCatalogEntry entry = ModuleCatalog.Parse(Example).Entries[0];
            entry.Values["Token"] = "not-a-secret-source";
            entry.Values["Unknown"] = "x";

            ModuleSettings settings = new ModuleSettings();
            string key = ModuleSettings.Key("CachedInstaller", "ContosoExampleApp");
            settings.SetValue(key, "Version", "1.0");                  // not in the catalog: cleared
            settings.SetCredential(key, "Token", "Example token");    // secret: kept
            settings.SetValue("CachedInstaller", "File", "module-level.msi");
            List<string> warnings = new List<string>();
            ModuleCatalog.Apply(entry, m, settings, warnings);

            Assert.AreEqual("Contoso Example App", settings.Get(key, "Product").Value);
            Assert.AreEqual("CachedInstaller/ExampleVendor/ContosoExampleApp-2.4.1.msi", settings.Get(key, "File").Value);
            Assert.AreEqual("ExampleVendor Inc.", settings.Get(key, "Publisher").Value);
            Assert.IsNull(settings.Get(key, "Version"), "the instance matches the catalog");
            Assert.AreEqual("Example token", settings.Get(key, "Token").Credential, "a secret parameter is never set from a catalog");
            Assert.AreEqual("module-level.msi", settings.Get("CachedInstaller", "File").Value, "other keys unchanged");
            Assert.AreEqual(2, warnings.Count, String.Join(" | ", warnings));

            string error;
            Dictionary<string, string> values = UpdateModules.BuildParameterValues(m, "ContosoExampleApp", settings, n => n == "Example token" ? "pw" : null, out error);
            Assert.IsNull(error);
            Assert.AreEqual("msi", values["Type"]);

            UpdatePackage row = m.ToPackage(entry.Instance, entry.Label);
            Assert.AreEqual("module:CachedInstaller/ContosoExampleApp||Contoso Example App", row.ToArgument());
        }
    }
}
