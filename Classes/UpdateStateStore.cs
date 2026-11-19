// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateStateStore.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: State of the software updates per computer (installed and available
//          versions, last check, last upgrade result, operating system and its
//          type, TeamViewer ID), kept in a local file,
//          %LOCALAPPDATA%\LiteDeploy\updates.xml. Read when the Software
//          Updates window opens; written only when a value changed (versions,
//          status, results, errors): a check that finds nothing new does not
//          rewrite the file. LastChange is the time of the last such change.
//          A computer is found whatever the spelling of its name
//          (ComputerName.Key); states saved under two spellings are merged
//          when the file is read.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace LiteDeploy
{
    public class PackageUpdateState
    {
        [XmlAttribute] public String Id;
        [XmlAttribute] public String Status;          // WorkerProtocol.Status*
        [XmlAttribute] public String Installed;
        [XmlAttribute] public String Available;
        [XmlAttribute] public String LastResult;      // WorkerProtocol.Result*
        [XmlAttribute] public String LastCode;
        public String LastMessage;
        public DateTime? LastUpgrade;
    }

    public class ComputerUpdateState
    {
        [XmlAttribute] public String Name;
        /// <summary>When a value of this computer last changed (not every check: see the file comment).</summary>
        public DateTime? LastChange;
        public String LastError;
        /// <summary>End of the last check or update that reached the computer (worker "done" line); null: never.</summary>
        public DateTime? LastScan;
        /// <summary>Paused (or excluded): skipped by Check and Update until resumed.</summary>
        [XmlAttribute] public bool Paused;
        public String PauseReason;
        /// <summary>Updated outside business hours only (UserSettings.BusinessHours*).</summary>
        [XmlAttribute] public bool NightOnly;
        /// <summary>MAC addresses of its physical adapters (Wake-on-LAN) and last IPv4 addresses, space-separated.</summary>
        [XmlAttribute] public String Macs;
        [XmlAttribute] public String Ips;
        /// <summary>Windows caption, version and release, as the worker last reported it ("os"); null: never reported.</summary>
        [XmlAttribute] public String OsCaption;
        /// <summary>Operating system type, as the worker last reported it ("ostype": ServerPolicy.Type*); null: never reported.</summary>
        [XmlAttribute] public String OsType;
        /// <summary>Added by name in the grid (a computer that is not in Active Directory): kept in the list.</summary>
        [XmlAttribute] public bool AddedByName;
        /// <summary>Removed from the list by the user (a computer of Active Directory that is not wanted): not shown, never updated, until shown again.</summary>
        [XmlAttribute] public bool Hidden;
        /// <summary>TeamViewer ID, as the worker last reported it ("teamviewer"); null: not installed or never reported.</summary>
        [XmlAttribute] public String TeamViewerId;
        /// <summary>Users with a desktop on the computer at the last check, update or card (DOMAIN\user, comma-separated); null: unknown or nobody.</summary>
        [XmlAttribute] public String Users;
        [XmlElement("Package")] public List<PackageUpdateState> Packages = new List<PackageUpdateState>();

        public PackageUpdateState Find(String id)
        {
            return Packages.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when the last known type is a server or a domain controller.</summary>
        public bool IsServer
        {
            get
            {
                int type = ServerPolicy.ProductTypeOf(OsType);
                return type == ServerPolicy.ProductServer || type == ServerPolicy.ProductDomainController;
            }
        }

        /// <summary>The displayed name of an operating system type (ServerPolicy.Type*); empty when unknown.</summary>
        public static String OsTypeText(String osType)
        {
            switch (ServerPolicy.ProductTypeOf(osType))
            {
                case ServerPolicy.ProductWorkstation: return Localization.T("ComputerUpdateState.Workstation", "Workstation");
                case ServerPolicy.ProductServer: return Localization.T("ComputerUpdateState.Server", "Server");
                case ServerPolicy.ProductDomainController: return Localization.T("ComputerUpdateState.DomainController", "Domain controller");
                default: return "";
            }
        }
    }

    [XmlRoot("LiteDeployUpdates")]
    public class UpdateStateStore
    {
        [XmlElement("Computer")] public List<ComputerUpdateState> Computers = new List<ComputerUpdateState>();

        static public String DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "updates.xml"); }
        }

        /// <summary>The state of a computer, whatever the spelling of its name (ComputerName.Key); null when none.</summary>
        public ComputerUpdateState Find(String computer)
        {
            String key = ComputerName.Key(computer);
            if (key.Length == 0) return null;
            return Computers.FirstOrDefault(c => ComputerName.Key(c.Name) == key);
        }

        /// <summary>
        /// One state per computer: the states saved under several spellings of the same name ("PC01",
        /// "pc01.example.com") by earlier versions are merged (MergeStates). Returns the number of states removed.
        /// </summary>
        public int MergeDuplicates()
        {
            List<ComputerUpdateState> merged = new List<ComputerUpdateState>();
            int removed = 0;
            foreach (IGrouping<String, ComputerUpdateState> g in Computers.Where(c => c != null).GroupBy(c => ComputerName.Key(c.Name)))
            {
                if (g.Key.Length == 0 || g.Count() == 1) { merged.AddRange(g); continue; }
                merged.Add(MergeStates(g.ToList()));
                removed += g.Count() - 1;
            }
            if (removed == 0) return 0;
            merged.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Computers = merged;
            return removed;
        }

        /// <summary>
        /// The states of one computer saved under several names, as one: the most recent data (LastChange) wins
        /// for each value, the flags (paused, outside business hours only, added by name) are kept when any
        /// spelling has them (hidden too), every package known under any spelling is kept (the most recent one for each).
        /// The name is the short one when there is one ("PC01" rather than "PC01.example.com").
        /// </summary>
        public static ComputerUpdateState MergeStates(IList<ComputerUpdateState> states)
        {
            List<ComputerUpdateState> newest = states.Where(s => s != null)
                .OrderByDescending(s => s.LastChange ?? DateTime.MinValue).ToList();
            if (newest.Count == 0) return null;
            ComputerUpdateState first = newest[0];
            ComputerUpdateState paused = newest.FirstOrDefault(s => s.Paused);
            ComputerUpdateState result = new ComputerUpdateState
            {
                Name = (newest.FirstOrDefault(s => IsShortName(s.Name)) ?? first).Name.Trim(),
                LastChange = first.LastChange,
                LastError = first.LastError,
                Paused = paused != null,
                PauseReason = paused == null ? null : newest.Where(s => s.Paused).Select(s => s.PauseReason).FirstOrDefault(r => !String.IsNullOrEmpty(r)),
                NightOnly = newest.Any(s => s.NightOnly),
                AddedByName = newest.Any(s => s.AddedByName),
                Hidden = newest.Any(s => s.Hidden),
                Macs = FirstValue(newest, s => s.Macs),
                Ips = FirstValue(newest, s => s.Ips),
                OsCaption = FirstValue(newest, s => s.OsCaption),
                OsType = FirstValue(newest, s => s.OsType),
                TeamViewerId = FirstValue(newest, s => s.TeamViewerId),
                Users = FirstValue(newest, s => s.Users),
                LastScan = newest.Max(s => s.LastScan)
            };
            foreach (ComputerUpdateState s in newest)
            {
                foreach (PackageUpdateState p in s.Packages ?? new List<PackageUpdateState>())
                {
                    if (p != null && !String.IsNullOrEmpty(p.Id) && result.Find(p.Id) == null) result.Packages.Add(p);
                }
            }
            return result;
        }

        static bool IsShortName(String name)
        {
            return !String.IsNullOrWhiteSpace(name) && name.Trim().IndexOfAny(new[] { '.', '$', (char)0x5C, '/' }) < 0;
        }

        static String FirstValue(IEnumerable<ComputerUpdateState> newest, Func<ComputerUpdateState, String> value)
        {
            return newest.Select(value).FirstOrDefault(v => !String.IsNullOrEmpty(v));
        }

        public ComputerUpdateState GetOrAdd(String computer)
        {
            ComputerUpdateState c = Find(computer);
            if (c == null)
            {
                c = new ComputerUpdateState { Name = computer };
                Computers.Add(c);
                Computers.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            return c;
        }

        /// <summary>
        /// Records the result of one worker run on a computer: package states, upgrade outcomes,
        /// errors. A run without any worker message (PsExec could not reach the computer) records
        /// <paramref name="failure"/> as the computer's error and keeps the last known versions.
        /// </summary>
        internal void Apply(String computer, IList<WorkerMessage> messages, String failure, DateTime now)
        {
            ComputerUpdateState c = GetOrAdd(computer);
            String before = Fingerprint(c);
            List<String> errors = new List<String>();
            if (!String.IsNullOrEmpty(failure) && messages.Count == 0) errors.Add(failure);

            foreach (WorkerMessage m in messages)
            {
                String id = m["id"];
                switch (m.Type)
                {
                    case WorkerProtocol.TypePackage:
                        if (String.IsNullOrEmpty(id)) break;
                        PackageUpdateState p = GetPackage(c, id);
                        p.Status = m["status"];
                        if (p.Status != WorkerProtocol.StatusError)
                        {
                            p.Installed = m["installed"];
                            p.Available = m["available"];
                        }
                        else
                        {
                            errors.Add(id + ": " + m["message"]);
                        }
                        break;
                    case WorkerProtocol.TypeUpgrade:
                        if (String.IsNullOrEmpty(id)) break;
                        // Skipped (up to date, not installed): the package state says it; the last
                        // real upgrade result is kept
                        if (m["result"] == WorkerProtocol.ResultSkipped) break;
                        PackageUpdateState u = GetPackage(c, id);
                        u.LastResult = m["result"];
                        u.LastCode = m["code"];
                        u.LastMessage = m["message"];
                        u.LastUpgrade = now;
                        if (u.LastResult == WorkerProtocol.ResultFailed) errors.Add(id + ": " + m["message"]);
                        break;
                    case "start":
                    case "info":
                        // Kept for Wake-on-LAN: the adapters of a computer that is now off are still known
                        if (!String.IsNullOrEmpty(m["macs"])) c.Macs = m["macs"];
                        if (!String.IsNullOrEmpty(m["ips"])) c.Ips = m["ips"];
                        // The card says it even when empty (TeamViewer removed); older workers do not send it
                        if (m.Type == "info" && m.Fields.ContainsKey("teamviewer"))
                            c.TeamViewerId = String.IsNullOrWhiteSpace(m["teamviewer"]) ? null : m["teamviewer"].Trim();
                        // Users with a desktop: the card ("info") and the start line of a check or update
                        // (newer workers); empty: nobody logged on
                        if (m.Fields.ContainsKey("users"))
                            c.Users = String.IsNullOrWhiteSpace(m["users"]) ? null : m["users"].Trim();
                        ApplyOs(c, m);
                        break;
                    case WorkerProtocol.TypeDone:
                        ApplyOs(c, m);
                        // The worker ran to its end on the computer: last scan (not part of the fingerprint,
                        // so a check that finds nothing new does not change LastChange)
                        c.LastScan = now;
                        break;
                    case WorkerProtocol.TypeError:
                        errors.Add(m["message"]);
                        break;
                }
            }
            c.LastError = errors.Count == 0 ? null : String.Join(" | ", errors);
            if (Fingerprint(c) != before) c.LastChange = now;
        }

        // Operating system of the info card and of the done line (older workers: no such field, kept as is)
        static void ApplyOs(ComputerUpdateState c, WorkerMessage m)
        {
            if (!String.IsNullOrEmpty(m["os"])) c.OsCaption = m["os"];
            if (!String.IsNullOrEmpty(m["ostype"])) c.OsType = m["ostype"];
        }

        // Everything but LastChange and the upgrade times
        static String Fingerprint(ComputerUpdateState c)
        {
            StringBuilder sb = new StringBuilder(c.LastError ?? "");
            foreach (PackageUpdateState p in c.Packages)
                sb.Append('\n').Append(String.Join("\t", p.Id, p.Status, p.Installed, p.Available, p.LastResult, p.LastCode, p.LastMessage));
            return sb.ToString();
        }

        static PackageUpdateState GetPackage(ComputerUpdateState c, String id)
        {
            PackageUpdateState p = c.Find(id);
            if (p == null)
            {
                p = new PackageUpdateState { Id = id };
                c.Packages.Add(p);
            }
            return p;
        }

        // ---- File -----------------------------------------------------------------

        static public UpdateStateStore Load(String path)
        {
            try
            {
                if (File.Exists(path))
                {
                    UpdateStateStore store;
                    using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        store = (UpdateStateStore)new XmlSerializer(typeof(UpdateStateStore)).Deserialize(file) ?? new UpdateStateStore();
                    }
                    // Written merged at the next change of the file
                    store.MergeDuplicates();
                    return store;
                }
            }
            catch (Exception)
            {
                // Unreadable file: start empty (it is rewritten at the next change)
            }
            return new UpdateStateStore();
        }

        public String Serialize()
        {
            XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlWriter w = XmlWriter.Create(ms, settings))
                {
                    XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                    ns.Add("", "");
                    new XmlSerializer(typeof(UpdateStateStore)).Serialize(w, this, ns);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Writes the file only when its content would change. Returns true when it was written.</summary>
        public bool SaveIfChanged(String path)
        {
            String content = Serialize();
            try
            {
                if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content) return false;
            }
            catch (IOException) { }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            String temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            return true;
        }
    }
}
