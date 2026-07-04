using System;
using System.Collections.Generic;
using System.Text;

namespace MusicBeePlugin
{
    public class PromptBuilder
    {
        private readonly PluginSettings settings;

        public PromptBuilder(PluginSettings settings)
        {
            this.settings = settings;
        }

        public string BuildSystemPrompt()
        {
            return
                "You are an AI assistant inside MusicBee. " +
                "Return only valid JSON, with no markdown. " +
                "Response schema: {\"message\":\"text\",\"chatTitle\":\"short title when requested\",\"actions\":[],\"toolRequests\":[]}. " +
                "Action schema: {\"message\":\"text\",\"chatTitle\":\"optional\",\"actions\":[{\"type\":\"create_playlist|queue_tracks_last|queue_tracks_next|play_track_now\",\"requiresConfirmation\":true,\"title\":\"text\",\"trackIds\":[\"track_1\"],\"explanation\":\"text\"}]}. " +
                "If more factual data is needed, return {\"message\":\"text\",\"toolRequests\":[{\"name\":\"get_now_playing|search_library|find_similar_tracks_basic|get_current_queue|get_library_facet|get_artist_profile|lookup_listenbrainz_similar_artists|lookup_wikipedia\",\"query\":\"text\",\"limit\":80}],\"actions\":[]}. Use limit up to 160 when the user asks for dozens of tracks. " +
                "For get_library_facet, query must be one of: tracks, genres, artists, years, custom_fields. Use 'facet: filter text' when filtering a facet, for example 'genres: metal' or 'tracks: industrial metal'. " +
                "For similar artist requests, request lookup_listenbrainz_similar_artists with the seed artist name. " +
                "For artist biography/library questions, request get_artist_profile first. If the user asks for more detailed background/history or outside-library facts, request lookup_wikipedia. " +
                "For lookup_wikipedia, query may be 'ru: artist name' or 'en: artist name'; choose the user's language when practical. " +
                "For custom_fields, distinguish configured field names from available MusicBee slots. Do not paste raw tool output; summarize it for the user. " +
                "For refine_previous tasks, preserve the previous proposal except where the user asks for changes, then satisfy the original constraints again. " +
                "Use English for internal tool names, parameters, and planning text, except artist, album, track, playlist, and field names. Reply to the user in the user's language. " +
                "Never invent file paths. Never use track IDs that are not provided. " +
                "Dangerous actions are forbidden. All write actions require confirmation.";
        }

        public string BuildUserPrompt(string userMessage, TrackInfo nowPlaying, List<TrackInfo> candidates, string toolResults, LibraryProfile profile)
        {
            return BuildUserPrompt(userMessage, nowPlaying, candidates, toolResults, profile, null);
        }

        public string BuildUserPrompt(string userMessage, TrackInfo nowPlaying, List<TrackInfo> candidates, string toolResults, LibraryProfile profile, SearchIntent intent)
        {
            return BuildUserPrompt(userMessage, nowPlaying, candidates, toolResults, profile, intent, null);
        }

        public string BuildUserPrompt(string userMessage, TrackInfo nowPlaying, List<TrackInfo> candidates, string toolResults, LibraryProfile profile, SearchIntent intent, List<ConversationMessage> history)
        {
            return BuildUserPrompt(userMessage, nowPlaying, candidates, toolResults, profile, intent, history, null, "", false);
        }

        public string BuildUserPrompt(string userMessage, TrackInfo nowPlaying, List<TrackInfo> candidates, string toolResults, LibraryProfile profile, SearchIntent intent, List<ConversationMessage> history, LastPlanContext lastPlan, string validationReport)
        {
            return BuildUserPrompt(userMessage, nowPlaying, candidates, toolResults, profile, intent, history, lastPlan, validationReport, false);
        }

