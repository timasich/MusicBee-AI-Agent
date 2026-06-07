using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace MusicBeePlugin
{
    public class PluginSettings
    {
        public string BaseUrl = "http://localhost:1234/v1";
        public string ApiKey = "";
        public string Model = "local-model";
        public int MaxTokens = 1000;
        public int RequestTimeoutSeconds = 60;
        public string DockPanelTarget = "MainPanel";

        public static PluginSettings Load(string dataPath)
        {
            PluginSettings settings = new PluginSettings();
            string file = GetSettingsPath(dataPath);
            if (!File.Exists(file))
            {
                return settings;
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(file);
            XmlElement root = doc.DocumentElement;
            if (root == null)
            {
                return settings;
            }

            settings.BaseUrl = Read(root, "BaseUrl", settings.BaseUrl);
            settings.ApiKey = Read(root, "ApiKey", settings.ApiKey);
            settings.Model = Read(root, "Model", settings.Model);
            settings.MaxTokens = ReadInt(root, "MaxTokens", settings.MaxTokens);
            settings.RequestTimeoutSeconds = ReadInt(root, "RequestTimeoutSeconds", settings.RequestTimeoutSeconds);
            settings.DockPanelTarget = Read(root, "DockPanelTarget", settings.DockPanelTarget);

            return settings;
        }

        public void Save(string dataPath)
        {
            Directory.CreateDirectory(dataPath);

            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Settings");
            doc.AppendChild(root);

            Write(doc, root, "BaseUrl", BaseUrl);
            Write(doc, root, "ApiKey", ApiKey);
            Write(doc, root, "Model", Model);
            Write(doc, root, "MaxTokens", MaxTokens.ToString(CultureInfo.InvariantCulture));
            Write(doc, root, "RequestTimeoutSeconds", RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            Write(doc, root, "DockPanelTarget", DockPanelTarget);

            doc.Save(GetSettingsPath(dataPath));
        }

        private static string GetSettingsPath(string dataPath)
        {
            return Path.Combine(dataPath, "settings.xml");
        }

        private static string Read(XmlElement root, string name, string fallback)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node == null ? fallback : node.InnerText;
        }

        private static int ReadInt(XmlElement root, string name, int fallback)
        {
            int value;
            return int.TryParse(Read(root, name, ""), out value) ? value : fallback;
        }

        private static void Write(XmlDocument doc, XmlElement root, string name, string value)
        {
            XmlElement child = doc.CreateElement(name);
            child.InnerText = value ?? "";
            root.AppendChild(child);
        }
    }
}
