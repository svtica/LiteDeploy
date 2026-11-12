// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ToolsForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Manage Tools window: the local tools (PsExec, LitePM), their version
//          and location; download or update one, open LitePM.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class ToolsForm : Form
    {
        List<ToolInfo> tools = new List<ToolInfo>();

        public ToolsForm()
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            RefreshList(null);
        }

        void RefreshList(String selectedId)
        {
            tools = ToolManager.List();
            toolsList.Items.Clear();
            foreach (ToolInfo t in tools)
            {
                ListViewItem item = new ListViewItem(new[]
                {
                    t.DisplayName,
                    t.Version ?? (t.Path != null ? "?" : ""),
                    t.Path ?? Localization.F("ToolsForm.NotInstalled", "Not installed (source: {0})", t.Source),
                    t.Description
                }) { Tag = t };
                if (t.Path == null) item.ForeColor = Theme.WarningColor;
                toolsList.Items.Add(item);
                if (t.Id == selectedId) item.Selected = true;
            }
            if (toolsList.SelectedItems.Count == 0 && toolsList.Items.Count > 0) toolsList.Items[0].Selected = true;
            UpdateButtons();
        }

        ToolInfo Selected
        {
            get { return toolsList.SelectedItems.Count == 0 ? null : (ToolInfo)toolsList.SelectedItems[0].Tag; }
        }

        void UpdateButtons()
        {
            ToolInfo t = Selected;
            installButton.Enabled = t != null;
            openButton.Enabled = t != null && t.Path != null && t.Id == ToolManager.LitePMId;
        }

        void toolsList_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        void installButton_Click(object sender, EventArgs e)
        {
            ToolInfo t = Selected;
            if (t == null) return;
            installButton.Enabled = false;
            Cursor = Cursors.WaitCursor;
            statusLabel.Text = Localization.F("ToolsForm.Downloading", "Downloading {0}...", t.DisplayName);
            new Thread(() =>
            {
                String message;
                try { message = ToolManager.Install(t.Id); }
                catch (Exception ex) { message = Localization.F("ToolsForm.Failed", "{0} could not be installed: {1}", t.DisplayName, ex.Message); }
                if (IsDisposed) return;
                BeginInvoke(new MethodInvoker(() =>
                {
                    Cursor = Cursors.Default;
                    statusLabel.Text = message;
                    RefreshList(t.Id);
                }));
            }) { IsBackground = true }.Start();
        }

        void openButton_Click(object sender, EventArgs e)
        {
            try { ToolManager.Open(Selected); }
            catch (Exception ex) { statusLabel.Text = ex.Message; }
        }
    }
}
