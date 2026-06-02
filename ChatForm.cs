using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public class ChatForm : Form
    {
        private readonly AgentController agent;
        private readonly RichTextBox history;
        private readonly TextBox input;
        private readonly Button sendButton;
        private readonly Panel actionPanel;
        private readonly Label actionLabel;
        private readonly ListView trackList;
        private readonly Button confirmButton;
        private readonly Button queueLastButton;
        private readonly Button queueNextButton;
        private readonly Button playlistButton;
        private readonly Button cancelButton;
        private PendingAction pendingAction;

        public bool AllowClose;

        public ChatForm(AgentController agent, PluginSettings settings)
        {
            this.agent = agent;

            Text = "MusicBee AI Agent";
            Width = 860;
            Height = 680;
            MinimumSize = new Size(620, 460);
            StartPosition = FormStartPosition.CenterScreen;

            history = new RichTextBox();
            history.ReadOnly = true;
            history.Dock = DockStyle.Fill;
            history.BorderStyle = BorderStyle.FixedSingle;

            input = new TextBox();
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            input.Left = 8;
            input.Top = 8;
            input.Width = 720;
            input.KeyDown += InputKeyDown;

            sendButton = new Button();
            sendButton.Text = "Send";
            sendButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            sendButton.Left = 736;
            sendButton.Top = 6;
            sendButton.Width = 88;
            sendButton.Click += SendClicked;

            Panel inputPanel = new Panel();
            inputPanel.Dock = DockStyle.Bottom;
            inputPanel.Height = 42;
            inputPanel.Controls.Add(input);
            inputPanel.Controls.Add(sendButton);
            inputPanel.Resize += delegate
            {
                sendButton.Left = inputPanel.Width - sendButton.Width - 8;
                input.Width = sendButton.Left - 16;
            };

            actionPanel = new Panel();
            actionPanel.Dock = DockStyle.Bottom;
            actionPanel.Height = 230;
            actionPanel.Visible = false;
            actionPanel.BorderStyle = BorderStyle.FixedSingle;

            actionLabel = new Label();
            actionLabel.AutoEllipsis = true;
            actionLabel.Left = 8;
            actionLabel.Top = 8;
            actionLabel.Width = 820;
            actionLabel.Height = 40;
            actionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

            trackList = new ListView();
            trackList.CheckBoxes = true;
            trackList.FullRowSelect = true;
            trackList.GridLines = true;
            trackList.View = View.Details;
            trackList.Left = 8;
            trackList.Top = 52;
            trackList.Width = 820;
            trackList.Height = 124;
            trackList.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            trackList.Columns.Add("Artist", 150);
            trackList.Columns.Add("Title", 210);
            trackList.Columns.Add("Album", 190);
            trackList.Columns.Add("Time", 58);
            trackList.Columns.Add("Why", 190);
            trackList.ItemChecked += delegate { RefreshActionSummary(); };

            confirmButton = AddActionButton("Confirm", 8, ConfirmClicked);
            queueLastButton = AddActionButton("Queue Last", 116, delegate { ExecutePending("queue_tracks_last"); });
            queueNextButton = AddActionButton("Queue Next", 224, delegate { ExecutePending("queue_tracks_next"); });
            playlistButton = AddActionButton("Create Playlist", 332, delegate { ExecutePending("create_playlist"); });
            cancelButton = AddActionButton("Cancel", 458, delegate { ClearPendingAction(); });

            actionPanel.Controls.Add(actionLabel);
            actionPanel.Controls.Add(trackList);
            actionPanel.Controls.Add(confirmButton);
            actionPanel.Controls.Add(queueLastButton);
            actionPanel.Controls.Add(queueNextButton);
            actionPanel.Controls.Add(playlistButton);
            actionPanel.Controls.Add(cancelButton);
            actionPanel.Resize += delegate
            {
                int top = actionPanel.Height - 34;
                confirmButton.Top = top;
                queueLastButton.Top = top;
                queueNextButton.Top = top;
                playlistButton.Top = top;
                cancelButton.Top = top;
                trackList.Height = Math.Max(70, top - trackList.Top - 8);
            };

            Controls.Add(history);
            Controls.Add(actionPanel);
            Controls.Add(inputPanel);

            Append("System", "Configured model: " + settings.Model + ". Privacy mode: " + settings.PrivacyMode + ".");
        }

        private Button AddActionButton(string text, int left, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.Left = left;
            button.Top = 190;
            button.Width = text.Length > 10 ? 118 : 100;
            button.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            button.Click += handler;
            return button;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        private void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                Send();
            }
        }

        private void SendClicked(object sender, EventArgs e)
        {
            Send();
        }

        private void Send()
        {
            string text = input.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            input.Text = "";
            ClearPendingAction();
            Append("User", text);
            SetBusy(true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                AgentResult result = agent.Send(text);
                BeginInvoke((MethodInvoker)delegate
                {
                    SetBusy(false);
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        Append("Error", result.Error);
                    }
                    else
                    {
                        Append("AI", result.Message);
                        if (result.PendingAction != null)
                        {
                            ShowPendingAction(result.PendingAction);
                        }
                    }
                });
            });
        }

        private void ConfirmClicked(object sender, EventArgs e)
        {
            ExecutePending(null);
        }

        private void ExecutePending(string overrideType)
        {
            if (pendingAction == null)
            {
                return;
            }

            List<TrackInfo> selected = GetSelectedTracks();
            PendingAction action = pendingAction;
            ClearPendingAction();
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string message = agent.Execute(action, overrideType, selected);
                BeginInvoke((MethodInvoker)delegate
                {
                    SetBusy(false);
                    Append("System", message);
                });
            });
        }

        private void ShowPendingAction(PendingAction action)
        {
            pendingAction = action;
            actionPanel.Visible = true;
            trackList.Items.Clear();
            SetActionButtonsEnabled(action.IsValid);

            if (!action.IsValid)
            {
                actionLabel.Text = "Rejected action: " + action.ValidationError;
                return;
            }

            for (int i = 0; i < action.Tracks.Count; i++)
            {
                TrackInfo track = action.Tracks[i];
                ListViewItem item = new ListViewItem(track.Artist);
                item.Checked = true;
                item.Tag = track;
                item.SubItems.Add(track.Title);
                item.SubItems.Add(track.Album);
                item.SubItems.Add(FormatDuration(track.DurationSeconds));
                item.SubItems.Add(track.ScoreReason);
                trackList.Items.Add(item);
            }

            RefreshActionSummary();
        }

        private void RefreshActionSummary()
        {
            if (pendingAction == null || !pendingAction.IsValid)
            {
                return;
            }

            List<TrackInfo> selected = GetSelectedTracks();
            int totalSeconds = 0;
            foreach (TrackInfo track in selected)
            {
                totalSeconds += track.DurationSeconds;
            }

            actionLabel.Text = pendingAction.Action.Type + ": " + pendingAction.Action.Title +
                " | selected " + selected.Count + " track(s), " + FormatDuration(totalSeconds) +
                " | " + pendingAction.Action.Explanation;
            SetActionButtonsEnabled(selected.Count > 0);
        }

        private List<TrackInfo> GetSelectedTracks()
        {
            List<TrackInfo> selected = new List<TrackInfo>();
            foreach (ListViewItem item in trackList.Items)
            {
                if (item.Checked && item.Tag is TrackInfo)
                {
                    selected.Add((TrackInfo)item.Tag);
                }
            }
            return selected;
        }

        private void SetActionButtonsEnabled(bool enabled)
        {
            confirmButton.Enabled = enabled;
            queueLastButton.Enabled = enabled;
            queueNextButton.Enabled = enabled;
            playlistButton.Enabled = enabled;
        }

        private void ClearPendingAction()
        {
            pendingAction = null;
            actionPanel.Visible = false;
            actionLabel.Text = "";
            trackList.Items.Clear();
        }

        private void SetBusy(bool busy)
        {
            sendButton.Enabled = !busy;
            input.Enabled = !busy;
            bool actionEnabled = !busy && pendingAction != null && pendingAction.IsValid && GetSelectedTracks().Count > 0;
            SetActionButtonsEnabled(actionEnabled);
            if (busy)
            {
                Append("System", "Working...");
            }
        }

        private void Append(string role, string text)
        {
            history.SelectionFont = new Font(history.Font, FontStyle.Bold);
            history.AppendText(role + ": ");
            history.SelectionFont = history.Font;
            history.AppendText((text ?? "") + Environment.NewLine + Environment.NewLine);
            history.ScrollToCaret();
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0)
            {
                return "";
            }

            int hours = seconds / 3600;
            int minutes = (seconds % 3600) / 60;
            int sec = seconds % 60;
            if (hours > 0)
            {
                return hours + ":" + minutes.ToString("00") + ":" + sec.ToString("00");
            }

            return minutes + ":" + sec.ToString("00");
        }
    }
}
