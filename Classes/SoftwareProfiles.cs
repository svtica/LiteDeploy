// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareProfiles.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Profiles of the software catalog: named selections of the packages
//          of the catalog (packages.txt), e.g. "Base" for the software of
//          every computer. The active profile of the main window, and the
//          profile of a scheduled update, limit Check, Update and Install to
//          its packages (faster, and no accidental mass update). "All" (empty
//          name) is the whole catalog. Saved in
//          %LOCALAPPDATA%\LiteDeploy\profiles.xml; pure logic unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace LiteDeploy
{
    public class SoftwareProfile
    {
        [XmlAttribute] public String Name;
        /// <summary>Package Ids of the catalog (winget Ids or module:Name[/Instance]).</summary>
        [XmlElement("Package")] public List<String> Packages = new List<String>();

        public bool Contains(String id)
        {
            return Packages.Any(p => String.Equals(p, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    [XmlRoot("SoftwareProfiles")]
    public class SoftwareProfiles
    {
        [XmlElement("Profile")] public List<SoftwareProfile> Items = new List<SoftwareProfile>();

        static public String DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "profiles.xml"); }
        }

        /// <summary>The profiles of the file; none when it is absent or unreadable.</summary>
        static public SoftwareProfiles Load(String path = null)
        {
            path = path ?? DefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    using (FileStream file = File.OpenRead(path))
                    {
                        SoftwareProfiles loaded = (SoftwareProfiles)new XmlSerializer(typeof(SoftwareProfiles)).Deserialize(file);
                        loaded.Items = loaded.Items.Where(p => IsValidName(p.Name)).GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First()).ToList();
                        foreach (SoftwareProfile p in loaded.Items)
                        {
                            p.Name = p.Name.Trim();
                            p.Packages = (p.Packages ?? new List<String>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        }
                        return loaded;
                    }
                }
            }
            catch (Exception) { }
            return new SoftwareProfiles();
        }

        public void Save(String path = null)
        {
            path = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            String temp = path + ".tmp";
            using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
            {
                new XmlSerializer(typeof(SoftwareProfiles)).Serialize(writer, this);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public SoftwareProfile Find(String name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            return Items.FirstOrDefault(p => String.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A profile name: letters, digits, spaces and . - _ + ( ), 1 to 40 characters.</summary>
        public static bool IsValidName(String name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            String n = name.Trim();
            if (n.Length > 40) return false;
            return n.All(c => Char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '-' || c == '_' || c == '+' || c == '(' || c == ')');
        }

        /// <summary>
        /// The packages of a profile, in the order of the catalog: the whole catalog for "All" (empty name), the
        /// packages of the profile that are still in the catalog otherwise. <paramref name="found"/>: false when the
        /// profile does not exist (then nothing: never the whole catalog by mistake).
        /// </summary>
        internal List<UpdatePackage> Filter(IEnumerable<UpdatePackage> catalog, String profile, out bool found)
        {
            List<UpdatePackage> all = (catalog ?? Enumerable.Empty<UpdatePackage>()).ToList();
            if (String.IsNullOrWhiteSpace(profile))
            {
                found = true;
                return all;
            }
            SoftwareProfile p = Find(profile);
            found = p != null;
            if (p == null) return new List<UpdatePackage>();
            return all.Where(x => p.Contains(x.Id)).ToList();
        }

        /// <summary>Ids of the profiles that are no longer in the catalog are dropped (the catalog was edited).</summary>
        internal void KeepOnly(IEnumerable<UpdatePackage> catalog)
        {
            HashSet<String> ids = new HashSet<String>((catalog ?? Enumerable.Empty<UpdatePackage>()).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            foreach (SoftwareProfile p in Items) p.Packages = p.Packages.Where(ids.Contains).ToList();
        }
    }
}