        public string BuildUserPrompt(string userMessage, TrackInfo nowPlaying, List<TrackInfo> candidates, string toolResults, LibraryProfile profile, SearchIntent intent, List<ConversationMessage> history, LastPlanContext lastPlan, string validationReport, bool needsChatTitle)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("User request:");
            builder.AppendLine(userMessage ?? "");
            builder.AppendLine();
            if (needsChatTitle)
            {
                builder.AppendLine("This is the first assistant response in this chat. Include chatTitle in the final JSON response: a concise chat title based on the user's request, in the user's language when practical.");
                builder.AppendLine();
            }
            builder.AppendLine("Full conversation:");
            AppendHistory(builder, history);
            builder.AppendLine();
            if (lastPlan != null)
            {
                builder.AppendLine("Previous proposal context:");
                AppendLastPlan(builder, lastPlan);
                builder.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(validationReport))
            {
                builder.AppendLine("Validation report from previous model action:");
                builder.AppendLine(validationReport);
                builder.AppendLine("Return a corrected action using only available trackIds, or return actions: [] if correction is impossible.");
                builder.AppendLine();
            }
            if (profile != null && profile.TrackCount > 0)
            {
                builder.AppendLine(profile.ToPromptSummary());
                builder.AppendLine("Available library facets on request: tracks, genres, artists, years, custom_fields.");
            }
            AppendConstraints(builder, intent, candidates);
            builder.AppendLine();
            builder.AppendLine("Now playing:");
            AppendTrack(builder, nowPlaying);
            builder.AppendLine();
            builder.AppendLine("Candidate tracks. Choose only these trackIds for actions:");
            AppendTrackGroups(builder, candidates);
            if (!string.IsNullOrEmpty(toolResults))
            {
                builder.AppendLine();
                builder.AppendLine("Read-only tool results:");
                builder.AppendLine(toolResults);
            }
            builder.AppendLine();
            builder.AppendLine("If the user asks only for information, return actions: []. If proposing changes, return one action.");
            return builder.ToString();
        }

        private static void AppendHistory(StringBuilder builder, List<ConversationMessage> history)
        {
            if (history == null || history.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            for (int i = 0; i < history.Count; i++)
            {
                ConversationMessage message = history[i];
                builder.Append("- ").Append(message.Role).Append(": ");
                builder.AppendLine((message.Text ?? "").Replace("\r", " ").Replace("\n", " "));
            }
        }

        private static void AppendConstraints(StringBuilder builder, SearchIntent intent, List<TrackInfo> candidates)
        {
            if (intent == null)
            {
                return;
            }

            builder.Append("Selection constraints: ");
            builder.Append("use only provided trackIds");
            if (!string.IsNullOrWhiteSpace(intent.SelectionMode))
            {
                builder.Append("; selectionMode=").Append(intent.SelectionMode);
            }
            if (intent.DeduplicateTracks)
            {
                builder.Append("; avoid duplicate songs/versions");
            }
            if (intent.DiverseArtists)
            {
                builder.Append("; prefer different artists");
            }
            if (intent.DiverseAlbums)
            {
                builder.Append("; prefer different albums");
            }
            if (intent.ExcludeCurrentArtist)
            {
                builder.Append("; avoid current artist if possible");
            }
            if (intent.ExcludeInstrumental)
            {
                builder.Append("; exclude instrumental tracks");
            }
            if (intent.RequestedTrackCount > 0)
            {
                builder.Append("; requestedTrackCount=").Append(intent.RequestedTrackCount);
            }
            if (intent.RequestedTrackCountMin > 0 || intent.RequestedTrackCountMax > 0)
            {
                int min = intent.RequestedTrackCountMin <= 0 ? intent.RequestedTrackCountMax : intent.RequestedTrackCountMin;
                int max = intent.RequestedTrackCountMax <= 0 ? intent.RequestedTrackCountMin : intent.RequestedTrackCountMax;
                builder.Append("; requestedTrackCountRange=").Append(min).Append("-").Append(max);
                builder.Append("; choose at least ").Append(min).Append(" and at most ").Append(max).Append(" trackIds");
            }
            if (intent.TargetDurationSeconds > 0)
            {
                builder.Append("; targetDurationSeconds=").Append(intent.TargetDurationSeconds);
            }
            if (intent.ExcludedArtists.Count > 0)
            {
                builder.Append("; excludedArtists=").Append(JoinList(intent.ExcludedArtists));
            }
            if (intent.ExcludedAlbums.Count > 0)
            {
                builder.Append("; excludedAlbums=").Append(JoinList(intent.ExcludedAlbums));
            }
            if (!string.IsNullOrWhiteSpace(intent.RankingMode) && intent.RankingMode != "normal")
            {
                builder.Append("; rankingMode=").Append(intent.RankingMode);
                if (intent.RankingMode == "favorites")
                {
                    builder.Append("; prefer highest rating and strong play-count signals");
                }
                if (intent.RankingMode == "most_played")
                {
                    builder.Append("; preserve the order of most-played candidates unless there is a clear mismatch");
                }
            }
            builder.Append("; candidate artists=").Append(CountDistinctArtists(candidates));
            builder.AppendLine();
        }

        private static string JoinList(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(values[i]);
            }
            return builder.ToString();
        }

