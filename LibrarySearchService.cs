using System;
using System.Collections.Generic;
using System.IO;

namespace MusicBeePlugin
{
    public class LibrarySearchService
    {
        private readonly MusicBeeApiAdapter musicBee;
        private readonly string dataPath;
        private readonly CandidateRanker ranker = new CandidateRanker();

        public LibrarySearchService(MusicBeeApiAdapter musicBee, string dataPath)
        {
            this.musicBee = musicBee;
            this.dataPath = dataPath;
        }

        public List<TrackInfo> Search(SearchIntent intent, TrackInfo nowPlaying)
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    if (index.GetTrackCount() > 0)
                    {
                        LibraryProfile profile = index.GetProfile();
                        AdaptiveRetrievalBudget budget = AdaptiveRetrievalBudget.Create(null, profile, intent);
                        string[] tokens = BuildSearchTokens(intent, nowPlaying);
                        List<TrackInfo> candidates = tokens.Length == 0
                            ? index.LoadTracksForSearch(budget.ScanLimit)
                            : index.SearchTracksByTokens(tokens, budget.ScanLimit);
                        return ranker.Rank(candidates, intent, nowPlaying, budget.CandidateCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Index search fallback: " + ex.Message);
            }

            return musicBee.SearchLibrary(intent, nowPlaying);
        }

        public List<TrackInfo> Search(SearchIntent intent, TrackInfo nowPlaying, PluginSettings settings)
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    if (index.GetTrackCount() > 0)
                    {
                        LibraryProfile profile = index.GetProfile();
                        AdaptiveRetrievalBudget budget = AdaptiveRetrievalBudget.Create(settings, profile, intent);
                        string[] tokens = BuildSearchTokens(intent, nowPlaying);
                        List<TrackInfo> candidates = tokens.Length == 0
                            ? index.LoadTracksForSearch(budget.ScanLimit)
                            : index.SearchTracksByTokens(tokens, budget.ScanLimit);
                        return ranker.Rank(candidates, intent, nowPlaying, budget.CandidateCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Index search fallback: " + ex.Message);
            }

            return musicBee.SearchLibrary(intent, nowPlaying);
        }

        public List<TrackInfo> SearchArtistTracks(string artistName, int limit, PluginSettings settings)
        {
            SearchIntent intent = new SearchIntent();
            intent.QueryText = artistName;
            intent.RetrievalQuery = artistName;
            intent.MaxTracks = Math.Max(1, limit);
            intent.BoostArtists.Add(artistName);

            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    if (index.GetTrackCount() > 0)
                    {
                        int scanLimit = Math.Max(500, Math.Min(12000, index.GetTrackCount()));
                        List<TrackInfo> all = index.LoadTracksForSearch(scanLimit);
                        List<TrackInfo> filtered = new List<TrackInfo>();
                        string requested = NormalizationService.ArtistKey(artistName);
                        foreach (TrackInfo track in all)
                        {
                            string artist = NormalizationService.ArtistKey(track.Artist);
                            string albumArtist = NormalizationService.ArtistKey(track.AlbumArtist);
                            bool artistMatch = !string.IsNullOrEmpty(artist) && (artist.IndexOf(requested) >= 0 || requested.IndexOf(artist) >= 0);
                            bool albumArtistMatch = !string.IsNullOrEmpty(albumArtist) && (albumArtist.IndexOf(requested) >= 0 || requested.IndexOf(albumArtist) >= 0);
                            if (!string.IsNullOrEmpty(requested) && (artistMatch || albumArtistMatch))
                            {
                                filtered.Add(track);
                            }
                        }
                        return ranker.Rank(filtered, intent, null, Math.Max(1, limit));
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Artist search fallback: " + ex.Message);
            }

            return Search(intent, null, settings);
        }

