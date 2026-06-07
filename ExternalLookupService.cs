using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;

namespace MusicBeePlugin
{
    public class ExternalLookupService
    {
        private const string UserAgent = "MusicBeeAIAgentPlugin/0.1 (https://musicbee.fandom.com/wiki/Plugins)";

        public string LookupListenBrainzSimilarArtists(string artistName, int limit)
        {
            artistName = (artistName ?? "").Trim();
            if (artistName.Length == 0)
            {
                return "ListenBrainz similar artists: missing seed artist.";
            }

            limit = Math.Max(1, Math.Min(30, limit <= 0 ? 10 : limit));
            ArtistSeed seed = ResolveMusicBrainzArtist(artistName);
            if (seed == null || string.IsNullOrEmpty(seed.Mbid))
            {
                return "ListenBrainz similar artists: MusicBrainz artist lookup found no MBID for '" + artistName + "'.";
            }

            List<ExternalArtistRecommendation> recommendations = FetchListenBrainzSimilarArtists(seed, limit);
            if (recommendations.Count == 0)
            {
                return "ListenBrainz similar artists: no results for " + seed.Name + " (" + seed.Mbid + ").";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("ListenBrainz similar artists for " + seed.Name + " (" + seed.Mbid + "):");
            for (int i = 0; i < recommendations.Count; i++)
            {
                ExternalArtistRecommendation item = recommendations[i];
                builder.Append("- artist=").Append(item.ArtistName);
                if (!string.IsNullOrEmpty(item.ArtistMbid))
                {
                    builder.Append("; mbid=").Append(item.ArtistMbid);
                }
                if (item.ListenCount > 0)
                {
                    builder.Append("; listenCount=").Append(item.ListenCount);
                }
                builder.Append("; source=").Append(item.Source);
                builder.AppendLine();
            }
            return builder.ToString();
        }

        public string LookupWikipediaSummary(string query, int limit)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0)
            {
                return "Wikipedia lookup: missing query.";
            }

            string language = "ru";
            int separator = query.IndexOf(':');
            if (separator > 0 && separator <= 5)
            {
                string prefix = query.Substring(0, separator).Trim().ToLowerInvariant();
                if (IsLanguageCode(prefix))
                {
                    language = prefix;
                    query = query.Substring(separator + 1).Trim();
                }
            }

            string result = LookupWikipediaSummaryInLanguage(language, query, limit);
            if (result.Length == 0 && language != "en")
            {
                result = LookupWikipediaSummaryInLanguage("en", query, limit);
            }
            return result.Length == 0 ? "Wikipedia lookup: no page found for '" + query + "'." : result;
        }