        private static void AppendLastPlan(StringBuilder builder, LastPlanContext lastPlan)
        {
            builder.AppendLine("originalRequest: " + lastPlan.OriginalRequest);
            builder.AppendLine("intent: " + lastPlan.IntentSummary);
            builder.AppendLine("selectedTrackCount: " + lastPlan.SelectedTrackCount);
            builder.AppendLine("totalDurationSeconds: " + lastPlan.TotalDurationSeconds);
            builder.AppendLine("targetDurationSeconds: " + lastPlan.TargetDurationSeconds);
            if (!string.IsNullOrWhiteSpace(lastPlan.TrackSnapshot))
            {
                builder.AppendLine("tracks:");
                builder.AppendLine(lastPlan.TrackSnapshot);
            }
        }

        private static int CountDistinctArtists(List<TrackInfo> tracks)
        {
            Dictionary<string, bool> artists = new Dictionary<string, bool>();
            if (tracks == null)
            {
                return 0;
            }
            foreach (TrackInfo track in tracks)
            {
                string key = NormalizationService.ArtistKey(track.Artist);
                if (!string.IsNullOrEmpty(key))
                {
                    artists[key] = true;
                }
            }
            return artists.Count;
        }

        public void AppendTrack(StringBuilder builder, TrackInfo track)
        {
            if (track == null)
            {
                builder.AppendLine("- none");
                return;
            }

            builder.Append("- id=").Append(track.Id);
            builder.Append("; artist=").Append(track.Artist);
            builder.Append("; title=").Append(track.Title);
            builder.Append("; album=").Append(track.Album);
            builder.Append("; genre=").Append(track.Genre);
            builder.Append("; duration=").Append(track.Duration);
            builder.Append("; score=").Append(track.Score);
            builder.Append("; albumArtist=").Append(track.AlbumArtist);
            builder.Append("; year=").Append(track.Year);
            builder.Append("; bpm=").Append(track.Bpm);
            builder.Append("; mood=").Append(track.Mood);
            builder.Append("; rating=").Append(track.Rating);
            builder.Append("; playCount=").Append(track.PlayCount);
            builder.Append("; skipCount=").Append(track.SkipCount);
            builder.Append("; reason=").Append(track.ScoreReason);
            builder.AppendLine();
        }