        public List<LibraryFacetValue> GetFacetValues(string field, string query, int limit)
        {
            field = (field ?? "").Trim().ToLowerInvariant();
            if (field != "genres" && field != "artists" && field != "years")
            {
                return new List<LibraryFacetValue>();
            }

            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    if (index.GetTrackCount() > 0)
                    {
                        return index.GetFacetValues(field, query, limit);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Facet index fallback: " + ex.Message);
            }

            return BuildFacetFromApi(field, query, limit);
        }

        public LibraryProfile GetProfile()
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    return index.GetProfile();
                }
            }
            catch (Exception ex)
            {
                Log("Could not read library profile: " + ex.Message);
                return new LibraryProfile();
            }
        }

        private string DatabasePath
        {
            get { return Path.Combine(dataPath, "library-index.sqlite"); }
        }

        private List<LibraryFacetValue> BuildFacetFromApi(string field, string query, int limit)
        {
            Dictionary<string, LibraryFacetValue> values = new Dictionary<string, LibraryFacetValue>(StringComparer.OrdinalIgnoreCase);
            List<TrackInfo> tracks = musicBee.GetAllLibraryTracks();
            query = (query ?? "").Trim();
            limit = Math.Max(1, Math.Min(500, limit <= 0 ? 80 : limit));

            foreach (TrackInfo track in tracks)
            {
                string value = "";
                if (field == "genres")
                {
                    value = track.Genre;
                }
                else if (field == "artists")
                {
                    value = track.Artist;
                }
                else if (field == "years")
                {
                    value = track.Year;
                }

                value = (value ?? "").Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(query) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                LibraryFacetValue item;
                if (!values.TryGetValue(value, out item))
                {
                    item = new LibraryFacetValue();
                    item.Value = value;
                    values[value] = item;
                }
                item.Count++;
            }

            List<LibraryFacetValue> result = new List<LibraryFacetValue>(values.Values);
            result.Sort(delegate(LibraryFacetValue a, LibraryFacetValue b)
            {
                int count = b.Count.CompareTo(a.Count);
                if (count != 0) return count;
                return string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);
            });
            if (result.Count > limit)
            {
                result.RemoveRange(limit, result.Count - limit);
            }
            return result;
        }

        private static string[] BuildSearchTokens(SearchIntent intent, TrackInfo nowPlaying)
        {
            string query = intent == null
                ? ""
                : string.IsNullOrWhiteSpace(intent.RetrievalQuery) ? intent.QueryText : intent.RetrievalQuery;
            if (intent != null && IsRankingMode(intent.RankingMode))
            {
                query = StripRankingWords(query);
            }
            string originalQuery = intent == null ? "" : intent.QueryText;
            if (intent != null && IsRankingMode(intent.RankingMode))
            {
                originalQuery = "";
            }

            if (intent != null && intent.Similar && nowPlaying != null)
            {
                return NormalizationService.Tokenize(nowPlaying.Artist, nowPlaying.AlbumArtist, nowPlaying.Genre, nowPlaying.Mood, query, originalQuery);
            }

            return NormalizationService.Tokenize(query, originalQuery);
        }

        private static bool IsRankingMode(string mode)
        {
            mode = NormalizationService.NormalizeKey(mode);
            return mode == "favorites" || mode == "most_played" || mode == "recently_played" || mode == "least_played";
        }

        private static string StripRankingWords(string query)
        {
            string value = " " + NormalizationService.NormalizeKey(query) + " ";
            string[] words = new string[]
            {
                "favorite", "favorites", "favourite", "favourites", "loved", "liked",
                "most played", "frequently played", "often played", "often listened",
                "songs", "song", "tracks", "track", "music",
                "любимые", "любимых", "любимая", "любимую", "любимые песни", "любимые треки",
                "часто прослушиваемые", "чаще прослушиваемые", "прослушиваемые", "часто", "чаще"
            };
            for (int i = 0; i < words.Length; i++)
            {
                value = value.Replace(" " + words[i] + " ", " ");
            }
            return value.Trim();
        }

        private void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(dataPath);
                File.AppendAllText(Path.Combine(dataPath, "search.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
