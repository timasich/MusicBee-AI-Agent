using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    public class MusicBeeApiAdapter
    {
        private readonly Plugin.MusicBeeApiInterface api;

        public MusicBeeApiAdapter(Plugin.MusicBeeApiInterface api)
        {
            this.api = api;
        }

        public TrackInfo GetNowPlaying()
        {
            string fileUrl = api.NowPlaying_GetFileUrl();
            if (string.IsNullOrEmpty(fileUrl))
            {
                return null;
            }

            return BuildTrack("current_track", fileUrl, true);
        }

        public List<TrackInfo> GetQueueSummary(int maxTracks)
        {
            List<TrackInfo> tracks = new List<TrackInfo>();
            string[] files;
            if (!api.NowPlayingList_QueryFilesEx("", out files) || files == null)
            {
                return tracks;
            }

            int count = Math.Min(maxTracks, files.Length);
            for (int i = 0; i < count; i++)
            {
                tracks.Add(BuildTrack("queue_" + (i + 1), files[i], false));
            }

            return tracks;
        }

        public List<TrackInfo> SearchLibrary(string freeText, int maxTracks)
        {
            SearchIntent intent = new SearchIntent();
            intent.QueryText = freeText;
            intent.MaxTracks = maxTracks;
            return SearchLibrary(intent, null);
        }

        public List<TrackInfo> GetAllLibraryTracks()
        {
            List<TrackInfo> tracks = new List<TrackInfo>();
            string[] files;
            if (!api.Library_QueryFilesEx("", out files) || files == null)
            {
                return tracks;
            }

            for (int i = 0; i < files.Length; i++)
            {
                tracks.Add(BuildTrack("track_" + (i + 1), files[i], false));
            }

            return tracks;
        }

        public List<TrackInfo> SearchLibrary(SearchIntent intent, TrackInfo nowPlaying)
        {
            List<TrackInfo> tracks = new List<TrackInfo>();
            string[] files;
            if (!api.Library_QueryFilesEx("", out files) || files == null)
            {
                return tracks;
            }

            intent = intent ?? new SearchIntent();
            string query = string.IsNullOrWhiteSpace(intent.RetrievalQuery) ? intent.QueryText : intent.RetrievalQuery;
            string[] tokens = Tokenize(query + " " + (intent.QueryText ?? ""));
            int scanLimit = Math.Min(files.Length, 3000);
            for (int i = 0; i < scanLimit; i++)
            {
                TrackInfo track = BuildTrack("candidate", files[i], false);
                if (nowPlaying != null && string.Equals(track.FileUrl, nowPlaying.FileUrl, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (intent.ExcludeCurrentArtist && nowPlaying != null && Same(track.Artist, nowPlaying.Artist))
                {
                    continue;
                }

                ScoreTrack(track, nowPlaying, intent, tokens);
                if (track.Score > 0 || tokens.Length == 0 || intent.Similar)
                {
                    tracks.Add(track);
                }
            }

            if (string.Equals(intent.SelectionMode, "random", StringComparison.OrdinalIgnoreCase))
            {
                Shuffle(tracks);
            }
            else
            {
                tracks.Sort(delegate(TrackInfo a, TrackInfo b)
                {
                    int score = b.Score.CompareTo(a.Score);
                    if (score != 0) return score;
                    return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                });
            }

            int targetCount = Math.Min(Math.Max(1, intent.MaxTracks), tracks.Count);
            List<TrackInfo> result = new List<TrackInfo>();
            int totalDuration = 0;
            for (int i = 0; i < tracks.Count && result.Count < targetCount; i++)
            {
                TrackInfo track = tracks[i];
                if (intent.TargetDurationSeconds > 0 && totalDuration >= intent.TargetDurationSeconds)
                {
                    break;
                }

                track.Id = "track_" + (result.Count + 1);
                result.Add(track);
                totalDuration += track.DurationSeconds;
            }

            return result;
        }

        public List<TrackInfo> FindSimilarTracksBasic(TrackInfo current, int maxTracks)
        {
            SearchIntent intent = new SearchIntent();
            intent.Similar = true;
            intent.QueryText = "";
            intent.MaxTracks = maxTracks;
            if (current == null)
            {
                return new List<TrackInfo>();
            }

            return SearchLibrary(intent, current);
        }

        public List<LibraryFacetValue> GetCustomFields()
        {
            List<LibraryFacetValue> fields = new List<LibraryFacetValue>();
            Plugin.MetaDataType[] customFields = new Plugin.MetaDataType[]
            {
                Plugin.MetaDataType.Custom1,
                Plugin.MetaDataType.Custom2,
                Plugin.MetaDataType.Custom3,
                Plugin.MetaDataType.Custom4,
                Plugin.MetaDataType.Custom5,
                Plugin.MetaDataType.Custom6,
                Plugin.MetaDataType.Custom7,
                Plugin.MetaDataType.Custom8,
                Plugin.MetaDataType.Custom9,
                Plugin.MetaDataType.Custom10,
                Plugin.MetaDataType.Custom11,
                Plugin.MetaDataType.Custom12,
                Plugin.MetaDataType.Custom13,
                Plugin.MetaDataType.Custom14,
                Plugin.MetaDataType.Custom15,
                Plugin.MetaDataType.Custom16
            };

            for (int i = 0; i < customFields.Length; i++)
            {
                string name = "";
                try
                {
                    name = api.Setting_GetFieldName(customFields[i]);
                }
                catch
                {
                    name = "";
                }

                bool configured = !string.IsNullOrWhiteSpace(name);
                if (!configured)
                {
                    name = "Custom" + (i + 1);
                }

                LibraryFacetValue value = new LibraryFacetValue();
                value.Value = "slot=Custom" + (i + 1) + "; displayName=" + name + "; configured=" + (configured ? "true" : "false");
                value.Count = 0;
                fields.Add(value);
            }

            return fields;
        }

        private static void ScoreTrack(TrackInfo track, TrackInfo nowPlaying, SearchIntent intent, string[] tokens)
        {
            int score = 0;
            List<string> reasons = new List<string>();

            if (tokens.Length > 0)
            {
                int matches = CountTokenMatches(track, tokens);
                score += matches * 20;
                if (matches > 0)
                {
                    reasons.Add("text match");
                }
            }

            if (intent.Similar && nowPlaying != null)
            {
                if (Same(track.Artist, nowPlaying.Artist))
                {
                    score += 45;
                    reasons.Add("same artist as seed");
                }
                if (Same(track.AlbumArtist, nowPlaying.AlbumArtist))
                {
                    score += 35;
                    reasons.Add("same seed album artist");
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

            int playCount = ParseInt(track.PlayCount);
            if (playCount > 0)
            {
                score += Math.Min(15, playCount);
                reasons.Add("played before");
            }

            int skipCount = ParseInt(track.SkipCount);
            if (skipCount > 0)
            {
                score -= Math.Min(20, skipCount * 3);
            }

            track.Score = score;
            track.ScoreReason = reasons.Count == 0 ? "" : string.Join(", ", reasons.ToArray());
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

        public bool QueueLast(IEnumerable<TrackInfo> tracks)
        {
            return api.NowPlayingList_QueueFilesLast(ToUrls(tracks));
        }

        public bool QueueNext(IEnumerable<TrackInfo> tracks)
        {
            return api.NowPlayingList_QueueFilesNext(ToUrls(tracks));
        }

        public bool PlayNow(TrackInfo track)
        {
            return track != null && api.NowPlayingList_PlayNow(track.FileUrl);
        }

        public string CreatePlaylist(string playlistName, IEnumerable<TrackInfo> tracks)
        {
            string name = string.IsNullOrEmpty(playlistName) ? "AI Playlist" : playlistName;
            return api.Playlist_CreatePlaylist("", name, ToUrls(tracks));
        }

        public List<PlaylistRecord> GetPlaylists()
        {
            List<PlaylistRecord> records = new List<PlaylistRecord>();
            if (!api.Playlist_QueryPlaylists())
            {
                return records;
            }

            string playlistUrl;
            while (!string.IsNullOrEmpty(playlistUrl = api.Playlist_QueryGetNextPlaylist()))
            {
                PlaylistRecord record = new PlaylistRecord();
                record.PlaylistUrl = playlistUrl;
                record.Name = api.Playlist_GetName(playlistUrl);
                records.Add(record);
            }
            return records;
        }

        public List<TrackInfo> GetPlaylistTracks(string playlistUrl, int maxTracks)
        {
            List<TrackInfo> tracks = new List<TrackInfo>();
            if (string.IsNullOrEmpty(playlistUrl))
            {
                return tracks;
            }

            string[] files;
            if (!api.Playlist_QueryFilesEx(playlistUrl, out files) || files == null)
            {
                return tracks;
            }

            int count = Math.Min(files.Length, Math.Max(1, maxTracks));
            for (int i = 0; i < count; i++)
            {
                tracks.Add(BuildTrack("playlist_track_" + (i + 1), files[i], false));
            }
            return tracks;
        }

        public bool ReplacePlaylistTracks(string playlistUrl, IEnumerable<TrackInfo> tracks)
        {
            if (string.IsNullOrEmpty(playlistUrl))
            {
                return false;
            }
            return api.Playlist_SetFiles(playlistUrl, ToUrls(tracks));
        }

        public bool AppendPlaylistTracks(string playlistUrl, IEnumerable<TrackInfo> tracks)
        {
            if (string.IsNullOrEmpty(playlistUrl))
            {
                return false;
            }
            return api.Playlist_AppendFiles(playlistUrl, ToUrls(tracks));
        }

        public bool DeletePlaylist(string playlistUrl)
        {
            return !string.IsNullOrEmpty(playlistUrl) && api.Playlist_DeletePlaylist(playlistUrl);
        }

        private TrackInfo BuildTrack(string id, string fileUrl, bool nowPlaying)
        {
            TrackInfo track = new TrackInfo();
            track.Id = id;
            track.FileUrl = fileUrl;
            track.Title = GetTag(fileUrl, Plugin.MetaDataType.TrackTitle, nowPlaying);
            track.Artist = GetTag(fileUrl, Plugin.MetaDataType.Artist, nowPlaying);
            track.Album = GetTag(fileUrl, Plugin.MetaDataType.Album, nowPlaying);
            track.AlbumArtist = GetTag(fileUrl, Plugin.MetaDataType.AlbumArtist, nowPlaying);
            track.Genre = GetTag(fileUrl, Plugin.MetaDataType.Genre, nowPlaying);
            track.Year = GetTag(fileUrl, Plugin.MetaDataType.Year, nowPlaying);
            track.Bpm = GetTag(fileUrl, Plugin.MetaDataType.BeatsPerMin, nowPlaying);
            track.Mood = GetTag(fileUrl, Plugin.MetaDataType.Mood, nowPlaying);
            track.Rating = GetTag(fileUrl, Plugin.MetaDataType.Rating, nowPlaying);
            track.Duration = nowPlaying ? Convert.ToString(api.NowPlaying_GetDuration()) : api.Library_GetFileProperty(fileUrl, Plugin.FilePropertyType.Duration);
            track.PlayCount = api.Library_GetFileProperty(fileUrl, Plugin.FilePropertyType.PlayCount);
            track.SkipCount = api.Library_GetFileProperty(fileUrl, Plugin.FilePropertyType.SkipCount);
            track.LastPlayed = api.Library_GetFileProperty(fileUrl, Plugin.FilePropertyType.LastPlayed);
            return track;
        }

        private string GetTag(string fileUrl, Plugin.MetaDataType field, bool nowPlaying)
        {
            return nowPlaying ? api.NowPlaying_GetFileTag(field) : api.Library_GetFileTag(fileUrl, field);
        }

        private static bool Matches(TrackInfo track, string needle)
        {
            return Contains(track.Title, needle) ||
                Contains(track.Artist, needle) ||
                Contains(track.Album, needle) ||
                Contains(track.AlbumArtist, needle) ||
                Contains(track.Genre, needle) ||
                Contains(track.Mood, needle);
        }

        private static string[] Tokenize(string text)
        {
            text = (text ?? "").ToLowerInvariant();
            string[] raw = Regex.Split(text, "[^\\p{L}\\p{Nd}]+");
            List<string> tokens = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string token = raw[i].Trim();
                if (token.Length >= 3 && !IsStopWord(token))
                {
                    tokens.Add(token);
                }
            }
            return tokens.ToArray();
        }

        private static bool IsStopWord(string token)
        {
            string words = "|create|playlist|queue|add|find|similar|track|tracks|music|now|playing|what|for|the|and|song|songs|dobav|sozday|pohozh|pohozhi|spisok|pleylist|trek|treki|";
            return words.IndexOf("|" + token + "|", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountTokenMatches(TrackInfo track, string[] tokens)
        {
            int score = 0;
            string haystack = ((track.Title ?? "") + " " + (track.Artist ?? "") + " " + (track.Album ?? "") + " " +
                (track.AlbumArtist ?? "") + " " + (track.Genre ?? "") + " " + (track.Mood ?? "")).ToLowerInvariant();
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
            int a = ParseInt(left);
            int b = ParseInt(right);
            if (a <= 0 || b <= 0)
            {
                return 0;
            }

            int diff = Math.Abs(a - b);
            if (diff <= 5) return 20;
            if (diff <= 12) return 12;
            if (diff <= 20) return 6;
            return 0;
        }

        private static int ParseInt(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            int parsed;
            if (int.TryParse(value, out parsed))
            {
                return parsed;
            }

            double d;
            if (double.TryParse(value, out d))
            {
                return Convert.ToInt32(d);
            }

            return 0;
        }

        private static bool Contains(string value, string needle)
        {
            return (value ?? "").ToLowerInvariant().IndexOf(needle) >= 0;
        }

        private static bool IsSimilar(TrackInfo a, TrackInfo b)
        {
            return Same(a.Artist, b.Artist) ||
                Same(a.AlbumArtist, b.AlbumArtist) ||
                Same(a.Genre, b.Genre) ||
                Same(a.Mood, b.Mood);
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string[] ToUrls(IEnumerable<TrackInfo> tracks)
        {
            List<string> urls = new List<string>();
            foreach (TrackInfo track in tracks)
            {
                if (track != null && !string.IsNullOrEmpty(track.FileUrl))
                {
                    urls.Add(track.FileUrl);
                }
            }

            return urls.ToArray();
        }
    }
}