        public void AppendTrackGroups(StringBuilder builder, List<TrackInfo> tracks)
        {
            if (tracks == null || tracks.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            List<TrackGroup> groups = BuildGroups(tracks);
            for (int i = 0; i < groups.Count; i++)
            {
                TrackGroup group = groups[i];
                string commonGenre = CommonValue(group.Tracks, "genre");
                string commonYear = CommonValue(group.Tracks, "year");
                string commonMood = CommonValue(group.Tracks, "mood");

                builder.Append("Group ").Append(i + 1);
                builder.Append(": artist=").Append(group.Artist);
                builder.Append("; album=").Append(group.Album);
                if (!string.IsNullOrEmpty(group.AlbumArtist) && !Same(group.AlbumArtist, group.Artist))
                {
                    builder.Append("; albumArtist=").Append(group.AlbumArtist);
                }
                AppendCommon(builder, "genre", commonGenre);
                AppendCommon(builder, "year", commonYear);
                AppendCommon(builder, "mood", commonMood);
                builder.AppendLine();

                for (int j = 0; j < group.Tracks.Count; j++)
                {
                    AppendCompactTrack(builder, group.Tracks[j], commonGenre, commonYear, commonMood);
                }
            }
        }

        private static List<TrackGroup> BuildGroups(List<TrackInfo> tracks)
        {
            List<TrackGroup> groups = new List<TrackGroup>();
            Dictionary<string, TrackGroup> byKey = new Dictionary<string, TrackGroup>();
            foreach (TrackInfo track in tracks)
            {
                if (track == null)
                {
                    continue;
                }

                string key = GroupKey(track);
                TrackGroup group;
                if (!byKey.TryGetValue(key, out group))
                {
                    group = new TrackGroup();
                    group.Artist = track.Artist;
                    group.Album = track.Album;
                    group.AlbumArtist = track.AlbumArtist;
                    byKey[key] = group;
                    groups.Add(group);
                }
                group.Tracks.Add(track);
            }
            return groups;
        }

        private static string GroupKey(TrackInfo track)
        {
            return NormalizationService.ArtistKey(track == null ? "" : track.Artist) + "|" +
                NormalizationService.NormalizeKey(track == null ? "" : track.Album) + "|" +
                NormalizationService.ArtistKey(track == null ? "" : track.AlbumArtist);
        }

        private static string CommonValue(List<TrackInfo> tracks, string field)
        {
            string value = null;
            foreach (TrackInfo track in tracks ?? new List<TrackInfo>())
            {
                string current = Field(track, field);
                if (string.IsNullOrEmpty(current))
                {
                    return "";
                }
                if (value == null)
                {
                    value = current;
                    continue;
                }
                if (!Same(value, current))
                {
                    return "";
                }
            }
            return value ?? "";
        }

        private static string Field(TrackInfo track, string field)
        {
            if (track == null)
            {
                return "";
            }
            if (field == "genre") return track.Genre;
            if (field == "year") return track.Year;
            if (field == "mood") return track.Mood;
            return "";
        }

        private static void AppendCommon(StringBuilder builder, string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append("; ").Append(name).Append("=").Append(value);
            }
        }

        private static void AppendCompactTrack(StringBuilder builder, TrackInfo track, string commonGenre, string commonYear, string commonMood)
        {
            builder.Append("  - id=").Append(track.Id);
            builder.Append("; title=").Append(track.Title);
            builder.Append("; duration=").Append(track.Duration);
            builder.Append("; score=").Append(track.Score);
            AppendIfDifferent(builder, "genre", track.Genre, commonGenre);
            AppendIfDifferent(builder, "year", track.Year, commonYear);
            AppendIfDifferent(builder, "mood", track.Mood, commonMood);
            AppendIfPresent(builder, "bpm", track.Bpm);
            AppendIfPresent(builder, "rating", track.Rating);
            AppendIfPresent(builder, "playCount", track.PlayCount);
            AppendIfPresent(builder, "skipCount", track.SkipCount);
            AppendIfPresent(builder, "reason", track.ScoreReason);
            builder.AppendLine();
        }

        private static void AppendIfDifferent(StringBuilder builder, string name, string value, string commonValue)
        {
            if (!string.IsNullOrEmpty(value) && !Same(value, commonValue))
            {
                builder.Append("; ").Append(name).Append("=").Append(value);
            }
        }

        private static void AppendIfPresent(StringBuilder builder, string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append("; ").Append(name).Append("=").Append(value);
            }
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private class TrackGroup
        {
            public string Artist;
            public string Album;
            public string AlbumArtist;
            public readonly List<TrackInfo> Tracks = new List<TrackInfo>();
        }
    }
}
