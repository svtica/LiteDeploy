// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleSettings.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Values of the parameters of the update modules, set by the
//          administrator (Package list > Parameters...) and saved in
//          %LOCALAPPDATA%\LiteDeploy\module-settings.xml:
//            <ModuleSettings version="1">
//              <Module name="Example">
//                <Parameter name="Environment" value="Production" />
//                <Parameter name="Token" credential="Example token" />
//              </Module>
//            </ModuleSettings>
//          A secret parameter holds the NAME of a stored credential (Tools >
//          Credentials), never the secret: its password is read at run time.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LiteDeploy
{
    /// <summary>The setting of one parameter: a plain value, or the name of a credential (secret).</summary>
    internal sealed class ModuleParameterSetting
    {
        public String Value;
        public String Credential;
    }

    internal sealed class ModuleSettings
    {
        // Module name -> parameter name -> setting (names case-insensitive)
        readonly Dictionary<String, Dictionary<String, ModuleParameterSetting>> modules =
            new Dictionary<String, Dictionary<String, ModuleParameterSetting>>(StringComparer.OrdinalIgnoreCase);

        static public String FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "module-settings.xml"); }
        }

        /// <summary>The setting of a parameter; null when none.</summary>
        public ModuleParameterSetting Get(String module, String parameter)
        {
            Dictionary<String, ModuleParameterSetting> m;
            ModuleParameterSetting s;
            if (module == null || parameter == null || !modules.TryGetValue(module, out m)) return null;
            return m.TryGetValue(parameter, out s) ? s : null;
        }

        /// <summary>Plain value of a parameter (null: none).</summary>
        public void SetValue(String module, String parameter, String value)
        {
            Set(module, parameter, String.IsNullOrEmpty(value) ? null : new ModuleParameterSetting { Value = value });
        }

        /// <summary>Secret parameter: the name of the stored credential (null: none).</summary>
        public void SetCredential(String module, String parameter, String credential)
        {
            Set(module, parameter, String.IsNullOrWhiteSpace(credential) ? null : new ModuleParameterSetting { Credential = credential.Trim() });
        }

        void Set(String module, String parameter, ModuleParameterSetting setting)
        {
            if (!UpdatePackage.IsValidId(UpdatePackage.ModulePrefix + module) || !ModuleParameters.IsValidName(parameter))
                throw new ArgumentException("Invalid module or parameter name.");
            Dictionary<String, ModuleParameterSetting> m;
            if (!modules.TryGetValue(module, out m))
            {
                if (setting == null) return;
                m = new Dictionary<String, ModuleParameterSetting>(StringComparer.OrdinalIgnoreCase);
                modules[module] = m;
            }
            if (setting == null) m.Remove(parameter); else m[parameter] = setting;
            if (m.Count == 0) modules.Remove(module);
        }

        /// <summary>Names of the modules that have settings.</summary>
        public IEnumerable<String> Modules
        {
            get { return modules.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase); }
        }

        // ---- File ----------------------------------------------------------------------

        static public ModuleSettings Load()
        {
            return Load(FilePath);
        }

        /// <summary>The saved settings; empty when the file is missing or unreadable.</summary>
        static public ModuleSettings Load(String path)
        {
            try
            {
                return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new ModuleSettings();
            }
            catch (IOException) { return new ModuleSettings(); }
            catch (UnauthorizedAccessException) { return new ModuleSettings(); }
        }

        public void Save()
        {
            Save(FilePath);
        }

        public void Save(String path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToXml(), new UTF8Encoding(false));
        }

        /// <summary>Reads the XML of the file; invalid entries are skipped, unreadable XML gives empty settings.</summary>
        static public ModuleSettings Parse(String xml)
        {
            ModuleSettings settings = new ModuleSettings();
            XDocument doc;
            try { doc = XDocument.Parse(xml ?? ""); }
            catch (XmlException) { return settings; }
            if (doc.Root == null || doc.Root.Name.LocalName != "ModuleSettings") return settings;
            foreach (XElement module in doc.Root.Elements("Module"))
            {
                String name = (String)module.Attribute("name");
                if (!UpdatePackage.IsValidId(UpdatePackage.ModulePrefix + name)) continue;
                foreach (XElement parameter in module.Elements("Parameter"))
                {
                    String p = (String)parameter.Attribute("name");
                    if (!ModuleParameters.IsValidName(p)) continue;
                    String credential = (String)parameter.Attribute("credential");
                    String value = (String)parameter.Attribute("value");
                    if (!String.IsNullOrWhiteSpace(credential)) settings.SetCredential(name, p, credential);
                    else if (!String.IsNullOrEmpty(value)) settings.SetValue(name, p, value);
                }
            }
            return settings;
        }

        public String ToXml()
        {
            XElement root = new XElement("ModuleSettings", new XAttribute("version", "1"));
            foreach (String module in Modules)
            {
                XElement m = new XElement("Module", new XAttribute("name", module));
                foreach (KeyValuePair<String, ModuleParameterSetting> p in modules[module].OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    XElement e = new XElement("Parameter", new XAttribute("name", p.Key));
                    if (p.Value.Credential != null) e.Add(new XAttribute("credential", p.Value.Credential));
                    else e.Add(new XAttribute("value", p.Value.Value ?? ""));
                    m.Add(e);
                }
                root.Add(m);
            }
            XDocument doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XComment(" LiteDeploy - values of the parameters of the update modules (docs/modules.md). A secret parameter holds the name of a credential (Tools > Credentials), never the secret. "),
                root);
            StringBuilder sb = new StringBuilder();
            using (StringWriter w = new Utf8StringWriter(sb))
                doc.Save(w);
            return sb.ToString();
        }

        sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter(StringBuilder sb) : base(sb, System.Globalization.CultureInfo.InvariantCulture) { }
            public override Encoding Encoding { get { return new UTF8Encoding(false); } }
        }
    }
}
