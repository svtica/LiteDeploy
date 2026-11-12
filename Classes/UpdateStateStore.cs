// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateStateStore.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: State of the software updates per computer (installed and available
//          versions, last check, last upgrade result), kept in a local file,
//          %LOCALAPPDATA%\LiteDeploy\updates.xml. Read when the Software
//          Updates window opens; written only when a value changed (versions,
//          status, results, errors): a check that finds nothing new does not
//          rewrite the file. LastChange is the time of the last such change.
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
        /// <summary>Paused (or excluded): skipped by Check and Update until resumed.</summary>
        [XmlAttribute] public bool Paused;
        public String PauseReason;
        /// <summary>Updated outside business hours only (UserSettings.BusinessHours*).</summary>
        [XmlAttribute] public bool NightOnly;
        /// <summary>MAC addresses of its physical adapters (Wake-on-LAN) and last IPv4 addresses, space-separated.</summary>
        [XmlAttribute] public String Macs;
        [XmlAttribute] public String Ips;
        [XmlElement("Package")] public List<PackageUpdateState> Packages = new List<PackageUpdateState>();

        public PackageUpdateState Find(String id)
        {
            return Packages.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
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

        public ComputerUpdateState Find(String computer)
        {
            return Computers.FirstOrDefault(c => String.Equals(c.Name, computer, StringComparison.OrdinalIgnoreCase));
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
                        break;
                    case WorkerProtocol.TypeError:
                        errors.Add(m["message"]);
                        break;
                }
            }
            c.LastError = errors.Count == 0 ? null : String.Join(" | ", errors);
            if (Fingerprint(c) != before) c.LastChange = now;
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
                    using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return (UpdateStateStore)new XmlSerializer(typeof(UpdateStateStore)).Deserialize(file) ?? new UpdateStateStore();
                    }
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
