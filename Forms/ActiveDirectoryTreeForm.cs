// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ActiveDirectoryTreeForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Tree of the Active Directory organizational units (not used).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class ActiveDirectoryTreeForm : Form
    {
        public ActiveDirectoryTreeForm()
        {
            InitializeComponent();
        }

        private void ActiveDirectoryTreeForm_Load(object sender, EventArgs e)
        {
            treeView1.Nodes.Clear();
            treeView1.Nodes.Add(Environment.UserDomainName);

            for (int i = 0; i < ActiveDirectoryCache.OUs.Count; i++)
            {
                for (int j = ActiveDirectoryCache.OUs[i].Count - 1; j >= 0; j--)
                {
                    String Key = ActiveDirectoryCache.OUs[i][j];

                    if (treeView1.Nodes.ContainsKey(Key))
                    {
                        continue;
                    }

                    else
                    {
                        treeView1.Nodes[0].Nodes.Add(Key, Key);
                    }
                }
            }
        }
    }
}
