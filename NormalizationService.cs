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

        public static string ArtistKey(string artist)
        {
            return NormalizeKey(artist);
        }

        public static string AlbumKey(string album, string albumArtist)
        {
            return NormalizeKey((album ?? "") + "|" + (albumArtist ?? ""));
        }

        public static string CanonicalTitleKey(string title)
        {
            string value = NormalizeKey(title);
            value = Regex.Replace(value, "\\[[^\\]]*\\]", " ");
            value = Regex.Replace(value, "\\([^\\)]*\\)", " ");
            value = Regex.Replace(value, "\\{[^\\}]*\\}", " ");
            value = Regex.Replace(value, "\\s+-\\s+.*\\b(remix|mix|edit|version|live|remaster(ed)?)\\b.*$", " ");
            value = Regex.Replace(value, "\\b(remix|mix|edit|version)\\s+by\\s+.+$", " ");
            value = Regex.Replace(value, "\\b(remixed|mixed|edited)\\s+by\\s+.+$", " ");
            value = Regex.Replace(value, "\\b(live|remaster(ed)?|radio edit|single version|album version|instrumental|acoustic|demo|edit|mix|remix|mono|stereo|version|bonus track)\\b", " ");
            value = Regex.Replace(value, "\\b(\\d{4})\\b", " ");
            value = Regex.Replace(value, "[^\\p{L}\\p{Nd}]+", " ");
            value = Regex.Replace(value, "\\s+", " ").Trim();
            return value;
        }

        public static string CanonicalTrackKey(TrackInfo track)
        {
            if (track == null)
            {
                return "";
            }

            string title = CanonicalTitleKey(track.Title);
            if (string.IsNullOrEmpty(title))
            {
                return NormalizeKey(track.FileUrl);
            }

            return ArtistKey(track.Artist) + "|" + title;
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
            int value = NormalizeRating(rating);
            if (value >= 80) return "favorite";
            if (value >= 60) return "liked";
            if (value > 0) return "rated";
            return "";
        }

        public static int NormalizeRating(string rating)
        {
            int value = ParseInt(rating);
            if (value <= 0) return 0;
            if (value <= 5) return value * 20;
            if (value <= 10) return value * 10;
            return Math.Min(100, value);
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
