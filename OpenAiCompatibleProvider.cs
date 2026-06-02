using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MusicBeePlugin
{
    public interface IAiProvider
    {
        string SendChat(string systemPrompt, string userPrompt);
    }

    public class OpenAiCompatibleProvider : IAiProvider
    {
        private readonly PluginSettings settings;

        public OpenAiCompatibleProvider(PluginSettings settings)
        {
            this.settings = settings;
        }

        public string SendChat(string systemPrompt, string userPrompt)
        {
            string baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (baseUrl.Length == 0)
            {
                throw new InvalidOperationException("AI provider Base URL is empty.");
            }

            Hashtable request = new Hashtable();
            request["model"] = settings.Model;
            request["temperature"] = settings.Temperature;
            request["max_tokens"] = settings.MaxTokens;

            ArrayList messages = new ArrayList();
            messages.Add(Message("system", systemPrompt));
            messages.Add(Message("user", userPrompt));
            request["messages"] = messages;

            byte[] body = Encoding.UTF8.GetBytes(SimpleJson.Stringify(request));
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(baseUrl + "/chat/completions");
            webRequest.Method = "POST";
            webRequest.ContentType = "application/json";
            webRequest.Timeout = Math.Max(5, settings.RequestTimeoutSeconds) * 1000;
            webRequest.ReadWriteTimeout = webRequest.Timeout;

            if (!string.IsNullOrEmpty(settings.ApiKey))
            {
                webRequest.Headers["Authorization"] = "Bearer " + settings.ApiKey;
            }

            using (Stream stream = webRequest.GetRequestStream())
            {
                stream.Write(body, 0, body.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                return ExtractMessageContent(json);
            }
        }

        private static Hashtable Message(string role, string content)
        {
            Hashtable message = new Hashtable();
            message["role"] = role;
            message["content"] = content ?? "";
            return message;
        }

        private static string ExtractMessageContent(string json)
        {
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            object choicesValue;
            if (root == null || !root.TryGetValue("choices", out choicesValue))
            {
                throw new FormatException("AI response does not contain choices.");
            }

            IList choices = choicesValue as IList;
            if (choices == null || choices.Count == 0)
            {
                throw new FormatException("AI response choices are empty.");
            }

            IDictionary<string, object> first = choices[0] as IDictionary<string, object>;
            object messageValue;
            IDictionary<string, object> message = first != null && first.TryGetValue("message", out messageValue) ? messageValue as IDictionary<string, object> : null;
            if (message == null)
            {
                throw new FormatException("AI response choice does not contain a message.");
            }

            return SimpleJson.GetString(message, "content");
        }
    }
}
