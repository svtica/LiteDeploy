// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UserMessageForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: The message to send to the users logged on to the computers: text
//          (at most UserMessage.MaxLength characters, with a counter), ready
//          texts to start from, display time (0: until the user closes it).
//          ComputerActions.SendMessage asks for the confirmation and sends it.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class UserMessageForm : Form
    {
        readonly Label introLabel = new Label();
        readonly Label presetLabel = new Label();
        readonly ComboBox presetBox = new ComboBox();
        readonly TextBox textBox = new TextBox();
        readonly Label counterLabel = new Label();
        readonly Label secondsLabel = new Label();
        readonly NumericUpDown secondsBox = new NumericUpDown();
        readonly Button sendButton = new Button();
        readonly Button cancelButton = new Button();

        /// <param name="computerCount">Number of computers, for the text at the top.</param>
        /// <param name="presets">Ready texts offered in the list (already translated).</param>
        /// <param name="text">The text to start with (a ready text, or empty).</param>
        public UserMessageForm(int computerCount, IList<String> presets, String text)
        {
            Text = Localization.T("UserMessageForm.Text", "Send a message");
            ClientSize = new Size(460, 300);
            MinimumSize = new Size(400, 300);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            introLabel.Text = Localization.F("UserMessageForm.Intro", "Message to the users logged on to {0} computer(s):", computerCount);
            introLabel.SetBounds(12, 12, 436, 18);
            introLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            introLabel.AutoEllipsis = true;

            presetLabel.Text = Localization.T("UserMessageForm.Preset", "Ready text:");
            presetLabel.SetBounds(12, 39, 110, 18);
            presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetBox.SetBounds(126, 36, 322, 23);
            presetBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            presetBox.Items.Add(Localization.T("UserMessageForm.NoPreset", "(choose a ready text)"));
            foreach (String p in presets ?? new String[0]) presetBox.Items.Add(p);
            presetBox.SelectedIndex = 0;
            presetBox.SelectedIndexChanged += (s, e) =>
            {
                if (presetBox.SelectedIndex > 0) textBox.Text = (String)presetBox.SelectedItem;
            };

            textBox.Multiline = true;
            textBox.AcceptsReturn = true;
            textBox.ScrollBars = ScrollBars.Vertical;
            textBox.MaxLength = UserMessage.MaxLength;
            textBox.SetBounds(12, 66, 436, 130);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBox.Text = text ?? "";
            textBox.TextChanged += (s, e) => UpdateCounter();

            counterLabel.SetBounds(298, 200, 150, 18);
            counterLabel.TextAlign = ContentAlignment.TopRight;
            counterLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            secondsLabel.Text = Localization.T("UserMessageForm.Seconds", "Display time in seconds (0: until closed):");
            secondsLabel.SetBounds(12, 228, 300, 18);
            secondsLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            secondsBox.Minimum = 0;
            secondsBox.Maximum = UserMessage.MaxSeconds;
            secondsBox.Increment = 60;
            secondsBox.Value = UserMessage.DefaultSeconds;
            secondsBox.SetBounds(318, 225, 80, 23);
            secondsBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            sendButton.Text = Localization.T("UserMessageForm.Send", "Send");
            sendButton.SetBounds(292, 263, 75, 25);
            sendButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            sendButton.DialogResult = DialogResult.OK;
            AcceptButton = null;   // Enter makes a new line in the text

            cancelButton.Text = Localization.T("Prompt.Cancel", "Cancel");
            cancelButton.SetBounds(373, 263, 75, 25);
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancelButton.DialogResult = DialogResult.Cancel;
            CancelButton = cancelButton;

            Controls.AddRange(new Control[] { introLabel, presetLabel, presetBox, textBox, counterLabel, secondsLabel, secondsBox, sendButton, cancelButton });
            Theme.Apply(this);
            UpdateCounter();
            Shown += (s, e) => { textBox.Focus(); textBox.SelectionStart = textBox.TextLength; };
        }

        /// <summary>The text to send (UserMessage.Clean).</summary>
        public String MessageText { get { return UserMessage.Clean(textBox.Text); } }

        /// <summary>Display time in seconds; 0: until the user closes the message.</summary>
        public int Seconds { get { return (int)secondsBox.Value; } }

        void UpdateCounter()
        {
            counterLabel.Text = Localization.F("UserMessageForm.Counter", "{0} / {1} characters", textBox.TextLength, UserMessage.MaxLength);
            sendButton.Enabled = UserMessage.Validate(textBox.Text) == null;
        }
    }
}
