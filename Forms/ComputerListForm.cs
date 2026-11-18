// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerListForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Add computers (main window): Active Directory search, subnet
//          scan, file, manual entry, ping. The Active Directory search runs by
//          itself: the domain computers are listed when the window opens, and
//          the list is filtered as the search text or its options change.
//          Both lists show the operating system and the last logon of the
//          Active Directory inventory (sortable columns) and follow the rules of
//          the grid (ComputerListFilter): disabled accounts hidden,
//          servers hidden unless Include servers is checked (the same setting,
//          kept in sync); a computer unknown to Active Directory is kept. Add
//          to the list applies the same filter and gives the computers to the
//          grid of the main window (Pick).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Net;
using System.DirectoryServices;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

namespace LiteDeploy
{
    public partial class ComputerListForm : Form
    {
        /// <summary>The computers chosen by Add to the list; null: closed without adding.</summary>
        private List<String> picked;
        private bool searchInProgress = false;
        private volatile int runningPingThreads = 0;
        private String baseTitle;    // window title without the Active Directory state
        private String baseStatus;   // status label without "Ping running"
        private List<int> timeSinceLastChecked;
        private Object pingThreadCountLock;
        private bool abortPing = false;
        /// <summary>Raised at each search: the ping of the previous results stops.</summary>
        private int pingGeneration = 0;
        /// <summary>Search as you type: runs once the text has not changed for a moment.</summary>
        private readonly System.Windows.Forms.Timer autoSearchTimer = new System.Windows.Forms.Timer { Interval = 400 };
        /// <summary>Results not shown (servers while they are not included, disabled accounts): shown again when servers are included.</summary>
        private readonly List<String> hiddenCandidates = new List<String>();
        private readonly ListViewColumnSorter candidatesSorter = new ListViewColumnSorter();
        private readonly ListViewColumnSorter selectedSorter = new ListViewColumnSorter();
        /// <summary>The Active Directory inventory by name, rebuilt when ActiveDirectoryCache replaces it.</summary>
        private IList<AdComputer> inventorySource;
        private Dictionary<String, AdComputer> inventoryByName = new Dictionary<String, AdComputer>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The Include servers box follows the setting (no save, no message).</summary>
        private bool syncingIncludeServers;

        public ComputerListForm()
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            baseTitle = Text;
            baseStatus = label2.Text;

            // Same setting as the grid and App > Options: the three stay in sync
            syncingIncludeServers = true;
            includeServersCheck.Checked = UserSettings.Current.IncludeServers;
            syncingIncludeServers = false;
            UserSettings.Saved += UserSettings_Saved;

            // Created at start-up and hidden most of the time: only refresh the
            // status (title, counters) while the window is shown.
            timer.Enabled = false;
            VisibleChanged += ComputerListForm_VisibleChanged;

            autoSearchTimer.Tick += (s, e) => { autoSearchTimer.Stop(); SearchDirectory(true); };
            adSearchKey.TextChanged += (s, e) => ScheduleSearch();
            adSearchContains.CheckedChanged += (s, e) => ScheduleSearch();
            adSearchStartsWith.CheckedChanged += (s, e) => ScheduleSearch();
            adSearchCaseSensitive.CheckedChanged += (s, e) => ScheduleSearch();
            FormClosed += (s, e) => autoSearchTimer.Dispose();
        }

        private void ComputerListForm_VisibleChanged(object sender, EventArgs e)
        {
            timer.Enabled = Visible;
            if (!Visible) { autoSearchTimer.Stop(); return; }
            SyncIncludeServers();
            // The domain computers at once, without clicking Search
            if (candidates.Items.Count == 0 && DirectorySearchAvailable())
                SearchDirectory(true);
        }

        private bool DirectorySearchAvailable()
        {
            return !ActiveDirectoryCache.adNotAvailable && !searchInProgress && adSearchGroup.Enabled && SearchGroup.Enabled;
        }

        private void ScheduleSearch()
        {
            autoSearchTimer.Stop();
            if (Visible && DirectorySearchAvailable()) autoSearchTimer.Start();
        }

        private void ComputerListForm_Load(object sender, EventArgs e)
        {
            getSubnets();
            abortPing = false;
            pingThreadCountLock = new Object();
            timeSinceLastChecked = new List<int>();            
        }

