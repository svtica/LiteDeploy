// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    DnsScanForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Reverse DNS scan of a /24 subnet.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net;

namespace LiteDeploy
{
    public partial class DnsScanForm : Form
    {
        private String Subnet;
        private int currentAddress;
        private List<Thread> lookUpThreads;
        public List<String> Hosts;
        static public bool abort;
        private Object HostNumLock;

        public DnsScanForm( String s)
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            Subnet = s;
            Hosts = new List<String>();
            lookUpThreads = new List<Thread>();
            ThreadStartTimer.Enabled = true;
            abort = false;
            HostNumLock = new Object();
        }

        public List<String> GetHosts()
        {
            lock (Hosts)
            {
                return new List<String>(Hosts);
            }
        }

        private void DnsScanForm_Load(object sender, EventArgs e)
        {
            
        }

        private void FastDNSLookUp()
        {
            int hostnum = 0;

            lock (HostNumLock)
            {
                hostnum = currentAddress;
            }

            try
            {
                IPAddress hostIPAddress = IPAddress.Parse(Subnet + hostnum);
                IPHostEntry hostInfo = Dns.GetHostEntry(hostIPAddress);

                // Get the IP address list that resolves to the host names contained in 
                // the Alias property.
                IPAddress[] address = hostInfo.AddressList;

                if ( address.Length > 0 )
                {
                    // Several lookup threads add to the same list
                    lock (Hosts)
                    {
                        Hosts.Add(hostInfo.HostName.ToUpper().Substring(0, hostInfo.HostName.IndexOf('.')));
                    }
                }
            }
            catch
            {
                // No Host Found for IP
            }
        }

        private void ThreadStartTimer_Tick(object sender, EventArgs e)
        {
            if (abort)
            {
                ThreadStartTimer.Enabled = false;
                foreach (Thread th in lookUpThreads)
                    th.Abort();
                Close();
            }

            if (currentAddress == 256)
            {
                label1.Text = Localization.T("DnsScanForm.Waiting", "Waiting for threads to exit...");
                bool working = false;

                foreach (Thread th in lookUpThreads)
                {
                    if (th.ThreadState == ThreadState.Running)
                    {
                        working = true;
                        break;
                    }
                }

                if (!working)
                {
                    progressBar1.PerformStep();
                    Close();
                }
            }
            else
            {
                Thread t = new Thread(new ThreadStart(FastDNSLookUp));
                t.Start();

                lookUpThreads.Add(t);

                lock (HostNumLock)
                {
                    currentAddress++;
                }

                progressBar1.PerformStep();
            }
        }
    }
}
