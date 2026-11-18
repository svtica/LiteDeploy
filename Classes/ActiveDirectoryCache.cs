// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ActiveDirectoryCache.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Computer names of the current domain (LDAP search), cached in
//          adcache.xml next to LiteDeploy.exe, with the inventory of Software
//          Updates (operating system, last logon, disabled account, OU). A
//          cache written by an earlier version (names only) stays readable:
//          its inventory has the names only until the next refresh.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.DirectoryServices;
using System.Windows.Forms;
using System.IO;
using System.Xml.Serialization;
using System.Threading;
using System.Net;
using System.Net.NetworkInformation;

namespace LiteDeploy
{
    // Root element kept as "ActiveDirCacheFile": adcache.xml files written by earlier versions stay readable
    [XmlRoot("ActiveDirCacheFile")]
    public struct ActiveDirectoryCacheFile
    {
        public String DomainName;
        public List<String> Computers;
        /// <summary>Details of the computers (absent from the files of earlier versions, ignored by them).</summary>
        [XmlArrayItem("Computer")] public List<AdComputer> Inventory;
    }

    static public class ActiveDirectoryCache
    {
        static public List<String> Computers = new List<String>();
        static public List<String> CachedAD = new List<String>();
        static public bool cacheFound = false;
        static public bool adUpdating = false;
        static public bool adUpToDate = false;
        static public bool adNotAvailable = false;
        static public bool DCAvailable = false;
        static public DateTime LastUpdate;
        static private Thread UpdateThread = new Thread( new ThreadStart(BeginADDownload) );
        static public volatile bool Abort = false;

        // Replaced as a whole (never changed in place): readers can keep the list they got
        static private volatile List<AdComputer> inventory = new List<AdComputer>();
        // Being filled by a refresh
        static private List<AdComputer> downloading = new List<AdComputer>();

        /// <summary>
        /// Computers of the domain with their details (the grid): the last refresh, else the cache.
        /// Never changed once returned.
        /// </summary>
        static public IList<AdComputer> Inventory { get { return inventory; } }

        /// <summary>Raised (on a worker thread) when a refresh replaced the inventory.</summary>
        static public event EventHandler InventoryChanged;

        /// <summary>Inventory of a cache file: its details, else its names only (file of an earlier version).</summary>
        static internal List<AdComputer> InventoryOf(ActiveDirectoryCacheFile cache)
        {
            if (cache.Inventory != null && cache.Inventory.Count > 0)
                return cache.Inventory.Where(c => c != null && !String.IsNullOrEmpty(c.Name)).ToList();
            return (cache.Computers ?? new List<String>()).Where(n => !String.IsNullOrEmpty(n)).Select(n => new AdComputer { Name = n }).ToList();
        }

        static void SetInventory(List<AdComputer> list)
        {
            inventory = list;
            EventHandler handler = InventoryChanged;
            if (handler != null)
            {
                try { handler(null, EventArgs.Empty); }
                catch (Exception) { }   // a window closed meanwhile
            }
        }