        private static string LookupWikipediaSummaryInLanguage(string language, string query, int limit)
        {
            string title = SearchWikipediaTitle(language, query);
            if (string.IsNullOrEmpty(title))
            {
                return "";
            }

            string url = "https://" + language + ".wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title);
            IDictionary<string, object> root = GetJsonObject(url);
            if (root == null)
            {
                return "";
            }

            string extract = SimpleJson.GetString(root, "extract");
            if (string.IsNullOrWhiteSpace(extract))
            {
                return "";
            }

            int maxChars = Math.Max(400, Math.Min(4000, limit <= 0 ? 1600 : limit * 160));
            if (extract.Length > maxChars)
            {
                extract = extract.Substring(0, maxChars).Trim() + "...";
            }

            string pageUrl = "";
            object urlsValue;
            IDictionary<string, object> urls = root.TryGetValue("content_urls", out urlsValue) ? urlsValue as IDictionary<string, object> : null;
            object desktopValue;
            IDictionary<string, object> desktop = urls != null && urls.TryGetValue("desktop", out desktopValue) ? desktopValue as IDictionary<string, object> : null;
            if (desktop != null)
            {
                pageUrl = SimpleJson.GetString(desktop, "page");
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("Wikipedia summary:");
            builder.AppendLine("- language=" + language);
            builder.AppendLine("- title=" + SimpleJson.GetString(root, "title"));
            string description = SimpleJson.GetString(root, "description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.AppendLine("- description=" + description);
            }
            if (!string.IsNullOrWhiteSpace(pageUrl))
            {
                builder.AppendLine("- url=" + pageUrl);
            }
            builder.AppendLine("- extract=" + extract);
            return builder.ToString();
        }

        private static string SearchWikipediaTitle(string language, string query)
        {
            string url = "https://" + language + ".wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=1&srsearch=" + Uri.EscapeDataString(query);
            IDictionary<string, object> root = GetJsonObject(url);
            object queryValue;
            IDictionary<string, object> queryObject = root != null && root.TryGetValue("query", out queryValue) ? queryValue as IDictionary<string, object> : null;
            object searchValue;
            IList search = queryObject != null && queryObject.TryGetValue("search", out searchValue) ? searchValue as IList : null;
            if (search == null || search.Count == 0)
            {
                return "";
            }
            IDictionary<string, object> first = search[0] as IDictionary<string, object>;
            return first == null ? "" : SimpleJson.GetString(first, "title");
        }

        private static bool IsLanguageCode(string value)
        {
            if (value.Length < 2 || value.Length > 5)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || c == '-'))
                {
                    return false;
                }
            }
            return true;
        }

        private static ArtistSeed ResolveMusicBrainzArtist(string artistName)
        {
            string query = "artist:\"" + artistName.Replace("\"", "") + "\"";
            string url = "https://musicbrainz.org/ws/2/artist/?fmt=json&limit=1&query=" + Uri.EscapeDataString(query);
            IDictionary<string, object> root = GetJsonObject(url);
            object artistsValue;
            IList artists = root != null && root.TryGetValue("artists", out artistsValue) ? artistsValue as IList : null;
            if (artists == null || artists.Count == 0)
            {
                return null;
            }

            IDictionary<string, object> first = artists[0] as IDictionary<string, object>;
            if (first == null)
            {
                return null;
            }

            ArtistSeed seed = new ArtistSeed();
            seed.Mbid = SimpleJson.GetString(first, "id");
            seed.Name = SimpleJson.GetString(first, "name");
            if (string.IsNullOrEmpty(seed.Name))
            {
                seed.Name = artistName;
            }
            return seed;
        }

        private static List<ExternalArtistRecommendation> FetchListenBrainzSimilarArtists(ArtistSeed seed, int limit)
        {
            string url = "https://api.listenbrainz.org/1/lb-radio/artist/" + Uri.EscapeDataString(seed.Mbid) +
                "?mode=easy&max_similar_artists=" + Math.Max(1, limit) +
                "&max_recordings_per_artist=1&pop_begin=0&pop_end=100";
            IDictionary<string, object> root = GetJsonObject(url);
            List<ExternalArtistRecommendation> result = new List<ExternalArtistRecommendation>();
            Dictionary<string, ExternalArtistRecommendation> byArtist = new Dictionary<string, ExternalArtistRecommendation>(StringComparer.OrdinalIgnoreCase);
            CollectSimilarArtists(root, byArtist);

            foreach (ExternalArtistRecommendation item in byArtist.Values)
            {
                if (string.Equals(item.ArtistMbid, seed.Mbid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(item.ArtistName, seed.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                result.Add(item);
            }

            result.Sort(delegate(ExternalArtistRecommendation a, ExternalArtistRecommendation b)
            {
                int listens = b.ListenCount.CompareTo(a.ListenCount);
                if (listens != 0) return listens;
                return string.Compare(a.ArtistName, b.ArtistName, StringComparison.OrdinalIgnoreCase);
            });
            if (result.Count > limit)
            {
                result.RemoveRange(limit, result.Count - limit);
            }
            return result;
        }

        private static void CollectSimilarArtists(object value, Dictionary<string, ExternalArtistRecommendation> result)
        {
            IDictionary<string, object> obj = value as IDictionary<string, object>;
            if (obj != null)
            {
                string name = SimpleJson.GetString(obj, "similar_artist_name");
                if (!string.IsNullOrEmpty(name))
                {
                    string mbid = SimpleJson.GetString(obj, "similar_artist_mbid");
                    string key = string.IsNullOrEmpty(mbid) ? name : mbid;
                    ExternalArtistRecommendation item;
                    if (!result.TryGetValue(key, out item))
                    {
                        item = new ExternalArtistRecommendation();
                        item.ArtistName = name;
                        item.ArtistMbid = mbid;
                        item.Source = "ListenBrainz LB radio artist";
                        result[key] = item;
                    }
                    int listenCount = ParseInt(SimpleJson.GetString(obj, "total_listen_count"));
                    if (listenCount > item.ListenCount)
                    {
                        item.ListenCount = listenCount;
                    }
                }

                foreach (object child in obj.Values)
                {
                    CollectSimilarArtists(child, result);
                }
                return;
            }

            IList list = value as IList;
            if (list == null)
            {
                return;
            }
            foreach (object child in list)
            {
                CollectSimilarArtists(child, result);
            }
        }

        private static IDictionary<string, object> GetJsonObject(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = UserAgent;
            request.Accept = "application/json";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (System.IO.Stream stream = response.GetResponseStream())
            using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
            {
                return SimpleJson.Parse(reader.ReadToEnd()) as IDictionary<string, object>;
            }
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : 0;
        }

        private class ArtistSeed
        {
            public string Name;
            public string Mbid;
        }
    }
}
