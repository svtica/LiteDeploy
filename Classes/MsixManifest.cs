// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MsixManifest.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Identity of an MSIX package or bundle (Name, Publisher, Version),
//          read from its AppxManifest.xml or AppxMetadata/AppxBundleManifest.xml
//          without extracting it, as the MsixProvision module does: for a
//          bundle, the version is the one of its newest application package.
//          Used by Tools > Web versions... on the administrator's computer.
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace LiteDeploy
{
    internal sealed class MsixManifest
    {
        public String Name;
        public String Publisher;
        public String Version;

        /// <summary>Identity of the package file; throws when it has no manifest or no identity.</summary>
        static public MsixManifest Read(String path)
        {
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                bool bundle = false;
                ZipArchiveEntry entry = zip.GetEntry("AppxManifest.xml");
                if (entry == null) { entry = zip.GetEntry("AppxMetadata/AppxBundleManifest.xml"); bundle = true; }
                if (entry == null) throw new InvalidDataException("No package manifest in " + Path.GetFileName(path));
                using (Stream s = entry.Open())
                using (StreamReader reader = new StreamReader(s))
                    return Parse(reader.ReadToEnd(), bundle);
            }
        }

        /// <summary>Identity from the XML of a manifest. Pure logic, unit-tested.</summary>
        static public MsixManifest Parse(String xml, bool bundle)
        {
            XElement root = XDocument.Parse(xml).Root;
            XElement identity = root == null ? null : root.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
            String name = identity == null ? null : (String)identity.Attribute("Name");
            String version = identity == null ? null : (String)identity.Attribute("Version");
            if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(version)) throw new InvalidDataException("No identity in the package manifest");
            MsixManifest m = new MsixManifest { Name = name, Publisher = (String)identity.Attribute("Publisher"), Version = version };
            if (bundle)
            {
                // The provisioned version is the one of the application package, not of the bundle
                Version newest = root.Descendants().Where(e => e.Name.LocalName == "Package" && (String)e.Attribute("Type") == "application")
                    .Select(e => { Version v; return System.Version.TryParse((String)e.Attribute("Version"), out v) ? v : null; })
                    .Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();
                if (newest != null) m.Version = newest.ToString();
            }
            return m;
        }
    }
}
