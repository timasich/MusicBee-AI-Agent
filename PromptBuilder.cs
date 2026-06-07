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
                "If more factual data is needed, return {\"message\":\"text\",\"toolRequests\":[{\"name\":\"get_now_playing|search_library|find_similar_tracks_basic|get_current_queue|get_library_facet|get_artist_profile|lookup_listenbrainz_similar_artists|lookup_wikipedia\",\"query\":\"text\",\"limit\":40}],\"actions\":[]}. " +
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
            foreach (TrackInfo track in candidates)
            {
                AppendTrack(builder, track);
            }
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
            if (intent.RequestedTrackCount > 0)
            {
                builder.Append("; requestedTrackCount=").Append(intent.RequestedTrackCount);
            }
            if (intent.TargetDurationSeconds > 0)
            {
                builder.Append("; targetDurationSeconds=").Append(intent.TargetDurationSeconds);
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
    }
}
