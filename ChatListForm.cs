using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public class ChatListForm : Form
    {
        private readonly AgentController agent;
        private readonly ListView list;
        private readonly Button openButton;
        private readonly Button renameButton;
        private readonly Button cancelButton;

        public string SelectedConversationId;
        public string SelectedConversationTitle;

        public ChatListForm(List<ConversationSummary> conversations, AgentController agent)
        {
            this.agent = agent;
            Text = "MusicBee AI Agent Chats";
            Width = 620;
            Height = 420;
            MinimumSize = new Size(420, 260);
            StartPosition = FormStartPosition.CenterParent;

            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = true;
            list.Columns.Add("Chat", 220);
            list.Columns.Add("Updated", 140);
            list.Columns.Add("Last message", 240);
            list.DoubleClick += delegate { OpenSelected(); };

            foreach (ConversationSummary conversation in conversations ?? new List<ConversationSummary>())
            {
                ListViewItem item = new ListViewItem(conversation.Title);
                item.SubItems.Add(conversation.UpdatedAt);
                item.SubItems.Add(Shorten(conversation.Preview));
                item.Tag = conversation;
                list.Items.Add(item);
            }

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 42;

            openButton = new Button();
            openButton.Text = "Open";
            openButton.Left = 410;
            openButton.Top = 8;
            openButton.Width = 82;
            openButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            openButton.Click += delegate { OpenSelected(); };

            renameButton = new Button();
            renameButton.Text = "Rename";
            renameButton.Left = 320;
            renameButton.Top = 8;
            renameButton.Width = 82;
            renameButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            renameButton.Click += delegate { RenameSelected(); };

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 500;
            cancelButton.Top = 8;
            cancelButton.Width = 82;
            cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            cancelButton.DialogResult = DialogResult.Cancel;

            bottom.Controls.Add(renameButton);
            bottom.Controls.Add(openButton);
            bottom.Controls.Add(cancelButton);
            bottom.Resize += delegate
            {
                cancelButton.Left = bottom.Width - cancelButton.Width - 12;
                openButton.Left = cancelButton.Left - openButton.Width - 8;
                renameButton.Left = openButton.Left - renameButton.Width - 8;
            };

            Controls.Add(list);
            Controls.Add(bottom);
        }

        private void RenameSelected()
        {
            if (list.SelectedItems.Count == 0)
            {
                return;
            }

            ListViewItem selected = list.SelectedItems[0];
            ConversationSummary conversation = selected.Tag as ConversationSummary;
            if (conversation == null)
            {
                return;
            }

            using (RenameChatForm form = new RenameChatForm(conversation.Title))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                agent.RenameConversation(conversation.Id, form.ChatTitle);
                conversation.Title = form.ChatTitle;
                selected.Text = form.ChatTitle;
            }
        }

        private void OpenSelected()
        {
            if (list.SelectedItems.Count == 0)
            {
                return;
            }

            ConversationSummary conversation = list.SelectedItems[0].Tag as ConversationSummary;
            if (conversation == null)
            {
                return;
            }

            SelectedConversationId = conversation.Id;
            SelectedConversationTitle = conversation.Title;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string Shorten(string value)
        {
            value = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            return value.Length > 120 ? value.Substring(0, 120) + "..." : value;
        }
    }

    public class RenameChatForm : Form
    {
        private readonly TextBox titleBox;

        public string ChatTitle
        {
            get { return titleBox.Text.Trim(); }
        }

        public RenameChatForm(string currentTitle)
        {
            Text = "Rename Chat";
            Width = 420;
            Height = 140;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            titleBox = new TextBox();
            titleBox.Left = 12;
            titleBox.Top = 14;
            titleBox.Width = 382;
            titleBox.Text = currentTitle ?? "";

            Button ok = new Button();
            ok.Text = "OK";
            ok.Left = 212;
            ok.Top = 52;
            ok.Width = 86;
            ok.Click += delegate
            {
                if (ChatTitle.Length == 0)
                {
                    MessageBox.Show(this, "Chat title cannot be empty.", "MusicBee AI Agent");
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Left = 304;
            cancel.Top = 52;
            cancel.Width = 86;
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(titleBox);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
