// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleCatalog.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Catalog of the instances of a module (docs/modules.md), a JSON file
//          the administrator keeps, by default
//          %LOCALAPPDATA%\LiteDeploy\catalog.local.json:
//            { "module": "CachedInstaller",
//              "products": [ { "Instance": "ContosoExampleApp",
//                              "Product": "Contoso Example App", ... } ] }
//          Package list > Import from catalog... creates one row per product
//          ("module:<module>/<Instance>") and its parameter values in
//          module-settings.xml. Only LiteDeploy reads the file, never the
//          computers. Pure logic (parsing, validation, import), unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LiteDeploy
{
    /// <summary>One product of a catalog: the instance name and the values of its parameters.</summary>
    internal sealed class ModuleCatalogEntry
    {
        public String Instance;
        /// <summary>Parameter name -> value (names case-insensitive).</summary>
        public Dictionary<String, String> Values = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Label of the package row: the Product value, else the instance name.</summary>
        public String Label
        {
            get
            {
                String product;
                return Values.TryGetValue("Product", out product) && !String.IsNullOrWhiteSpace(product) ? product.Trim() : Instance;
            }
        }
    }

    internal sealed class ModuleCatalog
    {
        /// <summary>Module of the products when the file does not say ("module").</summary>
        public const String DefaultModule = "CachedInstaller";
        /// <summary>Key of the instance name in a product.</summary>
        public const String InstanceKey = "Instance";

        public String Module = DefaultModule;
        public List<ModuleCatalogEntry> Entries = new List<ModuleCatalogEntry>();
        /// <summary>Why products or values were skipped (names only).</summary>
        public List<String> Errors = new List<String>();

        /// <summary>Default location: %LOCALAPPDATA%\LiteDeploy\catalog.local.json.</summary>
        static public String LocalPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "catalog.local.json"); }
        }

        static public ModuleCatalog Load(String path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>
        /// Reads the JSON of a catalog. A product without a valid instance name, a duplicate instance,
        /// a value that is not a string or number, or an invalid parameter name is skipped and
        /// reported in Errors; invalid JSON gives an empty catalog with one error.
        /// </summary>
        static public ModuleCatalog Parse(String json)
        {
            ModuleCatalog catalog = new ModuleCatalog();
            XElement root;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes((json ?? "").TrimStart('\uFEFF'));
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
                    root = XElement.Load(reader);
            }
            catch (Exception ex) when (ex is XmlException || ex is ArgumentException || ex is InvalidOperationException)
            {
                catalog.Errors.Add(Localization.F("ModuleCatalog.InvalidJson", "Invalid JSON: {0}", ex.Message));
                return catalog;
            }
            if (Type(root) != "object")
            {
                catalog.Errors.Add(Localization.T("ModuleCatalog.NoProducts", "Invalid catalog: a JSON object with an array \"products\" is expected."));
                return catalog;
            }

            XElement module = Member(root, "module");
            if (module != null)
            {
                String name = module.Value.Trim();
                if (Type(module) != "string" || !UpdatePackage.IsValidModuleName(name))
                {
                    catalog.Errors.Add(Localization.F("ModuleCatalog.InvalidModule", "Invalid module name: {0}", name));
                    return catalog;
                }
                catalog.Module = name;
            }

            XElement products = Member(root, "products");
            if (products == null || Type(products) != "array")
            {
                catalog.Errors.Add(Localization.T("ModuleCatalog.NoProducts", "Invalid catalog: a JSON object with an array \"products\" is expected."));
                return catalog;
            }

            int index = 0;
            foreach (XElement product in products.Elements())
            {
                index++;
                if (Type(product) != "object")
                {
                    catalog.Errors.Add(Localization.F("ModuleCatalog.NotAnObject", "Product {0}: an object is expected.", index));
                    continue;
                }
                XElement instanceElement = Member(product, InstanceKey);
                String instance = instanceElement == null ? null : instanceElement.Value.Trim();
                if (!UpdatePackage.IsValidInstanceName(instance))
                {
                    catalog.Errors.Add(Localization.F("ModuleCatalog.InvalidInstance", "Product {0}: invalid or missing \"Instance\" (letters, digits and . - _ only): {1}", index, instance ?? ""));
                    continue;
                }
                if (catalog.Entries.Any(e => String.Equals(e.Instance, instance, StringComparison.OrdinalIgnoreCase)))
                {
                    catalog.Errors.Add(Localization.F("ModuleCatalog.Duplicate", "Product {0}: instance {1} listed twice, skipped.", index, instance));
                    continue;
                }

                ModuleCatalogEntry entry = new ModuleCatalogEntry { Instance = instance };
                foreach (XElement value in product.Elements())
                {
                    String name = MemberName(value);
                    if (String.Equals(name, InstanceKey, StringComparison.OrdinalIgnoreCase)) continue;
                    String type = Type(value);
                    if (type == "null") continue;
                    if (!ModuleParameters.IsValidName(name))
                    {
                        catalog.Errors.Add(Localization.F("ModuleCatalog.InvalidName", "{0}: invalid parameter name {1}, skipped.", instance, name));
                        continue;
                    }
                    if (type != "string" && type != "number" && type != "boolean")
                    {
                        catalog.Errors.Add(Localization.F("ModuleCatalog.NotAString", "{0}: the value of {1} must be a string, skipped.", instance, name));
                        continue;
                    }
                    if (!entry.Values.ContainsKey(name)) entry.Values[name] = value.Value;
                }
                catalog.Entries.Add(entry);
            }
            return catalog;
        }

        // JSON type of an element of the JSON-to-XML mapping (string when not said)
        static String Type(XElement e)
        {
            String type = (String)e.Attribute("type");
            return String.IsNullOrEmpty(type) ? "string" : type;
        }

        // A member name that is not a valid XML name is mapped to <a:item item="name">
        static String MemberName(XElement e)
        {
            String item = (String)e.Attribute("item");
            return item ?? e.Name.LocalName;
        }

        static XElement Member(XElement obj, String name)
        {
            return obj.Elements().FirstOrDefault(e => String.Equals(MemberName(e), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Writes the values of a product to the settings of its instance ("Module/Instance"): every
        /// parameter of the module header is set from the catalog or cleared, so the instance matches
        /// the catalog. A value for a secret parameter (a catalog never holds a secret) or for a
        /// parameter the module does not declare is skipped and reported in <paramref name="warnings"/>.
        /// </summary>
        static public void Apply(ModuleCatalogEntry entry, UpdateModule module, ModuleSettings settings, List<String> warnings)
        {
            String key = ModuleSettings.Key(module.Name, entry.Instance);
            foreach (KeyValuePair<String, String> v in entry.Values)
            {
                ModuleParameter declared = module.Parameters.FirstOrDefault(p => String.Equals(p.Name, v.Key, StringComparison.OrdinalIgnoreCase));
                if (declared == null)
                {
                    if (warnings != null) warnings.Add(Localization.F("ModuleCatalog.Undeclared", "{0}: {1} is not a parameter of the module {2}, skipped.", entry.Instance, v.Key, module.Name));
                }
                else if (declared.Secret)
                {
                    if (warnings != null) warnings.Add(Localization.F("ModuleCatalog.Secret", "{0}: {1} is a secret parameter, never read from a catalog: set it in Parameters... (skipped).", entry.Instance, v.Key));
                }
            }
            foreach (ModuleParameter p in module.Parameters.Where(x => !x.Secret))
            {
                String value;
                entry.Values.TryGetValue(p.Name, out value);
                if (value != null && value.Length > ModuleParameters.MaxValueLength)
                {
                    if (warnings != null) warnings.Add(Localization.F("ModuleCatalog.TooLong", "{0}: the value of {1} is too long, skipped.", entry.Instance, p.Name));
                    continue;
                }
                settings.SetValue(key, p.Name, value == null ? null : value.Trim());
            }
        }
    }
}
