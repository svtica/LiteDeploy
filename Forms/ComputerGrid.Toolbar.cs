// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerGrid.Toolbar.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-24
// Purpose: Lighter tool bars of the computer grid, built on the buttons of the
//          designer (kept, hidden, so their handlers and states stay the same):
//          "Selection" groups Select all / none / outdated; "More" groups Show
//          packages, Software catalog, Schedule and Deploy (also in the Tools
//          menu); Stop shows only while a check or an update runs; Update
//          checked gives the number of checked rows; the filter bar counts the
//          active filters (status chips and search) with "Clear".
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class ComputerGrid
    {
        ToolStripDropDownButton selectionButton;
        ToolStripDropDownButton moreButton;
        ToolStripLabel activeFiltersLabel;
        ToolStripButton clearFiltersButton;

        void InitializeToolbarTweaks()
        {
            // Selection: the three buttons of the designer in one drop-down
            selectionButton = new ToolStripDropDownButton
            {
                Name = "selectionButton",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = Localization.T("ComputerGrid.SelectionButton", "Selection"),
                ToolTipText = Localization.T("ComputerGrid.SelectionTip", "Check the rows shown: all (Ctrl+A), none, or the outdated ones")
            };
            // A hidden tool strip item ignores PerformClick: the handlers are called directly
            ToolStripItem[] selection = { selectAllButton, selectNoneButton, selectOutdatedButton };
            EventHandler[] selectionClicks = { selectAllButton_Click, selectNoneButton_Click, selectOutdatedButton_Click };
            for (int i = 0; i < selection.Length; i++)
            {
                EventHandler click = selectionClicks[i];
                ToolStripMenuItem item = new ToolStripMenuItem(selection[i].Text) { ToolTipText = selection[i].ToolTipText };
                item.Click += (s, e) => click(this, EventArgs.Empty);
                selectionButton.DropDownItems.Add(item);
            }
            selectionButton.DropDownOpening += (s, e) =>
            {
                for (int i = 0; i < selection.Length; i++) selectionButton.DropDownItems[i].Enabled = selection[i].Enabled;
            };
            toolStrip.Items.Insert(toolStrip.Items.IndexOf(selectAllButton), selectionButton);
            foreach (ToolStripItem original in selection) original.Visible = false;

            // More: the view and the windows also found in the Tools menu
            moreButton = new ToolStripDropDownButton
            {
                Name = "moreButton",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = Localization.T("ComputerGrid.MoreButton", "More"),
                ToolTipText = Localization.T("ComputerGrid.MoreTip", "Show packages, software catalog, schedules, standalone deployment")
            };
            ToolStripItem[] more = { byPackageButton, editPackagesButton, scheduleButton, deployButton };
            EventHandler[] moreClicks =
            {
                (s, e) => { byPackageButton.Checked = !byPackageButton.Checked; byPackageButton_Click(this, EventArgs.Empty); },
                editPackagesButton_Click, scheduleButton_Click, deployItem_Click
            };
            for (int i = 0; i < more.Length; i++)
            {
                EventHandler click = moreClicks[i];
                ToolStripMenuItem item = new ToolStripMenuItem(more[i].Text) { ToolTipText = more[i].ToolTipText };
                item.Click += (s, e) => click(this, EventArgs.Empty);
                moreButton.DropDownItems.Add(item);
            }
            moreButton.DropDownOpening += (s, e) =>
            {
                for (int i = 0; i < more.Length; i++)
                {
                    ToolStripMenuItem item = (ToolStripMenuItem)moreButton.DropDownItems[i];
                    item.Enabled = more[i] == deployButton ? deployEnabled : more[i].Enabled;
                    // Deploy: only when the standalone deployments are shown (App > Options)
                    item.Visible = more[i] != deployButton || UserSettings.Current.ShowStandaloneDeployments;
                }
                ((ToolStripMenuItem)moreButton.DropDownItems[0]).Checked = byPackageButton.Checked;
            };
            toolStrip.Items.Add(moreButton);
            foreach (ToolStripItem original in more) original.Visible = false;
            separator1.Visible = false;

            // Filter bar: number of active filters, and Clear
            activeFiltersLabel = new ToolStripLabel { Name = "activeFiltersLabel", ForeColor = Theme.WarningColor, Visible = false };
            clearFiltersButton = new ToolStripButton
            {
                Name = "clearFiltersButton",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = Localization.T("ComputerGrid.ClearFilters", "Clear"),
                ToolTipText = Localization.T("ComputerGrid.ClearFiltersTip", "Uncheck the status filters and empty the search (Online only and Include servers are kept)"),
                Visible = false
            };
            clearFiltersButton.Click += (s, e) => ClearFilters();
            int at = filterStrip.Items.IndexOf(filterSeparator2);
            filterStrip.Items.Insert(at, clearFiltersButton);
            filterStrip.Items.Insert(at, activeFiltersLabel);
        }

        /// <summary>The number of active filters: status chips checked, and the search when it is not empty. Unit-tested.</summary>
        internal static int ActiveFilterCount(IEnumerable<bool> chipsChecked, String search)
        {
            return (chipsChecked ?? Enumerable.Empty<bool>()).Count(c => c) + (String.IsNullOrWhiteSpace(search) ? 0 : 1);
        }

        /// <summary>The text of Update checked: with the number of checked rows when there are some.</summary>
        internal static String UpdateButtonText(int checkedRows)
        {
            String text = Localization.T("ComputerGrid.updateButton.Text", "Update checked");
            return checkedRows > 0 ? Localization.F("ComputerGrid.UpdateCheckedCount", "{0} ({1})", text, checkedRows) : text;
        }

        /// <summary>Called with the counts: Update checked (number), the active filters, Stop (only while working).</summary>
        void RefreshToolbarTweaks()
        {
            if (activeFiltersLabel == null) return;
            updateButton.Text = UpdateButtonText(ByPackage ? checkedKeys.Count : checkedComputers.Count);
            int filters = ActiveFilterCount(chips.Keys.Select(c => c.Checked), searchBox.Text);
            activeFiltersLabel.Text = Localization.F("ComputerGrid.ActiveFilters", "Active filters: {0}", filters);
            activeFiltersLabel.Visible = clearFiltersButton.Visible = filters > 0;
            stopButton.Visible = pending > 0;
        }

        void ClearFilters()
        {
            loading = true;   // one refresh at the end, not one per chip
            try
            {
                foreach (ToolStripButton chip in chips.Keys) chip.Checked = false;
                searchBox.Text = "";
            }
            finally { loading = false; }
            searchTimer.Stop();
            RefreshList();
        }
    }
}