        /// <summary>
        /// Shows the window (modal) with an empty "to add" list; the computers chosen, or null when the window is
        /// closed without adding. The search results are kept from one opening to the next.
        /// </summary>
        public List<String> Pick(IWin32Window owner)
        {
            picked = null;
            selected.Items.Clear();
            ShowDialog(owner);
            return picked;
        }

        // ---- Active Directory details and the server filter ---------------------------------

        /// <summary>The Active Directory entry of a computer; null when it is not in the inventory (unknown OS).</summary>
        private AdComputer Lookup(String name)
        {
            RefreshInventory();
            AdComputer computer;
            return name != null && inventoryByName.TryGetValue(name.Trim(), out computer) ? computer : null;
        }

        private void RefreshInventory()
        {
            IList<AdComputer> inventory = ActiveDirectoryCache.Inventory;
            if (ReferenceEquals(inventory, inventorySource)) return;
            inventoryByName = ComputerListFilter.ByName(inventory);
            inventorySource = inventory;
        }

        /// <summary>Same rule as the grid: no disabled account, no server unless the servers are included.</summary>
        private bool IsShown(String name)
        {
            return ComputerListFilter.IsShown(Lookup(name), includeServersCheck.Checked);
        }

        /// <summary>A row of either list: name (key), operating system and last logon from Active Directory.</summary>
        private ListViewItem NewItem(String name, int stateImage)
        {
            AdComputer ad = Lookup(name);
            ListViewItem item = new ListViewItem(name) { Name = name, StateImageIndex = stateImage };
            item.SubItems.Add(ad == null ? "" : ad.OperatingSystem ?? "");
            item.SubItems.Add(ad == null ? "" : ComputerListFilter.FormatLastLogon(ad.LastLogon));
            item.ToolTipText = ad == null
                ? Localization.F("ComputerListForm.NotInAdTip", "{0}: not in Active Directory (operating system unknown)", name)
                : name + ": " + (String.IsNullOrEmpty(ad.OperatingSystem) ? "?" : ad.OperatingSystem)
                    + (String.IsNullOrEmpty(ad.OperatingSystemVersion) ? "" : " (" + ad.OperatingSystemVersion + ")")
                    + (String.IsNullOrEmpty(ad.Ou) ? "" : " - " + AdComputer.ShortOu(ad.Ou));
            return item;
        }

