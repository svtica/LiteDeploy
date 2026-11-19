// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareCatalogFile.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Export and import of the software catalog (Software catalog
//          window): the packages (Id, processes to close, label) and the
//          profiles, as one JSON document to share with the team or keep:
//            { "format": "LiteDeploy.SoftwareCatalog", "version": 1,
//              "packages": [ { "id": "Example.App", "processes": ["app"],
//                              "label": "Example App" } ],
//              "profiles": [ { "name": "Base", "packages": ["Example.App"] } ] }
//          An import adds what is missing and never removes anything: the
//          packages not in the catalog yet, the profiles not there yet, and
//          the packages of a profile that has the same name. The parameters
//          of the modules are not part of it (Import instances...). Pure
//          logic unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LiteDeploy
{
    [DataContract]
    public sealed class SoftwareCatalogFile
    {
        public const String FormatName = "LiteDeploy.SoftwareCatalog";

        [DataMember(Name = "format", Order = 0)] public String Format = FormatName;
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "packages", Order = 2)] public List<CatalogPackage> Packages = new List<CatalogPackage>();
        [DataMember(Name = "profiles", Order = 3)] public List<CatalogProfile> Profiles = new List<CatalogProfile>();

        [DataContract]
        public sealed class CatalogPackage
        {
            [DataMember(Name = "id", Order = 0)] public String Id;
            [DataMember(Name = "processes", Order = 1)] public List<String> Processes = new List<String>();
            [DataMember(Name = "label", Order = 2, EmitDefaultValue = false)] public String Label;
        }

        [DataContract]
        public sealed class CatalogProfile
        {
            [DataMember(Name = "name", Order = 0)] public String Name;
            [DataMember(Name = "packages", Order = 1)] public List<String> Packages = new List<String>();
        }

        /// <summary>The document of a catalog and its profiles.</summary>
        public static SoftwareCatalogFile From(IEnumerable<UpdatePackage> packages, SoftwareProfiles profiles)
        {
            SoftwareCatalogFile f = new SoftwareCatalogFile();
            foreach (UpdatePackage p in packages ?? Enumerable.Empty<UpdatePackage>())
                f.Packages.Add(new CatalogPackage { Id = p.Id, Processes = p.Processes.ToList(), Label = p.Label });
            foreach (SoftwareProfile p in (profiles ?? new SoftwareProfiles()).Items)
                f.Profiles.Add(new CatalogProfile { Name = p.Name, Packages = p.Packages.ToList() });
            return f;
        }

        public String ToJson()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(SoftwareCatalogFile)).WriteObject(writer, this);
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>A catalog document; throws InvalidDataException when it is not one.</summary>
        public static SoftwareCatalogFile Parse(String json)
        {
            SoftwareCatalogFile f;
            try
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                {
                    f = (SoftwareCatalogFile)new DataContractJsonSerializer(typeof(SoftwareCatalogFile)).ReadObject(stream);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Not a software catalog: " + ex.Message, ex);
            }
            if (f == null || !String.Equals(f.Format, FormatName, StringComparison.Ordinal))
                throw new InvalidDataException("Not a software catalog (format \"" + FormatName + "\" expected).");
            f.Packages = f.Packages ?? new List<CatalogPackage>();
            f.Profiles = f.Profiles ?? new List<CatalogProfile>();
            return f;
        }

        /// <summary>What an import changed and left out.</summary>
        public sealed class MergeResult
        {
            public int PackagesAdded, ProfilesAdded, ProfilePackagesAdded;
            public List<String> Invalid = new List<String>();
        }

        /// <summary>
        /// Adds to <paramref name="catalog"/> and <paramref name="profiles"/> what the document has and they do not
        /// (never removes or changes an existing package). Invalid packages and profile names are left out and named
        /// in the result; a profile keeps only the packages of the catalog.
        /// </summary>
        public MergeResult MergeInto(List<UpdatePackage> catalog, SoftwareProfiles profiles)
        {
            MergeResult result = new MergeResult();
            foreach (CatalogPackage cp in Packages)
            {
                if (cp == null || String.IsNullOrWhiteSpace(cp.Id)) continue;
                String line = cp.Id.Trim() + " | " + String.Join(", ", cp.Processes ?? new List<String>()) + " | " + (cp.Label ?? "").Replace("|", " ");
                UpdatePackage p = UpdatePackage.Parse(line);
                if (p == null) { result.Invalid.Add(cp.Id.Trim()); continue; }
                if (catalog.Any(x => String.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))) continue;
                catalog.Add(p);
                result.PackagesAdded++;
            }
            HashSet<String> ids = new HashSet<String>(catalog.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            foreach (CatalogProfile cp in Profiles)
            {
                if (cp == null) continue;
                if (!SoftwareProfiles.IsValidName(cp.Name)) { result.Invalid.Add(cp.Name ?? ""); continue; }
                SoftwareProfile target = profiles.Find(cp.Name);
                if (target == null)
                {
                    target = new SoftwareProfile { Name = cp.Name.Trim() };
                    profiles.Items.Add(target);
                    result.ProfilesAdded++;
                }
                foreach (String id in (cp.Packages ?? new List<String>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()))
                {
                    if (!ids.Contains(id) || target.Contains(id)) continue;
                    // The spelling of the catalog
                    target.Packages.Add(catalog.First(x => String.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).Id);
                    result.ProfilePackagesAdded++;
                }
            }
            return result;
        }
    }
}
