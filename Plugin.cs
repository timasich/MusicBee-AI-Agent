using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();
        private PluginSettings settings;
        private MusicBeeApiAdapter musicBee;
        private LibraryIndexingService indexingService;
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
            indexingService = new LibraryIndexingService(pluginDataPath);

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
                    ResetChatAfterSettingsChange();
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
                mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee AI Agent - Rebuild Library Index", "", RebuildIndexFromMenu);
                StartInitialIndexing();
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

        private void RebuildIndexFromMenu(object sender, EventArgs e)
        {
            List<TrackInfo> snapshot = musicBee.GetAllLibraryTracks();
            if (snapshot.Count == 0)
            {
                MessageBox.Show("MusicBee returned zero library tracks. Index rebuild was not started.", "MusicBee AI Agent");
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    indexingService.RebuildIndexFromSnapshot(snapshot);
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(pluginDataPath, "index.log"), DateTime.Now.ToString("s") + " Manual rebuild failed: " + ex + Environment.NewLine);
                    }
                    catch
                    {
                    }
                }
            });
            MessageBox.Show("MusicBee AI Agent started rebuilding the library index in the background. Snapshot tracks: " + snapshot.Count, "MusicBee AI Agent");
        }

        private void StartInitialIndexing()
        {
            if (indexingService.HasAnyIndexedTracks())
            {
                ThreadPool.QueueUserWorkItem(delegate { indexingService.MarkChecked(); });
                return;
            }

            List<TrackInfo> snapshot = musicBee.GetAllLibraryTracks();
            if (snapshot.Count == 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    indexingService.RebuildIndexFromSnapshot(snapshot);
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(pluginDataPath, "index.log"), DateTime.Now.ToString("s") + " Initial index failed: " + ex + Environment.NewLine);
                    }
                    catch
                    {
                    }
                }
            });
        }

        private void ResetChatAfterSettingsChange()
        {
            if (chatForm != null && !chatForm.IsDisposed)
            {
                chatForm.AllowClose = true;
                chatForm.Close();
                chatForm = null;
                MessageBox.Show("Settings saved. Reopen MusicBee AI Agent chat to use the new provider/model.", "MusicBee AI Agent");
            }
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
