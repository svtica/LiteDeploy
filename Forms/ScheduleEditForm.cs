// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ScheduleEditForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Edits one scheduled check or update: name, action, once or on some
//          days at a time, Wake-on-LAN first, computers.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class ScheduleEditForm : Form
    {
        public UpdateSchedule Schedule { get; private set; }
        readonly UpdateSchedules all;
        readonly String originalName;

        CheckBox[] DayBoxes
        {
            get { return new[] { dayMonday, dayTuesday, dayWednesday, dayThursday, dayFriday, daySaturday, daySunday }; }
        }

        static readonly DayOfWeek[] Week = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };

        public ScheduleEditForm(UpdateSchedule schedule, UpdateSchedules all)
        {
            InitializeComponent();
            Localization.Apply(this);
            // Day names of the interface language
            for (int i = 0; i < 7; i++) DayBoxes[i].Text = CultureInfo.CurrentUICulture.DateTimeFormat.GetAbbreviatedDayName(Week[i]);
            Theme.Apply(this);
            this.all = all;
            originalName = schedule.Name;

            nameBox.Text = schedule.Name ?? "";
            actionCombo.SelectedIndex = schedule.Command == "upgrade" ? 1 : 0;
            List<DayOfWeek> days = schedule.DayList.ToList();
            weeklyRadio.Checked = days.Count > 0 || !schedule.OnceAt.HasValue;
            onceRadio.Checked = !weeklyRadio.Checked;
            for (int i = 0; i < 7; i++) DayBoxes[i].Checked = days.Contains(Week[i]);
            TimeSpan time;
            if (!TimeSpan.TryParse(schedule.Time, CultureInfo.InvariantCulture, out time)) time = new TimeSpan(22, 0, 0);
            timePicker.Value = DateTime.Today + time;
            oncePicker.Value = schedule.OnceAt ?? DateTime.Today.AddDays(1) + time;
            wakeCheck.Checked = schedule.WakeFirst;
            enabledCheck.Checked = schedule.Enabled;
            computersBox.Text = String.Join(Environment.NewLine, schedule.Computers);
            Recurrence_Changed(null, EventArgs.Empty);
        }

        void Recurrence_Changed(object sender, EventArgs e)
        {
            oncePicker.Enabled = onceRadio.Checked;
            foreach (CheckBox day in DayBoxes) day.Enabled = weeklyRadio.Checked;
            timePicker.Enabled = weeklyRadio.Checked;
        }

        void okButton_Click(object sender, EventArgs e)
        {
            String name = nameBox.Text.Trim();
            String error = null;
            if (!UpdateSchedules.IsValidName(name))
                error = Localization.T("ScheduleEditForm.BadName", "Name: letters, digits, spaces and . - _ only (64 at most).");
            else if (!String.Equals(name, originalName, StringComparison.OrdinalIgnoreCase) && all.Find(name) != null)
                error = Localization.T("ScheduleEditForm.NameUsed", "Another schedule has this name.");
            else if (weeklyRadio.Checked && !DayBoxes.Any(d => d.Checked))
                error = Localization.T("ScheduleEditForm.NoDay", "Check at least one day.");
            else if (onceRadio.Checked && oncePicker.Value <= DateTime.Now)
                error = Localization.T("ScheduleEditForm.PastDate", "Choose a date and time to come.");

            List<String> computers = computersBox.Lines.Select(l => l.Trim()).Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (error == null && computers.Count == 0)
                error = Localization.T("ScheduleEditForm.NoComputer", "Add at least one computer.");
            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Schedule = new UpdateSchedule
            {
                Name = name,
                Command = actionCombo.SelectedIndex == 1 ? "upgrade" : "scan",
                Enabled = enabledCheck.Checked,
                Days = weeklyRadio.Checked ? String.Join(",", Week.Where((d, i) => DayBoxes[i].Checked)) : "",
                Time = timePicker.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
                OnceAt = onceRadio.Checked ? oncePicker.Value : (DateTime?)null,
                WakeFirst = wakeCheck.Checked,
                Computers = computers
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
