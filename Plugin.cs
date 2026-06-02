using System;
using System.IO;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();
        private PluginSettings settings;
        private MusicBeeApiAdapter musicBee;
        private ChatForm chatForm;
        private string pluginDataPath;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);

            pluginDataPath = Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), "MusicBeeAIAgent");
            Directory.CreateDirectory(pluginDataPath);

            settings = PluginSettings.Load(pluginDataPath);
            musicBee = new MusicBeeApiAdapter(mbApiInterface);

            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "MusicBee AI Agent";
            about.Description = "AI chat assistant for MusicBee with confirmed structured actions.";
            about.Author = "MusicBee AI Agent";
            about.TargetApplication = "";
            about.Type = PluginType.General;
            about.VersionMajor = 0;
            about.VersionMinor = 1;
            about.Revision = 0;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            about.ConfigurationPanelHeight = 0;

            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            using (SettingsForm form = new SettingsForm(settings))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    settings = form.Settings;
                    settings.Save(pluginDataPath);
                }
            }

            return true;
        }

        public void SaveSettings()
        {
            if (settings != null)
            {
                settings.Save(pluginDataPath);
            }
        }

        public void Close(PluginCloseReason reason)
        {
            if (chatForm != null)
            {
                chatForm.AllowClose = true;
                chatForm.Close();
                chatForm = null;
            }
        }

        public void Uninstall()
        {
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (type == NotificationType.PluginStartup)
            {
                mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee AI Agent - Open Chat", "", OpenChatFromMenu);
                mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee AI Agent - Settings", "", OpenSettingsFromMenu);
            }
        }

        private void OpenChatFromMenu(object sender, EventArgs e)
        {
            ShowChat();
        }

        private void OpenSettingsFromMenu(object sender, EventArgs e)
        {
            Configure(IntPtr.Zero);
        }

        private void ShowChat()
        {
            if (chatForm == null || chatForm.IsDisposed)
            {
                IAiProvider provider = new OpenAiCompatibleProvider(settings);
                AgentController agent = new AgentController(musicBee, provider, settings, pluginDataPath);
                chatForm = new ChatForm(agent, settings);
                chatForm.FormClosed += delegate { chatForm = null; };
            }

            chatForm.Show();
            chatForm.Activate();
        }
    }
}
