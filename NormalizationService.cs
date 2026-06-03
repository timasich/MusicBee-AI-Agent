using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    public static class NormalizationService
    {
        public static string NormalizeKey(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            value = Regex.Replace(value, "\\s+", " ");
            return value;
        }

        public static string[] Tokenize(params string[] values)
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            List<string> tokens = new List<string>();
            for (int i = 0; i < values.Length; i++)
            {
                string text = NormalizeKey(values[i]);
                string[] raw = Regex.Split(text, "[^\\p{L}\\p{Nd}]+");
                for (int j = 0; j < raw.Length; j++)
                {
                    string token = raw[j].Trim();
                    if (token.Length < 2 || IsStopWord(token) || seen.ContainsKey(token))
                    {
                        continue;
                    }

                    seen[token] = true;
                    tokens.Add(token);
                }
            }

            return tokens.ToArray();
        }

        public static string[] SplitGenreTokens(string genre)
        {
            return Tokenize((genre ?? "").Replace("/", " ").Replace("|", " ").Replace(";", " "));
        }

        public static string BpmBucket(string bpm)
        {
            int value = ParseInt(bpm);
            if (value <= 0) return "";
            if (value < 80) return "slow";
            if (value < 110) return "mid-tempo";
            if (value < 135) return "upbeat";
            return "fast";
        }

        public static string YearBucket(string year)
        {
            int value = ParseInt(year);
            if (value <= 0) return "";
            int decade = value - (value % 10);
            return decade.ToString() + "s";
        }

        public static string RatingBucket(string rating)
        {
            int value = ParseInt(rating);
            if (value >= 80) return "favorite";
            if (value >= 60) return "liked";
            if (value > 0) return "rated";
            return "";
        }

        public static string DurationBucket(string duration)
        {
            int seconds = TrackDurationSeconds(duration);
            if (seconds <= 0) return "";
            if (seconds < 150) return "short";
            if (seconds < 360) return "standard";
            if (seconds < 720) return "long";
            return "very-long";
        }

        public static int TrackDurationSeconds(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int seconds;
            if (int.TryParse(value, out seconds)) return seconds;
            string[] parts = value.Split(':');
            if (parts.Length == 2)
            {
                int minutes;
                int sec;
                if (int.TryParse(parts[0], out minutes) && int.TryParse(parts[1], out sec))
                {
                    return minutes * 60 + sec;
                }
            }
            return 0;
        }

        public static int ParseInt(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int parsed;
            if (int.TryParse(value, out parsed)) return parsed;
            double d;
            if (double.TryParse(value, out d)) return Convert.ToInt32(d);
            return 0;
        }

        private static bool IsStopWord(string token)
        {
            string words = "|the|and|for|with|from|feat|ft|music|song|songs|track|tracks|playlist|create|find|add|queue|now|playing|";
            return words.IndexOf("|" + token + "|", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
