using System;
using System.Collections.Generic;
using System.Drawing;
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
        private AgentChatControl dockChatPanel;
        private string pluginDataPath;
        private bool menusRegistered;

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

            RegisterMenus();
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

            if (dockChatPanel != null)
            {
                try
                {
                    mbApiInterface.MB_RemovePanel(dockChatPanel);
                }
                catch
                {
                }
                dockChatPanel.Dispose();
                dockChatPanel = null;
            }
        }

        public void Uninstall()
        {
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (type == NotificationType.PluginStartup)
            {
                RegisterMenus();
                StartInitialIndexing();
            }
        }

        private void RegisterMenus()
        {
            if (menusRegistered || mbApiInterface.MB_AddMenuItem == null)
            {
                return;
            }

            try
            {
                mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee AI Agent - Open Chat", "", OpenChatFromMenu);
                mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee AI Agent - Settings", "", OpenSettingsFromMenu);
                mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee AI Agent - Rebuild Library Index", "", RebuildIndexFromMenu);
                menusRegistered = true;
            }
            catch (Exception ex)
            {
                LogPlugin("Menu registration failed: " + ex);
            }
        }

        private void LogPlugin(string message)
        {
            try
            {
                if (!string.IsNullOrEmpty(pluginDataPath))
                {
                    File.AppendAllText(Path.Combine(pluginDataPath, "plugin.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
                }
            }
            catch
            {
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

        private void OpenDockPanelFromMenu(object sender, EventArgs e)
        {
            ShowDockPanel();
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
            if (dockChatPanel != null && !dockChatPanel.IsDisposed)
            {
                try
                {
                    mbApiInterface.MB_RemovePanel(dockChatPanel);
                }
                catch
                {
                }
                dockChatPanel.Dispose();
                dockChatPanel = null;
            }
        }

        private void ShowChat()
        {
            if (chatForm == null || chatForm.IsDisposed)
            {
                IAiProvider provider = new OpenAiCompatibleProvider(settings);
                AgentController agent = new AgentController(musicBee, provider, settings, pluginDataPath);
                chatForm = new ChatForm(agent, settings, CreateTheme());
                chatForm.FormClosed += delegate { chatForm = null; };
            }

            chatForm.Show();
            chatForm.Activate();
        }

        private void ShowDockPanel()
        {
            if (dockChatPanel == null || dockChatPanel.IsDisposed)
            {
                IAiProvider provider = new OpenAiCompatibleProvider(settings);
                AgentController agent = new AgentController(musicBee, provider, settings, pluginDataPath);
                dockChatPanel = new AgentChatControl(agent, settings, CreateTheme());
                mbApiInterface.MB_AddPanel(dockChatPanel, ResolveDockPanelTarget());
                dockChatPanel.Show();
            }
            mbApiInterface.MB_RefreshPanels();
        }

        private PluginPanelDock ResolveDockPanelTarget()
        {
            try
            {
                return (PluginPanelDock)Enum.Parse(typeof(PluginPanelDock), settings.DockPanelTarget);
            }
            catch
            {
                return PluginPanelDock.MainPanel;
            }
        }

        private MusicBeeTheme CreateTheme()
        {
            MusicBeeTheme theme = new MusicBeeTheme();
            try
            {
                theme.BackColor = ReadSkinColor(SkinElement.SkinSubPanel, ElementComponent.ComponentBackground, SystemColors.Control);
                theme.ForeColor = ReadSkinColor(SkinElement.SkinInputPanelLabel, ElementComponent.ComponentForeground, SystemColors.ControlText);
                theme.InputBackColor = ReadSkinColor(SkinElement.SkinInputControl, ElementComponent.ComponentBackground, SystemColors.Window);
                theme.InputForeColor = ReadSkinColor(SkinElement.SkinInputControl, ElementComponent.ComponentForeground, SystemColors.WindowText);
            }
            catch
            {
            }
            return theme;
        }

        private Color ReadSkinColor(SkinElement element, ElementComponent component, Color fallback)
        {
            int argb = mbApiInterface.Setting_GetSkinElementColour(element, ElementState.ElementStateDefault, component);
            if (argb == 0)
            {
                return fallback;
            }
            return Color.FromArgb(argb);
        }
    }
}