        static private ActiveDirectoryCacheFile LoadCacheFile()
        {
            using (FileStream file = new FileStream(Directory.GetCurrentDirectory() + "\\adcache.xml", FileMode.Open, FileAccess.Read))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ActiveDirectoryCacheFile));
                return (ActiveDirectoryCacheFile)serializer.Deserialize(file);
            }
        }

        static private void WriteCacheFile()
        {
            ActiveDirectoryCacheFile cache = new ActiveDirectoryCacheFile();
            cache.DomainName = Environment.UserDomainName;
            cache.Computers = Computers;
            cache.Inventory = downloading;

            using (FileStream file = new FileStream(Directory.GetCurrentDirectory() + "\\adcache.xml", FileMode.Create, FileAccess.Write))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ActiveDirectoryCacheFile));
                serializer.Serialize(file, cache);
            }
        }

        static public void GetComputers()
        {
            LocalNetworkSettings.GetLocalIPAddress();

            if (Environment.UserDomainName.ToUpper() == Environment.MachineName)
            {
                adNotAvailable = true;
                return;
            }

            try
            {
                // get root of the directory data tree on a directory server
                string dnsHostname;

                using (DirectoryEntry dirEntry = new DirectoryEntry("LDAP://rootDSE"))
                {
                    // get the hostname of the directory server of your root (I'm assuming that's what you want)
                    dnsHostname = dirEntry.Properties["dnsHostname"].Value.ToString();
                }

                IPAddress[] ipAddresses = Dns.GetHostAddresses(dnsHostname);

                using (Ping pingSender = new Ping())
                {
                    for (int i = 0; i < ipAddresses.Count(); i++)
                    {
                        try
                        {
                            PingReply reply = pingSender.Send(ipAddresses[i].ToString(), 1000);
                            if ( reply.Status == IPStatus.Success )
                            {
                                DCAvailable = true;
                                break;
                            }
                        }

                        catch {}
                    }
                }
            }

            catch { }

            cacheFound = false;
            CachedAD.Clear();

            if (File.Exists("adcache.xml"))
            {
                ActiveDirectoryCacheFile cache = LoadCacheFile();

                if (Environment.UserDomainName == cache.DomainName)
                {
                    cacheFound = true;
                    LastUpdate = File.GetLastWriteTime("adcache.xml");

                    foreach (String s in cache.Computers ?? new List<String>())
                    {
                        CachedAD.Add(s);
                    }

                    SetInventory(InventoryOf(cache));
                }
            }

            if (cacheFound && AppSettings.AUTO_UPDATE_AD_ON_STARTUP)
            {
                Thread UpdateThread = new Thread(new ThreadStart(BeginADDownload));
                UpdateThread.Start();
            }

            else if (cacheFound && !AppSettings.AUTO_UPDATE_AD_ON_STARTUP)
            {
                AppConsole.WriteLine(Localization.T("ActiveDirectory.CacheLoaded", "Cached Active Directory list was loaded."));
            }

            else
            {
                if (adUpToDate)
                    MessageBox.Show(Localization.T("ActiveDirectory.NoCache", "Please wait!\n\nThe cached Active Directory list was not found.\n\nClick OK to continue. This process can take several minutes."), Localization.T("ActiveDirectory.NoCache.Title", "Active Directory Updating"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

                BeginADDownload();
            }
        }

        static private void BeginADDownload()
        {
            if (DCAvailable)
            {
                AppConsole.WriteLine(Localization.T("ActiveDirectory.Updating", "Active Directory started updating."));
                adUpToDate = false;
                adUpdating = true;

                try
                {
                    Computers.Clear();
                    downloading = new List<AdComputer>();

                    // SearchResultCollection must be disposed explicitly (it holds the
                    // unmanaged ADSI search handle), and so must every DirectoryEntry.
                    // The computer name is read from the search result itself instead
                    // of binding one DirectoryEntry per computer.
                    using (DirectoryEntry entry = new DirectoryEntry("LDAP://" + Environment.UserDomainName))
                    using (DirectorySearcher mySearcher = new DirectorySearcher(entry))
                    {
                        mySearcher.Asynchronous = true;
                        mySearcher.Filter = ("(objectClass=computer)");
                        mySearcher.SizeLimit = int.MaxValue;
                        mySearcher.PageSize = int.MaxValue;
                        mySearcher.PropertiesToLoad.Add("name");
                        // Inventory of the grid
                        mySearcher.PropertiesToLoad.Add("operatingSystem");
                        mySearcher.PropertiesToLoad.Add("operatingSystemVersion");
                        mySearcher.PropertiesToLoad.Add("lastLogonTimestamp");
                        mySearcher.PropertiesToLoad.Add("userAccountControl");
                        mySearcher.PropertiesToLoad.Add("distinguishedName");

                        using (SearchResultCollection FoundResults = mySearcher.FindAll())
                        {
                            foreach (SearchResult resEnt in FoundResults)
                            {
                                if (Abort)
                                    return;

                                SearchResult result = resEnt;
                                AdComputer computer = AdComputer.FromProperties(p =>
                                    result.Properties.Contains(p) && result.Properties[p].Count > 0 ? result.Properties[p][0] : null);
                                if (computer == null)
                                    continue;

                                Computers.Add(computer.Name);
                                downloading.Add(computer);
                            }
                        }
                    }

                    Computers.Sort();
                    downloading.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                    WriteCacheFile();
                    SetInventory(downloading);

                    LastUpdate = DateTime.Now;
                }

                catch (Exception e)
                {
                    MessageBox.Show(Localization.F("ActiveDirectory.UpdateFailedDetail", "Active Directory update failed. {0}", e.Message), Localization.T("ActiveDirectory.UpdateFailed.Title", "Active Directory Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    AppConsole.WriteLine(Localization.T("ActiveDirectory.UpdateFailed", "Active Directory update failed."));

                    if (cacheFound)
                    {
                        Computers.Clear();

                        foreach (String c in CachedAD)
                        {
                            Computers.Add(c);
                        }
                    }
                }

                adUpdating = false;
                adUpToDate = true;
                AppConsole.WriteLine(Localization.T("ActiveDirectory.Updated", "Active Directory finished updating."));
            }

            else
            {
                AppConsole.WriteLine(Localization.T("ActiveDirectory.NoDc", "Domain controller could not be reached. Cached Active Directory list was loaded."));
            }
        }

        static public void StopUpdate()
        {
            Abort = true;

            try
            {
                UpdateThread.Abort();            
            }
            catch { }
        }
    }
}
