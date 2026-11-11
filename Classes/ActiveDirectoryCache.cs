// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ActiveDirectoryCache.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Computer names of the current domain (LDAP search), cached in
//          adcache.xml next to LiteDeploy.exe.
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

                    foreach (String s in cache.Computers)
                    {
                        CachedAD.Add(s);
                    }
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

                        using (SearchResultCollection FoundResults = mySearcher.FindAll())
                        {
                            foreach (SearchResult resEnt in FoundResults)
                            {
                                if (Abort)
                                    return;

                                if (resEnt.Properties["name"].Count == 0)
                                    continue;

                                String ComputerName = resEnt.Properties["name"][0].ToString();

                                if (ComputerName.StartsWith("CN="))
                                {
                                    ComputerName = ComputerName.Remove(0, 3);
                                }

                                Computers.Add(ComputerName);
                            }
                        }
                    }

                    Computers.Sort();

                    WriteCacheFile();

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
