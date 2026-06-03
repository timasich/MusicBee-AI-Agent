using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    public class IntentParser
    {
        private readonly PluginSettings settings;

        public IntentParser(PluginSettings settings)
        {
            this.settings = settings;
        }

        public SearchIntent Parse(string userMessage)
        {
            string text = (userMessage ?? "").ToLowerInvariant();
            SearchIntent intent = new SearchIntent();
            intent.QueryText = userMessage;
            intent.MaxTracks = settings != null && settings.SmallLocalModelMode ? 12 : 40;
            intent.Similar = ContainsAny(text, new string[] { "similar", "like current", "\\u043f\\u043e\\u0445\\u043e\\u0436", "\\u043f\\u043e\\u0434\\u043e\\u0431\\u043d" });
            intent.Calmer = ContainsAny(text, new string[] { "calm", "calmer", "quiet", "focus", "\\u0441\\u043f\\u043e\\u043a\\u043e\\u0439", "\\u0444\\u043e\\u043a\\u0443\\u0441" });
            intent.Energetic = ContainsAny(text, new string[] { "energy", "energetic", "faster", "dance", "\\u0431\\u043e\\u0434\\u0440", "\\u044d\\u043d\\u0435\\u0440\\u0433" });
            intent.ExcludeCurrentArtist = ContainsAny(text, new string[] { "different artist", "not same artist", "\\u0434\\u0440\\u0443\\u0433\\u043e\\u0439 \\u0438\\u0441\\u043f\\u043e\\u043b\\u043d\\u0438\\u0442\\u0435\\u043b" });
            intent.TargetDurationSeconds = ParseTargetDurationSeconds(text);
            if (intent.TargetDurationSeconds > 0)
            {
                intent.MaxTracks = settings != null && settings.SmallLocalModelMode ? 20 : 120;
            }
            return intent;
        }

        private static bool ContainsAny(string text, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.IndexOf(DecodeEscapes(needles[i])) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string DecodeEscapes(string value)
        {
            return value.IndexOf("\\u") < 0 ? value : Regex.Unescape(value);
        }

        private static int ParseTargetDurationSeconds(string text)
        {
            Match match = Regex.Match(text, "(\\d+)\\s*(hour|hours|hr|h|minute|minutes|min|m)");
            if (match.Success)
            {
                int value = int.Parse(match.Groups[1].Value);
                string unit = match.Groups[2].Value;
                return unit.StartsWith("h") ? value * 3600 : value * 60;
            }

            match = Regex.Match(text, "(\\d+)\\s*(\\u0447\\u0430\\u0441|\\u043c\\u0438\\u043d)");
            if (match.Success)
            {
                int value = int.Parse(match.Groups[1].Value);
                string unit = match.Groups[2].Value;
                return unit.IndexOf(Regex.Unescape("\\u0447")) >= 0 ? value * 3600 : value * 60;
            }

            return 0;
        }
    }
}
