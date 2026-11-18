// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    AdComputer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: One computer of the Active Directory inventory (name, operating
//          system, last logon, disabled account, organizational unit), as
//          cached in adcache.xml, and the pure parsing of the LDAP attributes
//          it comes from (unit-tested on fake property values).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Serialization;

namespace LiteDeploy
{
    public class AdComputer
    {
        /// <summary>userAccountControl flag of a disabled account (ACCOUNTDISABLE).</summary>
        public const int AccountDisabled = 0x2;

        /// <summary>Escape character of distinguished names (backslash).</summary>
        const char Escape = (char)0x5C;

        [XmlAttribute] public String Name;
        [XmlAttribute] public String OperatingSystem;
        [XmlAttribute] public String OperatingSystemVersion;
        /// <summary>Distinguished name of the container of the computer (its OU), e.g. "OU=Workstations,DC=example,DC=com".</summary>
        [XmlAttribute] public String Ou;
        [XmlAttribute] public bool Disabled;

        /// <summary>lastLogonTimestamp (replicated, up to about two weeks late), local time; null: never or unknown.</summary>
        [XmlIgnore] public DateTime? LastLogon;

        /// <summary>LastLogon in the file (ISO 8601, round-trip); absent when unknown.</summary>
        [XmlAttribute("LastLogon")]
        public String LastLogonText
        {
            get { return LastLogon.HasValue ? LastLogon.Value.ToString("o", CultureInfo.InvariantCulture) : null; }
            set
            {
                DateTime parsed;
                LastLogon = !String.IsNullOrEmpty(value) && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)
                    ? parsed
                    : (DateTime?)null;
            }
        }

        /// <summary>True for a server operating system ("Windows Server 2022 Standard"); unknown or empty: not a server.</summary>
        public bool IsServer { get { return IsServerOs(OperatingSystem); } }

        // ---- Pure parsing -------------------------------------------------------------

        /// <summary>True when the operating system name contains "Server"; unknown or empty: false.</summary>
        public static bool IsServerOs(String operatingSystem)
        {
            return !String.IsNullOrEmpty(operatingSystem) && operatingSystem.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// A computer from the LDAP attributes of a search result. <paramref name="first"/> returns the first
        /// value of an attribute (lower-case name, as DirectorySearcher returns them), or null when absent.
        /// Null when there is no name.
        /// </summary>
        public static AdComputer FromProperties(Func<String, Object> first)
        {
            String name = AsString(first("name"));
            if (String.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) name = name.Substring(3);
            return new AdComputer
            {
                Name = name,
                OperatingSystem = Trimmed(first("operatingsystem")),
                OperatingSystemVersion = Trimmed(first("operatingsystemversion")),
                LastLogon = ParseFileTime(first("lastlogontimestamp")),
                Disabled = IsDisabled(first("useraccountcontrol")),
                Ou = ParentContainer(AsString(first("distinguishedname")))
            };
        }

        /// <summary>
        /// A large-integer date of Active Directory (FILETIME, 100 ns since 1601 UTC) as local time; null for
        /// 0, "never" (Int64.MaxValue), an unreadable value or null.
        /// </summary>
        public static DateTime? ParseFileTime(Object value)
        {
            if (value == null) return null;
            if (value is DateTime) return ((DateTime)value).ToLocalTime();
            long ticks;
            if (value is long) ticks = (long)value;
            else if (value is int) ticks = (int)value;
            else if (!Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)) return null;
            if (ticks <= 0 || ticks == Int64.MaxValue) return null;
            try { return DateTime.FromFileTimeUtc(ticks).ToLocalTime(); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        /// <summary>True when userAccountControl has the ACCOUNTDISABLE flag (bit 2); unknown: false.</summary>
        public static bool IsDisabled(Object userAccountControl)
        {
            if (userAccountControl == null) return false;
            long flags;
            if (userAccountControl is int) flags = (int)userAccountControl;
            else if (userAccountControl is long) flags = (long)userAccountControl;
            else if (!Int64.TryParse(Convert.ToString(userAccountControl, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out flags)) return false;
            return (flags & AccountDisabled) != 0;
        }

        /// <summary>
        /// The container of an object from its distinguished name: "CN=PC01,OU=Workstations,DC=example,DC=com"
        /// gives "OU=Workstations,DC=example,DC=com" (an escaped comma "\," stays in the first part). Null when
        /// there is no parent.
        /// </summary>
        public static String ParentContainer(String distinguishedName)
        {
            if (String.IsNullOrEmpty(distinguishedName)) return null;
            for (int i = 0; i < distinguishedName.Length; i++)
            {
                if (distinguishedName[i] == Escape) { i++; continue; }   // escaped character
                if (distinguishedName[i] == ',')
                {
                    String parent = distinguishedName.Substring(i + 1).Trim();
                    return parent.Length > 0 ? parent : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Short, readable form of an OU: the OU names from the top, "OU=Laptops,OU=Workstations,DC=example,DC=com"
        /// gives "Workstations/Laptops"; a container without OU ("CN=Computers,DC=...") gives its name.
        /// </summary>
        public static String ShortOu(String ou)
        {
            if (String.IsNullOrEmpty(ou)) return "";
            StringBuilder path = new StringBuilder();
            String first = null;
            foreach (String part in SplitDn(ou))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                String type = part.Substring(0, eq).Trim();
                String value = part.Substring(eq + 1).Trim().Replace(Escape + ",", ",");
                if (first == null && !type.Equals("DC", StringComparison.OrdinalIgnoreCase)) first = value;
                if (!type.Equals("OU", StringComparison.OrdinalIgnoreCase)) continue;
                path.Insert(0, path.Length > 0 ? value + "/" : value);
            }
            return path.Length > 0 ? path.ToString() : first ?? ou;
        }

        static List<String> SplitDn(String dn)
        {
            List<String> parts = new List<String>();
            int start = 0;
            for (int i = 0; i < dn.Length; i++)
            {
                if (dn[i] == Escape) { i++; continue; }
                if (dn[i] == ',') { parts.Add(dn.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(dn.Substring(start));
            return parts;
        }

        static String AsString(Object value)
        {
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static String Trimmed(Object value)
        {
            String s = AsString(value);
            return String.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
    }
}
