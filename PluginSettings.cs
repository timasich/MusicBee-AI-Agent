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
        public double Temperature = 0.2;
        public int MaxTokens = 1000;
        public PrivacyMode PrivacyMode = PrivacyMode.MetadataOnly;
        public int RequestTimeoutSeconds = 60;
        public bool SmallLocalModelMode = true;

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
            settings.Temperature = ReadDouble(root, "Temperature", settings.Temperature);
            settings.MaxTokens = ReadInt(root, "MaxTokens", settings.MaxTokens);
            settings.RequestTimeoutSeconds = ReadInt(root, "RequestTimeoutSeconds", settings.RequestTimeoutSeconds);
            settings.SmallLocalModelMode = ReadBool(root, "SmallLocalModelMode", settings.SmallLocalModelMode);

            string privacy = Read(root, "PrivacyMode", settings.PrivacyMode.ToString());
            try
            {
                settings.PrivacyMode = (PrivacyMode)Enum.Parse(typeof(PrivacyMode), privacy);
            }
            catch
            {
                settings.PrivacyMode = PrivacyMode.MetadataOnly;
            }

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
            Write(doc, root, "Temperature", Temperature.ToString(CultureInfo.InvariantCulture));
            Write(doc, root, "MaxTokens", MaxTokens.ToString(CultureInfo.InvariantCulture));
            Write(doc, root, "PrivacyMode", PrivacyMode.ToString());
            Write(doc, root, "RequestTimeoutSeconds", RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            Write(doc, root, "SmallLocalModelMode", SmallLocalModelMode.ToString());

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

        private static double ReadDouble(XmlElement root, string name, double fallback)
        {
            double value;
            return double.TryParse(Read(root, name, ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool ReadBool(XmlElement root, string name, bool fallback)
        {
            bool value;
            return bool.TryParse(Read(root, name, ""), out value) ? value : fallback;
        }

        private static void Write(XmlDocument doc, XmlElement root, string name, string value)
        {
            XmlElement child = doc.CreateElement(name);
            child.InnerText = value ?? "";
            root.AppendChild(child);
        }
    }
}
