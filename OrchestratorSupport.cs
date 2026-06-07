using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace MusicBeePlugin
{
    public class LastPlanContext
    {
        public string ProposalId;
        public string OriginalRequest;
        public string IntentSummary;
        public string DiagnosticsSummary;
        public string VerificationSummary;
        public string TrackSnapshot;
        public int SelectedTrackCount;
        public int TotalDurationSeconds;
        public int TargetDurationSeconds;
    }

    public class PlaylistProposalRecord
    {
        public string Id;
        public string CreatedAt;
        public string OriginalRequest;
        public string IntentSummary;
        public string DiagnosticsSummary;
        public string VerificationSummary;
        public string TrackSnapshot;
        public int SelectedTrackCount;
        public int TotalDurationSeconds;
        public int TargetDurationSeconds;
    }

    public class ConversationSummary
    {
        public string Id;
        public string Title;
        public string UpdatedAt;
        public string Preview;

        public override string ToString()
        {
            return Title + "  " + UpdatedAt;
        }
    }

    public class ConversationMessage
    {
        public string Role;
        public string Text;
        public string CreatedAt;
    }

    public class ConversationStore
    {
        private readonly string filePath;
        private string activeConversationId;
        private LastPlanContext lastPlan;

        public ConversationStore(string dataPath)
        {
            Directory.CreateDirectory(dataPath);
            filePath = Path.Combine(dataPath, "conversations.xml");
            EnsureLoaded();
        }

        public string ActiveConversationId
        {
            get { EnsureLoaded(); return activeConversationId; }
        }

        public LastPlanContext LastPlan
        {
            get { EnsureLoaded(); return lastPlan; }
        }

        public string NewConversation()
        {
            activeConversationId = "chat_" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            lastPlan = null;
            XmlDocument doc = LoadDocument();
            XmlElement root = doc.DocumentElement;
            root.SetAttribute("active", activeConversationId);
            GetOrCreateConversation(doc, root, activeConversationId);
            doc.Save(filePath);
            SaveMessage("system", "New chat started.");
            return activeConversationId;
        }

        public void OpenConversation(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            XmlDocument doc = LoadDocument();
            XmlElement root = doc.DocumentElement;
            root.SetAttribute("active", id);
            XmlElement conversation = GetOrCreateConversation(doc, root, id);
            activeConversationId = id;
            lastPlan = ReadLastPlan(conversation);
            doc.Save(filePath);
        }

        public void RenameConversation(string id, string title)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            XmlDocument doc = LoadDocument();
            XmlElement conversation = doc.DocumentElement.SelectSingleNode("Conversation[@id='" + EscapeXPath(id) + "']") as XmlElement;
            if (conversation == null)
            {
                return;
            }

            conversation.SetAttribute("title", title.Trim());
            conversation.SetAttribute("updated_at", DateTime.Now.ToString("s"));
            doc.Save(filePath);
        }

        public string GetConversationTitle(string id)
        {
            XmlDocument doc = LoadDocument();
            XmlElement conversation = doc.DocumentElement.SelectSingleNode("Conversation[@id='" + EscapeXPath(id) + "']") as XmlElement;
            return conversation == null ? "" : conversation.GetAttribute("title");
        }

        public List<ConversationSummary> ListConversations()
        {
            XmlDocument doc = LoadDocument();
            List<ConversationSummary> result = new List<ConversationSummary>();
            foreach (XmlNode node in doc.DocumentElement.SelectNodes("Conversation"))
            {
                XmlElement c = node as XmlElement;
                if (c == null) continue;
                ConversationSummary summary = new ConversationSummary();
                summary.Id = c.GetAttribute("id");
                summary.Title = c.GetAttribute("title");
                summary.UpdatedAt = c.GetAttribute("updated_at");
                XmlElement last = c.SelectSingleNode("Message[last()]") as XmlElement;
                summary.Preview = last == null ? "" : last.InnerText;
                result.Add(summary);
            }
            result.Sort(delegate(ConversationSummary a, ConversationSummary b)
            {
                return string.Compare(b.UpdatedAt, a.UpdatedAt, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public List<ConversationMessage> GetMessages(string id)
        {
            XmlDocument doc = LoadDocument();
            List<ConversationMessage> result = new List<ConversationMessage>();
            XmlElement conversation = doc.DocumentElement.SelectSingleNode("Conversation[@id='" + EscapeXPath(id) + "']") as XmlElement;
            if (conversation == null) return result;
            foreach (XmlNode node in conversation.SelectNodes("Message"))
            {
                XmlElement m = node as XmlElement;
                if (m == null) continue;
                ConversationMessage message = new ConversationMessage();
                message.Role = m.GetAttribute("role");
                message.Text = m.InnerText;
                message.CreatedAt = m.GetAttribute("created_at");
                result.Add(message);
            }
            return result;
        }

        public void SaveMessage(string role, string text)
        {
            XmlDocument doc = LoadDocument();
            XmlElement root = doc.DocumentElement;
            root.SetAttribute("active", activeConversationId);
            XmlElement conversation = GetOrCreateConversation(doc, root, activeConversationId);
            XmlElement message = doc.CreateElement("Message");
            message.SetAttribute("role", role ?? "");
            message.SetAttribute("created_at", DateTime.Now.ToString("s"));
            message.InnerText = text ?? "";
            conversation.AppendChild(message);
            conversation.SetAttribute("updated_at", DateTime.Now.ToString("s"));
            doc.Save(filePath);
        }

        public void SaveLastPlan(LastPlanContext context)
        {
            lastPlan = context;
            XmlDocument doc = LoadDocument();
            XmlElement root = doc.DocumentElement;
            root.SetAttribute("active", activeConversationId);
            XmlElement conversation = GetOrCreateConversation(doc, root, activeConversationId);
            XmlElement existing = conversation.SelectSingleNode("LastPlan") as XmlElement;
            if (existing != null) conversation.RemoveChild(existing);
            XmlElement plan = doc.CreateElement("LastPlan");
            Write(doc, plan, "ProposalId", context == null ? "" : context.ProposalId);
            Write(doc, plan, "OriginalRequest", context == null ? "" : context.OriginalRequest);
            Write(doc, plan, "IntentSummary", context == null ? "" : context.IntentSummary);
            Write(doc, plan, "DiagnosticsSummary", context == null ? "" : context.DiagnosticsSummary);
            Write(doc, plan, "VerificationSummary", context == null ? "" : context.VerificationSummary);
            Write(doc, plan, "TrackSnapshot", context == null ? "" : context.TrackSnapshot);
            Write(doc, plan, "SelectedTrackCount", context == null ? "0" : context.SelectedTrackCount.ToString());
            Write(doc, plan, "TotalDurationSeconds", context == null ? "0" : context.TotalDurationSeconds.ToString());
            Write(doc, plan, "TargetDurationSeconds", context == null ? "0" : context.TargetDurationSeconds.ToString());
            conversation.AppendChild(plan);
            conversation.SetAttribute("updated_at", DateTime.Now.ToString("s"));
            doc.Save(filePath);
        }

        public string SavePlaylistProposal(LastPlanContext context)
        {
            if (context == null)
            {
                return "";
            }

            XmlDocument doc = LoadDocument();
            XmlElement root = doc.DocumentElement;
            root.SetAttribute("active", activeConversationId);
            XmlElement conversation = GetOrCreateConversation(doc, root, activeConversationId);
            XmlElement proposals = conversation.SelectSingleNode("Proposals") as XmlElement;
            if (proposals == null)
            {
                proposals = doc.CreateElement("Proposals");
                conversation.AppendChild(proposals);
            }

            string id = string.IsNullOrEmpty(context.ProposalId) ? "proposal_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") : context.ProposalId;
            context.ProposalId = id;
            XmlElement proposal = doc.CreateElement("Proposal");
            proposal.SetAttribute("id", id);
            proposal.SetAttribute("created_at", DateTime.Now.ToString("s"));
            Write(doc, proposal, "OriginalRequest", context.OriginalRequest);
            Write(doc, proposal, "IntentSummary", context.IntentSummary);
            Write(doc, proposal, "DiagnosticsSummary", context.DiagnosticsSummary);
            Write(doc, proposal, "VerificationSummary", context.VerificationSummary);
            Write(doc, proposal, "TrackSnapshot", context.TrackSnapshot);
            Write(doc, proposal, "SelectedTrackCount", context.SelectedTrackCount.ToString());
            Write(doc, proposal, "TotalDurationSeconds", context.TotalDurationSeconds.ToString());
            Write(doc, proposal, "TargetDurationSeconds", context.TargetDurationSeconds.ToString());
            proposals.AppendChild(proposal);
            conversation.SetAttribute("updated_at", DateTime.Now.ToString("s"));
            doc.Save(filePath);
            SaveLastPlan(context);
            return id;
        }

        private void EnsureLoaded()
        {
            XmlDocument doc = LoadDocument();
            XmlElement root = doc.DocumentElement;
            activeConversationId = root.GetAttribute("active");
            if (string.IsNullOrEmpty(activeConversationId))
            {
                activeConversationId = "chat_" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
                root.SetAttribute("active", activeConversationId);
                GetOrCreateConversation(doc, root, activeConversationId);
                doc.Save(filePath);
            }
            XmlElement conversation = GetOrCreateConversation(doc, root, activeConversationId);
            lastPlan = ReadLastPlan(conversation);
        }

        private XmlDocument LoadDocument()
        {
            XmlDocument doc = new XmlDocument();
            if (File.Exists(filePath)) doc.Load(filePath);
            if (doc.DocumentElement == null)
            {
                XmlElement root = doc.CreateElement("Conversations");
                doc.AppendChild(root);
            }
            return doc;
        }

        private static XmlElement GetOrCreateConversation(XmlDocument doc, XmlElement root, string id)
        {
            XmlElement conversation = root.SelectSingleNode("Conversation[@id='" + EscapeXPath(id) + "']") as XmlElement;
            if (conversation != null) return conversation;
            conversation = doc.CreateElement("Conversation");
            conversation.SetAttribute("id", id);
            conversation.SetAttribute("title", "Chat " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            conversation.SetAttribute("created_at", DateTime.Now.ToString("s"));
            conversation.SetAttribute("updated_at", DateTime.Now.ToString("s"));
            root.AppendChild(conversation);
            return conversation;
        }

        private static LastPlanContext ReadLastPlan(XmlElement conversation)
        {
            XmlElement plan = conversation == null ? null : conversation.SelectSingleNode("LastPlan") as XmlElement;
            if (plan == null) return null;
            LastPlanContext context = new LastPlanContext();
            context.ProposalId = Read(plan, "ProposalId");
            context.OriginalRequest = Read(plan, "OriginalRequest");
            context.IntentSummary = Read(plan, "IntentSummary");
            context.DiagnosticsSummary = Read(plan, "DiagnosticsSummary");
            context.VerificationSummary = Read(plan, "VerificationSummary");
            context.TrackSnapshot = Read(plan, "TrackSnapshot");
            context.SelectedTrackCount = ReadInt(plan, "SelectedTrackCount");
            context.TotalDurationSeconds = ReadInt(plan, "TotalDurationSeconds");
            context.TargetDurationSeconds = ReadInt(plan, "TargetDurationSeconds");
            return context;
        }

        private static void Write(XmlDocument doc, XmlElement root, string name, string value)
        {
            XmlElement child = doc.CreateElement(name);
            child.InnerText = value ?? "";
            root.AppendChild(child);
        }

        private static string Read(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node == null ? "" : node.InnerText;
        }

        private static int ReadInt(XmlElement root, string name)
        {
            int value;
            return int.TryParse(Read(root, name), out value) ? value : 0;
        }

        private static string EscapeXPath(string value)
        {
            return (value ?? "").Replace("'", "&apos;");
        }
    }
}
