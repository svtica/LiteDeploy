// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SchedulesForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Scheduled Updates window (agent of the MAJ-Logiciels panel): list of
//          the schedules, add, edit, delete, run now, logs; engine switch
//          between internal and Windows scheduled tasks (the tasks are created
//          or removed accordingly).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class SchedulesForm : Form
    {
        UpdateSchedules schedules;
        readonly List<String> defaultComputers;
        bool loading;

        public SchedulesForm(IEnumerable<String> defaultComputers)
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            this.defaultComputers = (defaultComputers ?? new String[0]).ToList();
            loading = true;
            windowsRadio.Checked = UserSettings.Current.SchedulerEngine == UpdateSchedules.WindowsTask;
            internalRadio.Checked = !windowsRadio.Checked;
            loading = false;
            Reload();
        }

        void Reload()
        {
            schedules = UpdateSchedules.Load();
            String selected = Selected?.Name;
            schedulesList.Items.Clear();
            DateTime now = DateTime.Now;
            foreach (UpdateSchedule s in schedules.Items)
            {
                DateTime? next = s.NextRun(now);
                ListViewItem item = new ListViewItem(new[]
                {
                    s.Name,
                    s.Command == "upgrade" ? Localization.T("SchedulesForm.Update", "Update") : Localization.T("SchedulesForm.Check", "Check"),
                    s.Describe(),
                    s.Computers.Count.ToString(),
                    s.Enabled ? (next.HasValue ? next.Value.ToString("yyyy-MM-dd HH:mm") : "") : Localization.T("SchedulesForm.Disabled", "Disabled"),
                    s.LastRun.HasValue ? s.LastRun.Value.ToString("yyyy-MM-dd HH:mm") + " - " + s.LastResult : ""
                }) { Tag = s };
                if (!s.Enabled) item.ForeColor = System.Drawing.SystemColors.GrayText;
                schedulesList.Items.Add(item);
                if (s.Name == selected) item.Selected = true;
            }
            UpdateButtons();
        }

        UpdateSchedule Selected
        {
            get { return schedulesList.SelectedItems.Count == 0 ? null : (UpdateSchedule)schedulesList.SelectedItems[0].Tag; }
        }

        void UpdateButtons()
        {
            editButton.Enabled = deleteButton.Enabled = runButton.Enabled = Selected != null;
        }

        void schedulesList_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        void addButton_Click(object sender, EventArgs e)
        {
            UpdateSchedule s = new UpdateSchedule { Name = "", Days = "Monday,Tuesday,Wednesday,Thursday,Friday", Time = "22:00", Computers = defaultComputers.ToList() };
            Edit(s, null);
        }

        void editButton_Click(object sender, EventArgs e)
        {
            if (Selected != null) Edit(Selected, Selected.Name);
        }

        void Edit(UpdateSchedule s, String originalName)
        {
            using (ScheduleEditForm editor = new ScheduleEditForm(s, schedules))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                UpdateSchedule edited = editor.Schedule;
                if (originalName != null)
                {
                    UpdateSchedule old = schedules.Find(originalName);
                    edited.LastRun = old?.LastRun;
                    edited.LastResult = old?.LastResult;
                    schedules.Items.Remove(old);
                }
                schedules.Items.Add(edited);
                SaveAndSync();
            }
        }

        void deleteButton_Click(object sender, EventArgs e)
        {
            UpdateSchedule s = Selected;
            if (s == null) return;
            if (MessageBox.Show(this, Localization.F("SchedulesForm.ConfirmDelete", "Delete the schedule \"{0}\"?", s.Name), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            schedules.Items.Remove(s);
            SaveAndSync();
        }

        void SaveAndSync()
        {
            try
            {
                schedules.Save();
                if (UserSettings.Current.SchedulerEngine == UpdateSchedules.WindowsTask)
                {
                    List<String> errors = schedules.SyncWindowsTasks(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    statusLabel.Text = errors.Count == 0
                        ? Localization.T("SchedulesForm.TasksUpdated", "Windows scheduled tasks updated (folder \\LiteDeploy).")
                        : String.Join(" | ", errors);
                }
                else
                {
                    statusLabel.Text = Localization.T("SchedulesForm.Saved", "Saved. The schedules run while LiteDeploy is open.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Reload();
        }

        void runButton_Click(object sender, EventArgs e)
        {
            UpdateSchedule s = Selected;
            if (s == null) return;
            if (s.Command == "upgrade" && MessageBox.Show(this,
                    Localization.F("SchedulesForm.ConfirmRun", "Run \"{0}\" now? The outdated packages of {1} computer(s) are updated and their applications closed.", s.Name, s.Computers.Count),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            runButton.Enabled = false;
            statusLabel.Text = Localization.F("SchedulesForm.Running", "\"{0}\" is running (see Logs)...", s.Name);
            new Thread(() =>
            {
                ScheduleRunner.Run(s);
                if (IsDisposed) return;
                BeginInvoke(new MethodInvoker(() => { statusLabel.Text = ""; Reload(); }));
            }) { IsBackground = true }.Start();
        }

        void logButton_Click(object sender, EventArgs e)
        {
            Directory.CreateDirectory(ScheduleRunner.LogDir);
            using (Process.Start(new ProcessStartInfo(ScheduleRunner.LogDir) { UseShellExecute = true })) { }
        }

        void Engine_Changed(object sender, EventArgs e)
        {
            if (loading || !((RadioButton)sender).Checked) return;
            String engine = windowsRadio.Checked ? UpdateSchedules.WindowsTask : UpdateSchedules.Internal;
            if (engine == UserSettings.Current.SchedulerEngine) return;
            try
            {
                UserSettings settings = UserSettings.Current.Clone();
                settings.SchedulerEngine = engine;
                UserSettings.Save(settings);
                if (engine == UpdateSchedules.Internal)
                {
                    UpdateSchedules.RemoveWindowsTasks();
                    statusLabel.Text = Localization.T("SchedulesForm.InternalOn", "Internal engine: the Windows tasks of LiteDeploy were removed.");
                }
                else
                {
                    SaveAndSync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
