using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    public class CandidateRanker
    {
        public List<TrackInfo> Rank(IEnumerable<TrackInfo> candidates, SearchIntent intent, TrackInfo nowPlaying, int maxTracks)
        {
            List<TrackInfo> ranked = new List<TrackInfo>();
            string query = intent == null ? "" : string.IsNullOrWhiteSpace(intent.RetrievalQuery) ? intent.QueryText : intent.RetrievalQuery;
            string[] tokens = NormalizationService.Tokenize(query, intent == null ? "" : intent.QueryText);
            foreach (TrackInfo track in candidates)
            {
                if (track == null)
                {
                    continue;
                }

                if (nowPlaying != null && string.Equals(track.FileUrl, nowPlaying.FileUrl, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsExcluded(track, intent))
                {
                    continue;
                }

                Score(track, intent, nowPlaying, tokens);
                if (IsRankingMode(intent))
                {
                    if (track.Score > 0)
                    {
                        ranked.Add(track);
                    }
                }
                else if (track.Score > 0 || tokens.Length == 0 || (intent != null && intent.Similar))
                {
                    ranked.Add(track);
                }
            }

            if (intent != null && string.Equals(intent.SelectionMode, "random", StringComparison.OrdinalIgnoreCase))
            {
                Shuffle(ranked);
            }
            else
            {
                ranked.Sort(delegate(TrackInfo a, TrackInfo b)
                {
                    int score = b.Score.CompareTo(a.Score);
                    if (score != 0) return score;
                    return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                });
            }

            List<TrackInfo> result = new List<TrackInfo>();
            int targetSeconds = intent == null ? 0 : intent.TargetDurationSeconds;
            int count = Math.Min(Math.Max(1, maxTracks), ranked.Count);
            SelectDiverse(ranked, result, intent, count, targetSeconds, false);
            if (NeedMore(result, count, targetSeconds))
            {
                SelectDiverse(ranked, result, intent, count, targetSeconds, true);
            }

            for (int i = 0; i < result.Count; i++)
            {
                result[i].Id = "track_" + (i + 1);
            }
            return result;
        }

        private static void SelectDiverse(List<TrackInfo> ranked, List<TrackInfo> result, SearchIntent intent, int count, int targetSeconds, bool relaxed)
        {
            Dictionary<string, bool> selectedFiles = new Dictionary<string, bool>();
            Dictionary<string, bool> canonicalTitles = new Dictionary<string, bool>();
            Dictionary<string, int> artistCounts = new Dictionary<string, int>();
            Dictionary<string, int> albumCounts = new Dictionary<string, int>();

            for (int i = 0; i < result.Count; i++)
            {
                AddSelectionState(result[i], selectedFiles, canonicalTitles, artistCounts, albumCounts, intent);
            }

            int artistLimit = intent == null ? 0 : intent.MaxTracksPerArtist;
            int albumLimit = intent == null ? 0 : intent.MaxTracksPerAlbum;
            if (relaxed)
            {
                artistLimit = artistLimit <= 0 ? 0 : artistLimit + 2;
                albumLimit = albumLimit <= 0 ? 0 : albumLimit + 2;
            }

            int totalSeconds = TotalDuration(result);
            for (int i = 0; i < ranked.Count && result.Count < count; i++)
            {
                if (targetSeconds > 0 && totalSeconds >= targetSeconds)
                {
                    break;
                }

                TrackInfo track = ranked[i];
                if (string.IsNullOrEmpty(track.FileUrl) || selectedFiles.ContainsKey(track.FileUrl))
                {
                    continue;
                }

                string canonical = NormalizationService.CanonicalTrackKey(track);
                if (intent == null || (intent.DeduplicateTracks && !intent.AllowVersions))
                {
                    if (!string.IsNullOrEmpty(canonical) && canonicalTitles.ContainsKey(canonical))
                    {
                        continue;
                    }
                }

                string artistKey = NormalizationService.ArtistKey(track.Artist);
                if (artistLimit > 0 && CountOf(artistCounts, artistKey) >= artistLimit)
                {
                    continue;
                }

                string albumKey = NormalizationService.AlbumKey(track.Album, track.AlbumArtist);
                if (albumLimit > 0 && CountOf(albumCounts, albumKey) >= albumLimit)
                {
                    continue;
                }

                result.Add(track);
                AddSelectionState(track, selectedFiles, canonicalTitles, artistCounts, albumCounts, intent);
                totalSeconds += track.DurationSeconds;
            }
        }

        private static void Shuffle(List<TrackInfo> tracks)
        {
            Random random = new Random();
            for (int i = tracks.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                TrackInfo temp = tracks[i];
                tracks[i] = tracks[j];
                tracks[j] = temp;
            }
        }

        private static bool NeedMore(List<TrackInfo> result, int count, int targetSeconds)
        {
            if (result.Count == 0)
            {
                return true;
            }

            if (targetSeconds > 0 && TotalDuration(result) < targetSeconds)
            {
                return true;
            }

            return result.Count < Math.Min(10, count);
        }

        private static void AddSelectionState(TrackInfo track, Dictionary<string, bool> selectedFiles, Dictionary<string, bool> canonicalTitles, Dictionary<string, int> artistCounts, Dictionary<string, int> albumCounts, SearchIntent intent)
        {
            if (!string.IsNullOrEmpty(track.FileUrl))
            {
                selectedFiles[track.FileUrl] = true;
            }

            string canonical = NormalizationService.CanonicalTrackKey(track);
            if (!string.IsNullOrEmpty(canonical))
            {
                canonicalTitles[canonical] = true;
            }

            Increment(artistCounts, NormalizationService.ArtistKey(track.Artist));
            Increment(albumCounts, NormalizationService.AlbumKey(track.Album, track.AlbumArtist));
        }

        private static int TotalDuration(List<TrackInfo> tracks)
        {
            int total = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                total += tracks[i].DurationSeconds;
            }
            return total;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            counts[key] = CountOf(counts, key) + 1;
        }

        private static int CountOf(Dictionary<string, int> counts, string key)
        {
            if (string.IsNullOrEmpty(key) || !counts.ContainsKey(key))
            {
                return 0;
            }
            return counts[key];
        }

        private static void Score(TrackInfo track, SearchIntent intent, TrackInfo nowPlaying, string[] tokens)
        {
            int score = 0;
            List<string> reasons = new List<string>();

            int textMatches = CountTokenMatches(track, tokens);
            if (textMatches > 0)
            {
                score += textMatches * 20;
                reasons.Add("text match");
            }

            int boostScore = BoostArtistScore(track, intent);
            if (boostScore > 0)
            {
                score += boostScore;
                reasons.Add("requested artist");
            }

            if (intent != null && intent.Similar && nowPlaying != null)
            {
                if (Same(track.Artist, nowPlaying.Artist))
                {
                    if (intent != null && (intent.ExcludeCurrentArtist || intent.DiverseArtists))
                    {
                        score -= 35;
                        reasons.Add("same artist penalty");
                    }
                    else
                    {
                        score += 45;
                        reasons.Add("same artist as seed");
                    }
                }
                if (Same(track.AlbumArtist, nowPlaying.AlbumArtist))
                {
                    if (intent != null && (intent.ExcludeCurrentArtist || intent.DiverseArtists))
                    {
                        score -= 20;
                    }
                    else
                    {
                        score += 35;
                        reasons.Add("same seed album artist");
                    }
                }
                if (Same(track.Genre, nowPlaying.Genre))
                {
                    score += 30;
                    reasons.Add("same genre");
                }
                if (Same(track.Mood, nowPlaying.Mood))
                {
                    score += 25;
                    reasons.Add("same mood");
                }

                int bpmScore = BpmScore(track.Bpm, nowPlaying.Bpm);
                if (bpmScore > 0)
                {
                    score += bpmScore;
                    reasons.Add("close BPM");
                }
            }

            int rating = NormalizationService.NormalizeRating(track.Rating);
            if (rating >= 70)
            {
                score += 15;
                reasons.Add("high rating");
            }

            int playCount = NormalizationService.ParseInt(track.PlayCount);
            if (playCount > 0)
            {
                score += Math.Min(15, playCount);
                reasons.Add("played before");
            }

            int skipCount = NormalizationService.ParseInt(track.SkipCount);
            if (skipCount > 0)
            {
                score -= Math.Min(20, skipCount * 3);
            }

            int rankingScore = RankingScore(track, intent, rating, playCount, skipCount, reasons);
            if (rankingScore != 0)
            {
                score += rankingScore;
            }

            track.Score = score;
            track.ScoreReason = reasons.Count == 0 ? "" : string.Join(", ", reasons.ToArray());
        }

        private static bool IsRankingMode(SearchIntent intent)
        {
            return intent != null && IsRankingMode(intent.RankingMode);
        }

        private static bool IsRankingMode(string mode)
        {
            mode = NormalizationService.NormalizeKey(mode);
            return mode == "favorites" || mode == "most_played" || mode == "recently_played" || mode == "least_played";
        }

        private static int RankingScore(TrackInfo track, SearchIntent intent, int rating, int playCount, int skipCount, List<string> reasons)
        {
            string mode = intent == null ? "" : NormalizationService.NormalizeKey(intent.RankingMode);
            if (mode == "favorites")
            {
                int score = 0;
                if (rating > 0)
                {
                    score += rating * 3;
                    reasons.Add("favorite rating");
                }
                if (playCount > 0)
                {
                    score += Math.Min(160, playCount * 8);
                    reasons.Add("play count signal");
                }
                if (skipCount > 0)
                {
                    score -= Math.Min(80, skipCount * 10);
                }
                return score;
            }
            if (mode == "most_played")
            {
                if (playCount <= 0)
                {
                    return 0;
                }
                reasons.Add("most played");
                return Math.Min(10000, playCount * 100) + Math.Min(50, rating / 2) - Math.Min(100, skipCount * 5);
            }
            if (mode == "least_played")
            {
                if (playCount > 2)
                {
                    return 0;
                }
                reasons.Add("least played");
                return Math.Max(1, 200 - playCount * 50) + Math.Min(50, rating / 2);
            }
            return 0;
        }

        private static bool IsExcluded(TrackInfo track, SearchIntent intent)
        {
            if (track == null || intent == null)
            {
                return false;
            }
            string artist = NormalizationService.ArtistKey(track.Artist);
            for (int i = 0; i < intent.ExcludedArtists.Count; i++)
            {
                string excluded = NormalizationService.ArtistKey(intent.ExcludedArtists[i]);
                if (!string.IsNullOrEmpty(excluded) && (artist.IndexOf(excluded) >= 0 || excluded.IndexOf(artist) >= 0))
                {
                    return true;
                }
            }
            string album = NormalizationService.NormalizeKey(track.Album);
            for (int i = 0; i < intent.ExcludedAlbums.Count; i++)
            {
                string excludedAlbum = NormalizationService.NormalizeKey(intent.ExcludedAlbums[i]);
                if (!string.IsNullOrEmpty(excludedAlbum) && album.IndexOf(excludedAlbum) >= 0)
                {
                    return true;
                }
            }
            if (AvoidInstrumental(intent) && LooksInstrumental(track))
            {
                return true;
            }
            return false;
        }

        private static bool AvoidInstrumental(SearchIntent intent)
        {
            string text = NormalizationService.NormalizeKey((intent.QueryText ?? "") + " " + (intent.RetrievalQuery ?? "") + " " + (intent.UserGoal ?? ""));
            if (text.IndexOf("\u043d\u0435 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442") >= 0 ||
                text.IndexOf("\u0431\u0435\u0437 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442") >= 0 ||
                text.IndexOf("\u043d\u0435 \u0445\u043e\u0447\u0443 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442") >= 0)
            {
                return true;
            }
            return text.IndexOf("non instrumental") >= 0 ||
                text.IndexOf("noninstrumental") >= 0 ||
                text.IndexOf("no instrumental") >= 0 ||
                text.IndexOf("without instrumental") >= 0 ||
                text.IndexOf("не инструмент") >= 0 ||
                text.IndexOf("без инструмент") >= 0 ||
                text.IndexOf("не хочу инструмент") >= 0;
        }

        private static bool LooksInstrumental(TrackInfo track)
        {
            string text = NormalizationService.NormalizeKey((track.Title ?? "") + " " + (track.Album ?? "") + " " + (track.Genre ?? "") + " " + (track.Mood ?? ""));
            if (text.IndexOf("\u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442") >= 0)
            {
                return true;
            }
            return text.IndexOf("instrumental") >= 0 ||
                text.IndexOf("инструмент") >= 0 ||
                text.IndexOf("orchestral") >= 0 ||
                text.IndexOf("score") >= 0;
        }

        private static int BoostArtistScore(TrackInfo track, SearchIntent intent)
        {
            if (track == null || intent == null)
            {
                return 0;
            }
            string artist = NormalizationService.ArtistKey(track.Artist);
            for (int i = 0; i < intent.BoostArtists.Count; i++)
            {
                string boost = NormalizationService.ArtistKey(intent.BoostArtists[i]);
                if (!string.IsNullOrEmpty(boost) && (artist.IndexOf(boost) >= 0 || boost.IndexOf(artist) >= 0))
                {
                    return 80;
                }
            }
            return 0;
        }

        private static int CountTokenMatches(TrackInfo track, string[] tokens)
        {
            int score = 0;
            string haystack = NormalizationService.NormalizeKey(
                (track.Title ?? "") + " " + (track.Artist ?? "") + " " + (track.Album ?? "") + " " +
                (track.AlbumArtist ?? "") + " " + (track.Genre ?? "") + " " + (track.Mood ?? ""));
            for (int i = 0; i < tokens.Length; i++)
            {
                if (haystack.IndexOf(tokens[i]) >= 0)
                {
                    score++;
                }
            }
            return score;
        }

        private static int BpmScore(string left, string right)
        {
            int a = NormalizationService.ParseInt(left);
            int b = NormalizationService.ParseInt(right);
            if (a <= 0 || b <= 0) return 0;
            int diff = Math.Abs(a - b);
            if (diff <= 5) return 20;
            if (diff <= 12) return 12;
            if (diff <= 20) return 6;
            return 0;
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
