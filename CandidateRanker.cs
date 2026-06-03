using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    public class CandidateRanker
    {
        public List<TrackInfo> Rank(IEnumerable<TrackInfo> candidates, SearchIntent intent, TrackInfo nowPlaying, int maxTracks)
        {
            List<TrackInfo> ranked = new List<TrackInfo>();
            string[] tokens = NormalizationService.Tokenize(intent == null ? "" : intent.QueryText);
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

                Score(track, intent, nowPlaying, tokens);
                if (track.Score > 0 || tokens.Length == 0 || (intent != null && intent.Similar))
                {
                    ranked.Add(track);
                }
            }

            ranked.Sort(delegate(TrackInfo a, TrackInfo b)
            {
                int score = b.Score.CompareTo(a.Score);
                if (score != 0) return score;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            List<TrackInfo> result = new List<TrackInfo>();
            int targetSeconds = intent == null ? 0 : intent.TargetDurationSeconds;
            int totalSeconds = 0;
            int count = Math.Min(Math.Max(1, maxTracks), ranked.Count);
            for (int i = 0; i < ranked.Count && result.Count < count; i++)
            {
                if (targetSeconds > 0 && totalSeconds >= targetSeconds)
                {
                    break;
                }

                TrackInfo track = ranked[i];
                track.Id = "track_" + (result.Count + 1);
                result.Add(track);
                totalSeconds += track.DurationSeconds;
            }

            return result;
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

            if (intent != null && intent.Similar && nowPlaying != null)
            {
                if (Same(track.Artist, nowPlaying.Artist))
                {
                    score += 45;
                    reasons.Add("same artist");
                }
                if (Same(track.AlbumArtist, nowPlaying.AlbumArtist))
                {
                    score += 35;
                    reasons.Add("same album artist");
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

            int rating = NormalizationService.ParseInt(track.Rating);
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

            int bpm = NormalizationService.ParseInt(track.Bpm);
            if (intent != null && intent.Calmer && bpm > 0)
            {
                score += bpm <= 105 ? 15 : -10;
            }
            if (intent != null && intent.Energetic && bpm > 0)
            {
                score += bpm >= 115 ? 15 : -5;
            }

            track.Score = score;
            track.ScoreReason = reasons.Count == 0 ? "" : string.Join(", ", reasons.ToArray());
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