        private void includeServersCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (syncingIncludeServers) return;
            if (UserSettings.Current.IncludeServers != includeServersCheck.Checked)
            {
                UserSettings settings = UserSettings.Current.Clone();
                settings.IncludeServers = includeServersCheck.Checked;
                try { UserSettings.Save(settings); }
                catch (Exception ex) { AppConsole.WriteLine("Computer list: could not save " + UserSettings.FilePath + ": " + ex.Message); }
            }
            ApplyServerFilter();
        }

        private void UserSettings_Saved(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated) BeginInvoke(new MethodInvoker(SyncIncludeServers));
                return;
            }
            SyncIncludeServers();
        }

        // The box follows the setting changed in the grid or App > Options
        private void SyncIncludeServers()
        {
            if (includeServersCheck.Checked == UserSettings.Current.IncludeServers) return;
            syncingIncludeServers = true;
            try { includeServersCheck.Checked = UserSettings.Current.IncludeServers; }
            finally { syncingIncludeServers = false; }
            ApplyServerFilter();
        }

        // Servers included: the hidden results that are now allowed come back. Excluded: the servers leave
        // both lists (kept aside, they come back with the box).
        private void ApplyServerFilter()
        {
            candidates.BeginUpdate();
            selected.BeginUpdate();
            try
            {
                if (includeServersCheck.Checked)
                {
                    List<String> back = hiddenCandidates.Where(IsShown).ToList();
                    hiddenCandidates.RemoveAll(n => back.Contains(n, StringComparer.OrdinalIgnoreCase));
                    foreach (String name in back)
                    {
                        if (!candidates.Items.ContainsKey(name) && !selected.Items.ContainsKey(name))
                            candidates.Items.Add(NewItem(name, 8));
                    }
                    if (back.Count > 0) RunPing();
                }
                else
                {
                    foreach (ListView list in new[] { candidates, selected })
                    {
                        for (int i = list.Items.Count - 1; i >= 0; i--)
                        {
                            String name = list.Items[i].Text;
                            if (IsShown(name)) continue;
                            hiddenCandidates.Add(name);
                            list.Items.RemoveAt(i);
                        }
                    }
                }
            }
            finally
            {
                selected.EndUpdate();
                candidates.EndUpdate();
            }
        }

        // Sort by the clicked column (again: the other way); the sorter is removed afterwards so that adding
        // many results does not sort the list at each row
        private void list_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            ListView list = (ListView)sender;
            ListViewColumnSorter sorter = list == candidates ? candidatesSorter : selectedSorter;
            if (sorter.SortColumn == e.Column && sorter.Order == SortOrder.Ascending) sorter.Order = SortOrder.Descending;
            else { sorter.SortColumn = e.Column; sorter.Order = SortOrder.Ascending; }
            list.ListViewItemSorter = sorter;
            list.Sort();
            list.ListViewItemSorter = null;
        }

        // ---- Moving computers between the lists ----------------------------------------------

        private void addSelected_Click(object sender, EventArgs e)
        {
            selected.BeginUpdate();
            BeginCandUpdate();
            for (int i = 0; i < candidates.SelectedItems.Count; i++)
            {
                ListViewItem selItem = candidates.SelectedItems[i];
                if (!selected.Items.ContainsKey(selItem.Text))
                {
                    selected.Items.Add(NewItem(selItem.Text, selItem.StateImageIndex));
                }

                candidates.Items.RemoveByKey(selItem.Text);
                --i;
            }
            EndCandUpdate();
            selected.EndUpdate();            
        }

        private void remSelected_Click(object sender, EventArgs e)
        {
            selected.BeginUpdate();
            BeginCandUpdate();
            for (int i = 0; i < selected.SelectedItems.Count; i++)
            {
                ListViewItem liTem = selected.SelectedItems[i];
                AddCandidate(liTem.Text);
                selected.Items.RemoveByKey(liTem.Text);
                --i;
            }
            EndCandUpdate();
            selected.EndUpdate();
        }

        // Every result shown (the hidden servers and disabled accounts are not results)
        private void addAll_Click(object sender, EventArgs e)
        {
            selected.BeginUpdate();
            for (int i = 0; i < candidates.Items.Count; i++)
            {
                ListViewItem liTem = candidates.Items[i];
                if (!IsShown(liTem.Text)) { hiddenCandidates.Add(liTem.Text); continue; }
                if (!selected.Items.ContainsKey(liTem.Text))
                    selected.Items.Add(NewItem(liTem.Text, liTem.StateImageIndex));
            }
            selected.EndUpdate();
            candidates.Items.Clear();
        }

        private void removeAll_Click(object sender, EventArgs e)
        {
            BeginCandUpdate();
            for (int i = 0; i < selected.Items.Count; i++)
            {
                ListViewItem liTem = selected.Items[i];
                AddCandidate(liTem.Text);
            }
            EndCandUpdate();
            selected.Items.Clear();

            RunPing();
        }

        private void RemoveDoublesInSelected()
        {
            for (int i = 0; i < selected.Items.Count; i++)
            {
                if (ContainsDouble(i))
                {
                    selected.Items.RemoveAt(i);
                    i--;
                }
            }
        }

        private delegate bool ContainsDoubleDel( int i);
        private bool ContainsDouble( int i )
        {
            if ( selected.InvokeRequired )
            {
                ContainsDoubleDel a = new ContainsDoubleDel( ContainsDouble );
                object [] args = new object[] { i }; 
                return (bool)Invoke( a, args );
            }

            else
            {
                String key = selected.Items[i].Text;
                if (selected.Items.IndexOfKey(key) != i)
                    return true;

                else
                    return false;
            }
        }

        private void clearPreview_Click(object sender, EventArgs e)
        {
            candidates.Items.Clear();
            hiddenCandidates.Clear();
        }

        private void clearSelected_Click(object sender, EventArgs e)
        {
            selected.Items.Clear();
        }

        private void openList_Click(object sender, EventArgs e)
        {
            openComputerList.ShowDialog();
        }

        private void openComputerList_FileOk(object sender, CancelEventArgs e)
        {
            Thread readThread = new Thread( new ThreadStart(openListThreaded));
            readThread.Start();
        }

        private void openListThreaded()
        {
            using (FileStream file = new FileStream(openComputerList.FileName, FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new StreamReader(file))
            {
                BeginCandUpdate();
                while (!reader.EndOfStream)
                {
                    AddCandidate(reader.ReadLine());
                }
                EndCandUpdate();
            }

            RunPing();
        }

        private delegate void ClearCandidateDel();
        private void ClearCandidates()
        {
            if (candidates.InvokeRequired)
            {
                ClearCandidateDel a = new ClearCandidateDel(ClearCandidates);
                Invoke(a);
            }

            else
            {
                candidates.Items.Clear();
                hiddenCandidates.Clear();
            }
        }

        private void manAdd_Click(object sender, EventArgs e)
        {
            SearchGroup.Enabled = false;
            Cursor = Cursors.WaitCursor;

            if (candidates.Items.ContainsKey(manualPC.Text.ToUpper()) ||
                candidates.Items.ContainsKey(manualPC.Text.ToLower()) ||
                candidates.Items.ContainsKey(manualPC.Text) ||
                selected.Items.ContainsKey(manualPC.Text.ToUpper()) ||
                selected.Items.ContainsKey(manualPC.Text.ToLower()) ||
                selected.Items.ContainsKey(manualPC.Text))
            {
                MessageBox.Show(Localization.T("ComputerListForm.ComputerExists", "Computer is already in one of the lists."), Localization.T("ComputerListForm.ComputerExists.Title", "Computer Exists "), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                SearchGroup.Enabled = true;
                Cursor = Cursors.Default;
                manualPC.Focus();
                manualPC.SelectAll();
                return;
            }

            if (adCheck.Checked)
            {
                if (!CompInDomain(manualPC.Text) &&
                    !CompInDomain(manualPC.Text.ToLower()) &&
                    !CompInDomain(manualPC.Text.ToUpper()))
                {
                    MessageBox.Show(Localization.T("ComputerListForm.NotInAd", "Computer account was not found in AD."), Localization.T("ComputerListForm.NotInAd.Title", "Computer Not Found"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    SearchGroup.Enabled = true;
                    Cursor = Cursors.Default;
                    manualPC.Focus();
                    manualPC.SelectAll();
                    return;
                }
            }

            // A computer known to Active Directory follows the same rule as the search results
            AdComputer known = Lookup(manualPC.Text);
            if (!ComputerListFilter.IsShown(known, includeServersCheck.Checked))
            {
                String why = known.Disabled
                    ? Localization.F("ComputerListForm.ManualDisabled", "The computer account {0} is disabled in Active Directory.", known.Name)
                    : Localization.F("ComputerListForm.ManualServer", "{0} is a server ({1}). Check Include servers to add it.", known.Name, known.OperatingSystem);
                MessageBox.Show(why, Localization.T("ComputerListForm.ManualHidden.Title", "Computer Not Added"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                SearchGroup.Enabled = true;
                Cursor = Cursors.Default;
                manualPC.Focus();
                manualPC.SelectAll();
                return;
            }

            selected.Items.Add(NewItem(manualPC.Text.Trim(), -1));
            SearchGroup.Enabled = true;
            manualPC.Focus();
            manualPC.SelectAll();
            Cursor = Cursors.Default;
        }

        public bool CompInDomain(string username)
        {
            if (ActiveDirectoryCache.cacheFound && ActiveDirectoryCache.adUpdating)
            {
                if (ActiveDirectoryCache.CachedAD.Contains(username))
                    return true;

                else
                    return false;
            }

            else
            {
                if (ActiveDirectoryCache.Computers.Contains(username))
                    return true;

                else
                    return false;
            }
        }

        private void saveList_Click(object sender, EventArgs e)
        {
            if (selected.Items.Count == 0)
            {
                MessageBox.Show(Localization.T("ComputerListForm.ListEmpty", "There are no computers in the list. Add computers to your list."), Localization.T("ComputerListForm.ListEmpty.Title", "No Computers Selected"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            saveFile.ShowDialog();
        }

        private void saveFile_FileOk(object sender, CancelEventArgs e)
        {
            try
            {
                using (FileStream file = new FileStream(saveFile.FileName, FileMode.Create))
                using (StreamWriter writer = new StreamWriter(file))
                {
                    for (int i = 0; i < selected.Items.Count; i++)
                        writer.WriteLine(selected.Items[i].Text);
                }
            }

            catch
            {
                MessageBox.Show(Localization.T("ComputerListForm.SaveError", "An error happened while trying to save your file. Please make sure you have the proper permissions to write to the directory."), Localization.T("ComputerListForm.SaveError.Title", "Error Saving"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Use_Click(object sender, EventArgs e)
        {
            RemoveDoublesInSelected();

            // Same filter as the grid: no server unless included, no disabled account
            RefreshInventory();
            List<String> dropped;
            List<String> names = ComputerListFilter.Shown(selected.Items.Cast<ListViewItem>().Select(o => o.Text), inventoryByName, includeServersCheck.Checked, out dropped);

            if (dropped.Count > 0)
                MessageBox.Show(Localization.F("ComputerListForm.Dropped", "{0} computer(s) not used (servers while Include servers is off, or disabled accounts): {1}", dropped.Count, String.Join(", ", dropped)),
                    Localization.T("ComputerListForm.Dropped.Title", "Add to the List"), MessageBoxButtons.OK, MessageBoxIcon.Information);

            picked = names;
            Close();
        }

        // Subnets menu: the subnets of this computer, then the saved ones (team's subnets.txt
        // next to LiteDeploy.exe and the user's list), then Edit.
        private void getSubnets()
        {
            LocalNetworkSettings.GetLocalSubnets();

            // Clear() does not dispose the removed menu items (nor unhook their Click handler)
            while (subnetsToolStripMenuItem.DropDownItems.Count > 0)
            {
                ToolStripItem old = subnetsToolStripMenuItem.DropDownItems[0];
                old.Click -= subnetDropDownMenuItem_Click;
                old.Click -= editSubnetsMenuItem_Click;
                subnetsToolStripMenuItem.DropDownItems.RemoveAt(0);
                old.Dispose();
            }

            String thisComputer = Localization.T("ComputerListForm.ThisComputer", "this computer");
            foreach (String s in LocalNetworkSettings.AvailableSubnets)
                AddSubnetItem(s, s + "0/24 - " + thisComputer);

            List<SubnetEntry> saved = SubnetList.Load().Where(x => !LocalNetworkSettings.AvailableSubnets.Contains(x.Prefix)).ToList();
            if (saved.Count > 0)
            {
                subnetsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
                foreach (SubnetEntry entry in saved)
                    AddSubnetItem(entry.Prefix, entry.Display);
            }

            subnetsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            ToolStripItem edit = subnetsToolStripMenuItem.DropDownItems.Add(Localization.T("ComputerListForm.EditSubnets", "Edit subnets..."));
            edit.Click += editSubnetsMenuItem_Click;

            octet1.Value = LocalNetworkSettings.subnetParseOctet1;
            octet2.Value = LocalNetworkSettings.subnetParseOctet2;
            octet3.Value = LocalNetworkSettings.subnetParseOctet3;

#if DEBUG
            LocalNetworkSettings.DnsOK = true;
            return;
#endif

            // A DNS server in the network of this computer (same first octet)
            foreach (String s in LocalNetworkSettings.AvailableDNSServers ?? new List<String>())
            {
                String first = s.Split('.')[0] + ".";
                if (LocalNetworkSettings.AvailableSubnets.Any(x => x.StartsWith(first, StringComparison.Ordinal)))
                {
                    LocalNetworkSettings.DnsOK = true;
                    break;
                }
            }
        }

        private void AddSubnetItem(String prefix, String text)
        {
            ToolStripItem item = subnetsToolStripMenuItem.DropDownItems.Add(text);
            item.Tag = prefix;
            item.Click += subnetDropDownMenuItem_Click;
        }

        private void subnetDropDownMenuItem_Click(object sender, EventArgs e)
        {
            String[] octets = ((String)((ToolStripItem)sender).Tag).TrimEnd('.').Split('.');
            octet1.Value = Convert.ToDecimal(octets[0]);
            octet2.Value = Convert.ToDecimal(octets[1]);
            octet3.Value = Convert.ToDecimal(octets[2]);
        }

        private void editSubnetsMenuItem_Click(object sender, EventArgs e)
        {
            using (SubnetsForm form = new SubnetsForm())
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    decimal o1 = octet1.Value, o2 = octet2.Value, o3 = octet3.Value;
                    getSubnets();
                    octet1.Value = o1; octet2.Value = o2; octet3.Value = o3;
                }
            }
        }

        private void scanSubnet_Click(object sender, EventArgs e)
        {
            candidates.Items.Clear();
            hiddenCandidates.Clear();
            Thread t = new Thread(new ThreadStart(GoDnsSearch));
            t.Start();
        }

        private void adSearch_Click(object sender, EventArgs e)
        {
            autoSearchTimer.Stop();
            SearchDirectory(false);
        }

        private void GoDnsSearch()
        {
            EnableSearchKey(false);

            List<String> hosts;

            // A form shown with ShowDialog is only hidden when closed: dispose it.
            using (DnsScanForm dq = new DnsScanForm(octet1.Value.ToString() + "." + octet2.Value.ToString() + "." + octet3.Value.ToString() + "."))
            {
                dq.ShowDialog();
                hosts = dq.GetHosts();
            }

            BeginCandUpdate();
            ClearCandidates();
            foreach (String h in hosts)
                AddCandidate(h);
            EndCandUpdate();

            EnableSearchKey(true);

            RunPing();
        }

        /// <summary>
        /// Computers of <paramref name="names"/> that match <paramref name="key"/>: the same name,
        /// or (options) a name that starts with it or contains it; case-insensitive unless
        /// <paramref name="caseSensitive"/>. An empty key with "starts with" or "contains" lists them all.
        /// </summary>
        internal static List<String> FindComputers(IEnumerable<String> names, String key, bool startsWith, bool contains, bool caseSensitive)
        {
            key = key ?? "";
            StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return names.Where(x => x != null && (String.Equals(x, key, comparison)
                    || (startsWith && x.StartsWith(key, comparison))
                    || (contains && x.IndexOf(key, comparison) >= 0)))
                .ToList();
        }

        // In-memory search of the Active Directory inventory (the one of the grid: the last
        // refresh, else the cache), fast enough for the UI thread, so the search box keeps the focus
        // while the user types. Disabled accounts and servers (unless included) are not listed.
        // auto: started by the window or the search box, without the message about the cache.
        private void SearchDirectory(bool auto)
        {
            if (searchInProgress) return;
            IList<AdComputer> inventory = ActiveDirectoryCache.Inventory;
            List<String> source;
            try
            {
                source = inventory.Count > 0
                    ? inventory.Select(c => c.Name).ToList()
                    : (ActiveDirectoryCache.cacheFound && (ActiveDirectoryCache.adUpdating || !ActiveDirectoryCache.DCAvailable)
                        ? ActiveDirectoryCache.CachedAD
                        : ActiveDirectoryCache.Computers).ToList();
            }
            catch (InvalidOperationException) { source = new List<String>(); }   // list being refreshed
            List<String> results = FindComputers(source, adSearchKey.Text, adSearchStartsWith.Checked, adSearchContains.Checked, adSearchCaseSensitive.Checked);

            candidates.BeginUpdate();
            candidates.Items.Clear();
            hiddenCandidates.Clear();
            foreach (String currentComp in results)
                AddCandidate(currentComp);
            candidates.EndUpdate();

            RunPing();

            if (!auto && ActiveDirectoryCache.adUpdating)
            {
                MessageBox.Show( Localization.F("ComputerListForm.FromCache", "Results acquired from the Active Directory cache created on {0} at {1}.",
                                ActiveDirectoryCache.LastUpdate.ToShortDateString(), ActiveDirectoryCache.LastUpdate.ToShortTimeString()),
                                Localization.T("ComputerListForm.FromCache.Title", "Search Results"), MessageBoxButtons.OK, MessageBoxIcon.Information );
            }
        }

        private delegate void BeginCandUpdateDel();
        private void BeginCandUpdate()
        {
            if (candidates.InvokeRequired)
            {
                BeginCandUpdateDel a = new BeginCandUpdateDel(BeginCandUpdate);
                Invoke(a);
            }

            else
            {
                candidates.BeginUpdate();
            }
        }

        private delegate void EndCandUpdateDel();
        private void EndCandUpdate()
        {
            if (candidates.InvokeRequired)
            {
                EndCandUpdateDel a = new EndCandUpdateDel(EndCandUpdate);
                Invoke(a);
            }

            else
            {
                candidates.EndUpdate();
            }
        }

        private delegate void AddCand(String s);
        private void AddCandidate(String s)
        {
            if (candidates.InvokeRequired)
            {
                AddCand a = new AddCand(AddCandidate);
                object[] args = new object[] { s };
                Invoke(a, args);
            }

            else
            {
                // Search, text file, DNS scan: same filter as the grid (a name unknown to
                // Active Directory is kept); blank lines of a file are skipped
                if (String.IsNullOrWhiteSpace(s)) return;
                String name = s.Trim();
                if (!IsShown(name)) { hiddenCandidates.Add(name); return; }
                candidates.Items.Add(NewItem(name, 8));
            }
        }
        
        private delegate void EnableSK(bool b);
        private void EnableSearchKey(bool b)
        {
            if (adSearchGroup.InvokeRequired)
            {
                EnableSK a = new EnableSK(EnableSearchKey);
                object[] args = new object[] { b };
                Invoke(a, args);
            }

            else
            {
                SearchGroup.Enabled = b;
                MoveButtons.Enabled = b;
                ListIOGroup.Enabled = b;
                candidates.Enabled = b;
                selected.Enabled = b;
                searchInProgress = !b;

                if (b == false)
                    Cursor = Cursors.WaitCursor;
                else
                    Cursor = Cursors.Default;
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            // Title: base title (translated) + Active Directory state; never compared as text
            String adState = "";

            if (!ActiveDirectoryCache.adNotAvailable)
            {
                if (ActiveDirectoryCache.adUpdating)
                {
                    adState = Localization.T("ComputerListForm.AdUpdating", "AD updating");
                    updateToolStripMenuItem.Enabled = false;
                }

                else if (ActiveDirectoryCache.adUpToDate || !ActiveDirectoryCache.DCAvailable)
                {
                    adState = Localization.F("ComputerListForm.AdLastUpdate", "AD last update: {0} {1}",
                                             ActiveDirectoryCache.LastUpdate.ToShortDateString(), ActiveDirectoryCache.LastUpdate.ToShortTimeString());
                    updateToolStripMenuItem.Enabled = true;
                }
            }

            else
            {
                adSearchGroup.Enabled = false;
                adCheck.Enabled = false;
                activeDirectoryToolStripMenuItem.Enabled = false;
            }

            String title = adState.Length > 0 ? baseTitle + " - " + adState : baseTitle;
            if (Text != title)
                Text = title;

            if (!LocalNetworkSettings.DnsOK)
            {
                SubnetScanGroup.Enabled = false;
                subnetsToolStripMenuItem.Enabled = false;
            }
            else
            {
                if (SubnetScanGroup.Enabled == false)
                {
                    SubnetScanGroup.Enabled = true;
                    subnetsToolStripMenuItem.Enabled = true;
                }
            }

            String status = runningPingThreads > 0
                ? baseStatus + " - " + Localization.T("ComputerListForm.PingRunning", "Ping running")
                : baseStatus;
            if (label2.Text != status)
                label2.Text = status;

            NumSelectedComps.Text = selected.Items.Count.ToString();

            String results = hiddenCandidates.Count > 0
                ? Localization.F("ComputerListForm.ResultCount", "{0} ({1} hidden: servers or disabled accounts)", candidates.Items.Count, hiddenCandidates.Count)
                : candidates.Items.Count.ToString();
            if (searchResultsCount.Text != results)
                searchResultsCount.Text = results;
        }

        private void adSearchKey_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Return)
            {
                adSearch_Click(sender, e);
                e.SuppressKeyPress = true;
            }
        }

        private void manualPC_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Return)
                manAdd_Click(sender, e);
        }

        private void updateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ActiveDirectoryCache.GetComputers();
        }

        private void octet1_Enter( object sender, EventArgs e )
        {
            octet1.Select( 0, octet1.Value.ToString().Length );
        }

        private void octet2_Enter( object sender, EventArgs e )
        {
            octet2.Select( 0, octet2.Value.ToString().Length );
        }

        private void octet3_Enter( object sender, EventArgs e )
        {
            octet3.Select( 0, octet3.Value.ToString().Length );
        }

        private void octet1_Click( object sender, EventArgs e )
        {
            octet1_Enter( sender, e );
        }

        private void octet2_Click( object sender, EventArgs e )
        {
            octet2_Enter( sender, e );
        }

        private void octet3_Click( object sender, EventArgs e )
        {
            octet3_Enter( sender, e );
        }

        private void ComputerListForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
            DnsScanForm.abort = true;
            abortPing = true;
            MainFormAccess.mainForm.Focus();
        }

        private void candidates_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (candidates.SelectedIndices.Count > 0)
            {
                if (candidates.SelectedIndices[0] >= 0)
                {
                    ListViewItem cand = candidates.Items[candidates.SelectedIndices[0]];

                    if (!selected.Items.ContainsKey(cand.Text))
                    {
                        selected.Items.Add(NewItem(cand.Text, cand.StateImageIndex));
                    }

                    candidates.Items.RemoveByKey(cand.Text);
                }
            }
        }

        private void selected_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (selected.SelectedIndices.Count > 0)
            {
                if (selected.SelectedIndices[0] >= 0)
                {
                    String item = selected.Items[selected.SelectedIndices[0]].Text;
                    AddCandidate(item);
                    selected.Items.RemoveByKey(item);
                }
            }
        }

        private void RunPing()
        {
            // A new search stops the ping of the previous results
            int generation = Interlocked.Increment(ref pingGeneration);
            Thread T = new Thread(() => pingCandidates(generation)) { IsBackground = true };
            T.Start();
        }

        private int CandidateCount()
        {
            if (candidates.InvokeRequired)
            {
                try { return (int)Invoke(new Func<int>(CandidateCount)); }
                catch (ObjectDisposedException) { return 0; }
                catch (InvalidOperationException) { return 0; }
            }
            return candidates.Items.Count;
        }

        private delegate KeyValuePair<String, int> GetCandidateDel(int i);
        private KeyValuePair<String, int> GetCandidate(int i)
        {
            if (candidates.InvokeRequired)
            {
                GetCandidateDel a = new GetCandidateDel(GetCandidate);
                object[] args = new object[] { i };
                return (KeyValuePair<String, int>)Invoke(a, args);
            }

            else
            {
                try
                {
                    ListViewItem item = candidates.Items[i];
                    return new KeyValuePair<string, int>(item.Text, item.StateImageIndex);
                }

                catch
                {
                    return new KeyValuePair<String, int>("", -1);
                }
            }
        }

        private void pingCandidates(int generation)
        {
            while(runningPingThreads > 0)
            {
                if (generation != Volatile.Read(ref pingGeneration)) return;
                Thread.Sleep(10);
            }

            for (int i = 0; i < CandidateCount(); i++)
            {
                if (searchInProgress || generation != Volatile.Read(ref pingGeneration))
                    return;

                if (abortPing)
                    return;

                while (runningPingThreads > 1024)
                    Thread.Sleep(10);

                KeyValuePair<String,int> item = GetCandidate(i);

                if ( item.Value == 0 )
                    continue;

                Thread T = new Thread(new ThreadStart(() => pingHost(item.Key)));
                T.Start();
            }
        }

        private delegate void SetCandidateStateDel(string key, int i);
        private void SetCandidateState(string key, int i)
        {
            if (searchInProgress)
                return;

            if (candidates.InvokeRequired)
            {
                SetCandidateStateDel a = new SetCandidateStateDel(SetCandidateState);
                object[] args = new object[] { key, i };
                Invoke(a, args);
            }

            else
            {
                try
                {
                    candidates.Items[key].StateImageIndex = i;
                }

                catch
                {
                }
            }
        }

        private void pingHost(string key)
        {
            if (searchInProgress)
                return;

            if (abortPing)
                return;

            try
            {
                lock (pingThreadCountLock)
                {
                    runningPingThreads++;
                }

                PingReply reply;

                using (Ping pingSender = new Ping())
                {
                    reply = pingSender.Send(key, 100);
                }

                if (reply.Status == IPStatus.Success)
                {
                    SetCandidateState(key, 0);
                }

                else
                {
                    SetCandidateState(key, 2);
                }
            }

            catch
            {
                SetCandidateState(key, 1);

                lock (pingThreadCountLock)
                {
                    runningPingThreads--;
                }

                return;
            }

            lock (pingThreadCountLock)
            {
                runningPingThreads--;
            }
        }

        private void AddAllAvailable_Click(object sender, EventArgs e)
        {
            if (runningPingThreads > 0)
            {
                DialogResult r = MessageBox.Show(Localization.T("ComputerListForm.StillPinging", "LiteDeploy is still checking for host availability. Try again in a few seconds."), "LiteDeploy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            selected.BeginUpdate();
            for (int i = 0; i < candidates.Items.Count; i++)
            {
                ListViewItem liTem = candidates.Items[i];

                // Reachable, and allowed by the same filter as the results
                if (liTem.StateImageIndex == 0 && IsShown(liTem.Text))
                {
                    if (!selected.Items.ContainsKey(liTem.Text))
                        selected.Items.Add(NewItem(liTem.Text, liTem.StateImageIndex));
                    candidates.Items.RemoveAt(i);
                    --i;
                }
            }
            selected.EndUpdate();
        }

        private void pingAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RunPing();
        }
    }
}
