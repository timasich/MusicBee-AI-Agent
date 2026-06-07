using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public class MusicBeeTheme
    {
        public Color BackColor = SystemColors.Control;
        public Color ForeColor = SystemColors.ControlText;
        public Color InputBackColor = SystemColors.Window;
        public Color InputForeColor = SystemColors.WindowText;

        public void Apply(Control root)
        {
            if (root == null)
            {
                return;
            }

            ApplyControl(root);
            foreach (Control child in root.Controls)
            {
                Apply(child);
            }
        }

        private void ApplyControl(Control control)
        {
            if (control is TextBox || control is RichTextBox || control is ListView)
            {
                control.BackColor = InputBackColor;
                control.ForeColor = InputForeColor;
                return;
            }

            control.BackColor = BackColor;
            control.ForeColor = ForeColor;
        }
    }

    public class ChatHistoryBox : RichTextBox
    {
        public ChatHistoryBox()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.FixedSingle;
            Dock = DockStyle.Fill;
            HideSelection = false;
            ShortcutsEnabled = true;
            TabStop = true;
            Multiline = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            HideSelection = false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.A))
            {
                SelectAll();
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
            {
                if (!string.IsNullOrEmpty(SelectedText))
                {
                    Clipboard.SetText(SelectedText);
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.A) || keyData == (Keys.Control | Keys.C))
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }
    }

    public class AgentChatControl : UserControl
    {
        private readonly AgentController agent;
        private readonly ChatHistoryBox history;
        private readonly TextBox input;
        private readonly Button sendButton;
        private readonly Button newChatButton;
        private readonly Button chatsButton;
        private readonly Button traceButton;
        private readonly Label chatTitle;
        private readonly TextBox traceBox;
        private readonly Panel actionPanel;
        private readonly Label actionLabel;
        private readonly ListView trackList;
        private readonly Button confirmButton;
        private readonly Button queueLastButton;
        private readonly Button queueNextButton;
        private readonly Button playlistButton;
        private readonly Button cancelButton;
        private PendingAction pendingAction;
        private bool resizingActionPanel;
        private int actionPanelResizeStartY;
        private int actionPanelResizeStartHeight;
        private int sortColumn = -1;
        private bool sortAscending = true;
        private CancellationTokenSource currentRequestCancellation;

        public AgentChatControl(AgentController agent, PluginSettings settings, MusicBeeTheme theme)
        {
            this.agent = agent;

            Dock = DockStyle.Fill;

            history = new ChatHistoryBox();
            history.KeyDown += CopyableControlKeyDown;

            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 36;

            chatsButton = new Button();
            chatsButton.Text = "Chats";
            chatsButton.Left = 8;
            chatsButton.Top = 6;
            chatsButton.Width = 80;
            chatsButton.Click += ChatsClicked;

            newChatButton = new Button();
            newChatButton.Text = "New Chat";
            newChatButton.Left = 96;
            newChatButton.Top = 6;
            newChatButton.Width = 92;
            newChatButton.Click += NewChatClicked;

            chatTitle = new Label();
            chatTitle.Left = 204;
            chatTitle.Top = 10;
            chatTitle.Width = 500;
            chatTitle.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            chatTitle.Text = "Current chat";

            traceButton = new Button();
            traceButton.Text = "Show Trace";
            traceButton.Top = 6;
            traceButton.Width = 92;
            traceButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            traceButton.Visible = false;
            traceButton.Click += ToggleTraceClicked;

            topPanel.Controls.Add(chatsButton);
            topPanel.Controls.Add(newChatButton);
            topPanel.Controls.Add(chatTitle);
            topPanel.Controls.Add(traceButton);
            topPanel.Resize += delegate
            {
                traceButton.Left = topPanel.Width - traceButton.Width - 8;
                chatTitle.Width = Math.Max(120, traceButton.Left - chatTitle.Left - 8);
            };

            traceBox = new TextBox();
            traceBox.Dock = DockStyle.Bottom;
            traceBox.Height = 130;
            traceBox.Multiline = true;
            traceBox.ScrollBars = ScrollBars.Vertical;
            traceBox.ReadOnly = true;
            traceBox.Visible = false;
            traceBox.Font = new Font(FontFamily.GenericMonospace, 8.5f);
            traceBox.KeyDown += CopyableControlKeyDown;

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
            actionPanel.Height = 320;
            actionPanel.Visible = false;
            actionPanel.BorderStyle = BorderStyle.FixedSingle;
            actionPanel.MouseDown += ActionPanelMouseDown;
            actionPanel.MouseMove += ActionPanelMouseMove;
            actionPanel.MouseUp += ActionPanelMouseUp;

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
            trackList.ColumnClick += TrackListColumnClick;

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
            Controls.Add(traceBox);
            Controls.Add(inputPanel);
            Controls.Add(topPanel);

            if (theme != null)
            {
                theme.Apply(this);
            }

            Append("System", "Configured model: " + settings.Model + ".");
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

        private void ActionPanelMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y <= 8)
            {
                resizingActionPanel = true;
                actionPanelResizeStartY = PointToClient(actionPanel.PointToScreen(e.Location)).Y;
                actionPanelResizeStartHeight = actionPanel.Height;
                actionPanel.Capture = true;
            }
        }

        private void ActionPanelMouseMove(object sender, MouseEventArgs e)
        {
            if (!resizingActionPanel)
            {
                actionPanel.Cursor = e.Y <= 8 ? Cursors.SizeNS : Cursors.Default;
                return;
            }

            int currentY = PointToClient(actionPanel.PointToScreen(e.Location)).Y;
            int delta = actionPanelResizeStartY - currentY;
            int maxHeight = Math.Max(220, ClientSize.Height - 100);
            actionPanel.Height = Math.Max(180, Math.Min(maxHeight, actionPanelResizeStartHeight + delta));
        }

        private void ActionPanelMouseUp(object sender, MouseEventArgs e)
        {
            resizingActionPanel = false;
            actionPanel.Capture = false;
        }

        private void TrackListColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (sortColumn == e.Column)
            {
                sortAscending = !sortAscending;
            }
            else
            {
                sortColumn = e.Column;
                sortAscending = true;
            }
            trackList.ListViewItemSorter = new TrackListComparer(sortColumn, sortAscending);
            trackList.Sort();
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
            if (currentRequestCancellation != null)
            {
                currentRequestCancellation.Cancel();
                AppendLiveTrace("Cancellation requested by user.");
                return;
            }
            Send();
        }

        private void NewChatClicked(object sender, EventArgs e)
        {
            ClearPendingAction();
            ClearTrace();
            string title = agent.NewConversation();
            history.Clear();
            chatTitle.Text = string.IsNullOrEmpty(title) ? "New chat" : title;
            Append("System", "New chat started.");
        }

        private void ChatsClicked(object sender, EventArgs e)
        {
            using (ChatListForm form = new ChatListForm(agent.ListConversations(), agent))
            {
                Form owner = FindForm();
                DialogResult dialogResult = owner == null ? form.ShowDialog() : form.ShowDialog(owner);
                if (dialogResult != DialogResult.OK || string.IsNullOrEmpty(form.SelectedConversationId))
                {
                    return;
                }
                ClearPendingAction();
                ClearTrace();
                List<ConversationMessage> messages = agent.OpenConversation(form.SelectedConversationId);
                history.Clear();
                chatTitle.Text = form.SelectedConversationTitle;
                foreach (ConversationMessage message in messages)
                {
                    Append(message.Role, message.Text);
                }
            }
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
            ClearTrace();
            SetBusy(true);

            currentRequestCancellation = new CancellationTokenSource();
            CancellationToken token = currentRequestCancellation.Token;
            ThreadPool.QueueUserWorkItem(delegate
            {
                AgentResult result = agent.Send(text, token, AppendLiveTrace);
                BeginInvoke((MethodInvoker)delegate
                {
                    SetBusy(false);
                    currentRequestCancellation = null;
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        Append("Error", result.Error);
                        ShowTrace(result.OrchestratorTrace);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(result.ChatTitle))
                        {
                            chatTitle.Text = result.ChatTitle;
                        }
                        Append("AI", result.Message);
                        ShowTrace(result.OrchestratorTrace);
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

            if (action.Action.Type == "delete_ai_playlist")
            {
                actionLabel.Text = action.Action.Type + ": " + action.Action.PlaylistName + " | " + action.Action.Explanation;
                confirmButton.Enabled = true;
                queueLastButton.Enabled = false;
                queueNextButton.Enabled = false;
                playlistButton.Enabled = false;
                return;
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
                " | selected " + selected.Count + " track(s), " + FormatDuration(totalSeconds) + FormatTargetDuration(pendingAction.TargetDurationSeconds, totalSeconds) +
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
            sendButton.Enabled = true;
            sendButton.Text = busy ? "Cancel" : "Send";
            input.Enabled = !busy;
            bool actionEnabled = !busy && pendingAction != null && pendingAction.IsValid && GetSelectedTracks().Count > 0;
            if (!busy && pendingAction != null && pendingAction.IsValid && pendingAction.Action != null && pendingAction.Action.Type == "delete_ai_playlist")
            {
                confirmButton.Enabled = true;
                queueLastButton.Enabled = false;
                queueNextButton.Enabled = false;
                playlistButton.Enabled = false;
                return;
            }
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

        private void ToggleTraceClicked(object sender, EventArgs e)
        {
            traceBox.Visible = !traceBox.Visible;
            traceButton.Text = traceBox.Visible ? "Hide Trace" : "Show Trace";
        }

        private void ShowTrace(List<string> trace)
        {
            if (trace == null || trace.Count == 0)
            {
                ClearTrace();
                return;
            }

            traceBox.Text = string.Join(Environment.NewLine, trace.ToArray());
            traceButton.Visible = true;
            traceBox.Visible = false;
            traceButton.Text = "Show Trace";
        }

        private void ClearTrace()
        {
            traceBox.Text = "";
            traceBox.Visible = false;
            traceButton.Visible = false;
            traceButton.Text = "Show Trace";
        }

        private void AppendLiveTrace(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            BeginInvoke((MethodInvoker)delegate
            {
                if (traceBox.TextLength == 0)
                {
                    traceBox.Text = line;
                }
                else
                {
                    traceBox.AppendText(Environment.NewLine + line);
                }
                traceButton.Visible = true;
                if (!traceBox.Visible)
                {
                    traceButton.Text = "Show Trace";
                }
            });
        }

        private void CopyableControlKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                string selected = "";
                if (sender is RichTextBox)
                {
                    selected = ((RichTextBox)sender).SelectedText;
                }
                else if (sender is TextBox)
                {
                    selected = ((TextBox)sender).SelectedText;
                }
                if (!string.IsNullOrEmpty(selected))
                {
                    Clipboard.SetText(selected);
                    e.SuppressKeyPress = true;
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                if (!string.IsNullOrEmpty(history.SelectedText))
                {
                    Clipboard.SetText(history.SelectedText);
                    return true;
                }
                if (!string.IsNullOrEmpty(traceBox.SelectedText))
                {
                    Clipboard.SetText(traceBox.SelectedText);
                    return true;
                }
                if (!string.IsNullOrEmpty(input.SelectedText))
                {
                    Clipboard.SetText(input.SelectedText);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
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

        private static string FormatTargetDuration(int targetSeconds, int totalSeconds)
        {
            if (targetSeconds <= 0)
            {
                return "";
            }
            int delta = totalSeconds - targetSeconds;
            return " / target " + FormatDuration(targetSeconds) + " (" + (delta >= 0 ? "+" : "-") + FormatDuration(Math.Abs(delta)) + ")";
        }

        private sealed class TrackListComparer : IComparer
        {
            private readonly int column;
            private readonly bool ascending;

            public TrackListComparer(int column, bool ascending)
            {
                this.column = column;
                this.ascending = ascending;
            }

            public int Compare(object x, object y)
            {
                ListViewItem left = x as ListViewItem;
                ListViewItem right = y as ListViewItem;
                string a = GetValue(left, column);
                string b = GetValue(right, column);
                int result;
                if (column == 3)
                {
                    result = ParseDuration(a).CompareTo(ParseDuration(b));
                }
                else
                {
                    result = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                }
                return ascending ? result : -result;
            }

            private static string GetValue(ListViewItem item, int column)
            {
                if (item == null || column < 0 || column >= item.SubItems.Count)
                {
                    return "";
                }
                return item.SubItems[column].Text;
            }

            private static int ParseDuration(string value)
            {
                string[] parts = (value ?? "").Split(':');
                int total = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    int part;
                    if (!int.TryParse(parts[i], out part))
                    {
                        return 0;
                    }
                    total = total * 60 + part;
                }
                return total;
            }
        }
    }

    public class ChatForm : Form
    {
        public bool AllowClose;

        public ChatForm(AgentController agent, PluginSettings settings, MusicBeeTheme theme)
        {
            Text = "MusicBee AI Agent";
            Width = 860;
            Height = 680;
            MinimumSize = new Size(620, 460);
            StartPosition = FormStartPosition.CenterScreen;

            AgentChatControl control = new AgentChatControl(agent, settings, theme);
            Controls.Add(control);
            if (theme != null)
            {
                theme.Apply(this);
            }
        }

        public ChatForm(AgentController agent, PluginSettings settings)
            : this(agent, settings, null)
        {
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
    }
}
