using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace MusicBeePlugin
{
    public class IntentParser
    {
        private readonly PluginSettings settings;
        private readonly IAiProvider aiProvider;

        public IntentParser(PluginSettings settings)
            : this(settings, null)
        {
        }

        public IntentParser(PluginSettings settings, IAiProvider aiProvider)
        {
            this.settings = settings;
            this.aiProvider = aiProvider;
        }

        public SearchIntent Parse(string userMessage)
        {
            return Parse(userMessage, null, null);
        }

        public SearchIntent ParseLocal(string userMessage)
        {
            SearchIntent intent = new SearchIntent();
            intent.Task = "search";
            intent.QueryText = userMessage;
            intent.RetrievalQuery = userMessage;
            intent.MaxTracks = 40;
            intent.DeduplicateTracks = true;
            return intent;
        }

        public SearchIntent Parse(string userMessage, TrackInfo nowPlaying, LibraryProfile profile)
        {
            return Parse(userMessage, nowPlaying, profile, null, null);
        }

        public SearchIntent Parse(string userMessage, TrackInfo nowPlaying, LibraryProfile profile, List<ConversationMessage> history, LastPlanContext lastPlan)
        {
            return Parse(userMessage, nowPlaying, profile, history, lastPlan, CancellationToken.None);
        }

        public SearchIntent Parse(string userMessage, TrackInfo nowPlaying, LibraryProfile profile, List<ConversationMessage> history, LastPlanContext lastPlan, CancellationToken cancellationToken)
        {
            SearchIntent llmIntent = TryParseWithModel(userMessage, nowPlaying, profile, history, lastPlan, cancellationToken);
            if (llmIntent == null)
            {
                throw new InvalidOperationException("The model did not return a valid intent plan.");
            }
            if (string.IsNullOrEmpty(llmIntent.QueryText))
            {
                llmIntent.QueryText = userMessage;
            }
            if (string.IsNullOrEmpty(llmIntent.RetrievalQuery))
            {
                llmIntent.RetrievalQuery = userMessage;
            }
            if (llmIntent.MaxTracks <= 0)
            {
                llmIntent.MaxTracks = 40;
            }
            if (llmIntent.TargetDurationSeconds > 0 && llmIntent.RequestedTrackCount <= 0)
            {
                llmIntent.MaxTracks = Math.Max(llmIntent.MaxTracks, 120);
            }
            llmIntent.MaxTracks = Clamp(llmIntent.MaxTracks, 1, 200);
            return llmIntent;
        }

        private SearchIntent TryParseWithModel(string userMessage, TrackInfo nowPlaying, LibraryProfile profile, List<ConversationMessage> history, LastPlanContext lastPlan, CancellationToken cancellationToken)
        {
            if (aiProvider == null || string.IsNullOrWhiteSpace(userMessage))
            {
                return null;
            }

            try
            {
                string raw = aiProvider.SendChat(BuildIntentSystemPrompt(), BuildIntentUserPrompt(userMessage, nowPlaying, profile, history, lastPlan), cancellationToken);
                return ParseModelJson(raw);
            }
            catch
            {
                return null;
            }
        }

        private static SearchIntent ParseModelJson(string raw)
        {
            IDictionary<string, object> root = SimpleJson.Parse(ExtractJson(raw)) as IDictionary<string, object>;
            if (root == null)
            {
                return null;
            }

            SearchIntent intent = new SearchIntent();
            intent.Task = CleanToken(SimpleJson.GetString(root, "task"), 32);
            intent.SelectionMode = CleanToken(SimpleJson.GetString(root, "selectionMode"), 32);
            intent.SourceLanguage = CleanToken(SimpleJson.GetString(root, "sourceLanguage"), 16);
            intent.QueryText = SimpleJson.GetString(root, "queryText");
            intent.RetrievalQuery = CleanQuery(SimpleJson.GetString(root, "retrievalQuery"));
            intent.TurnKind = CleanToken(SimpleJson.GetString(root, "turnKind"), 32);
            intent.UserGoal = SimpleJson.GetString(root, "userGoal");
            intent.Similar = SimpleJson.GetBool(root, "similar", false);
            intent.Calmer = SimpleJson.GetBool(root, "calmer", false);
            intent.Energetic = SimpleJson.GetBool(root, "energetic", false);
            intent.ExcludeCurrentArtist = SimpleJson.GetBool(root, "excludeCurrentArtist", false);
            intent.DiverseArtists = SimpleJson.GetBool(root, "diverseArtists", false);
            intent.DiverseAlbums = SimpleJson.GetBool(root, "diverseAlbums", false);
            intent.DeduplicateTracks = SimpleJson.GetBool(root, "deduplicateTracks", true);
            intent.AllowVersions = SimpleJson.GetBool(root, "allowVersions", false);
            intent.WantsOnlyLocal = SimpleJson.GetBool(root, "wantsOnlyLocal", false);
            intent.TargetPlaylistName = CleanQuery(SimpleJson.GetString(root, "targetPlaylistName"));
            intent.PlaylistOperation = CleanToken(SimpleJson.GetString(root, "playlistOperation"), 32);
            intent.RankingMode = CleanToken(SimpleJson.GetString(root, "rankingMode"), 32);
            AddStringList(root, "excludedArtists", intent.ExcludedArtists);
            AddStringList(root, "boostArtists", intent.BoostArtists);
            AddStringList(root, "excludedAlbums", intent.ExcludedAlbums);
            intent.MaxTracksPerArtist = Clamp(GetInt(root, "maxTracksPerArtist", 0), 0, 20);
            intent.MaxTracksPerAlbum = Clamp(GetInt(root, "maxTracksPerAlbum", 0), 0, 20);
            intent.RequestedTrackCount = Clamp(GetInt(root, "requestedTrackCount", 0), 0, 200);
            intent.MaxTracks = Clamp(GetInt(root, "maxTracks", 0), 0, 200);
            int targetDurationSeconds = GetInt(root, "targetDurationSeconds", 0);
            int targetDurationMinutes = GetInt(root, "targetDurationMinutes", 0);
            intent.TargetDurationSeconds = targetDurationSeconds > 0 ? Clamp(targetDurationSeconds, 0, 24 * 3600) : Clamp(targetDurationMinutes, 0, 24 * 60) * 60;
            intent.Confidence = ClampDouble(GetDouble(root, "confidence", 0), 0, 1);
            object planValue;
            IList plan = root.TryGetValue("orchestrationPlan", out planValue) ? planValue as IList : null;
            if (plan != null)
            {
                foreach (object item in plan)
                {
                    string step = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(step))
                    {
                        intent.OrchestrationPlan.Add(step.Trim());
                    }
                }
            }
            intent.WasLlmEnhanced = true;
            return intent;
        }

        private static string BuildIntentSystemPrompt()
        {
            return
                "Extract search intent for a MusicBee music library. Return only compact valid JSON. " +
                "Do not choose final tracks, playlist names, or MusicBee IDs. Do not answer the user. " +
                "Infer meaning from the user request, conversation history, previous proposed playlist, typos, and any language. " +
                "Allowed task values: info, search, create_playlist, queue, play, edit_playlist, delete_playlist, delete_proposal, other. " +
                "Allowed playlistOperation values: none, append_tracks, replace_tracks, update_tracks, delete_playlist, delete_proposal, reorder_tracks. " +
                "Schema: {\"turnKind\":\"new_task|follow_up|refine_previous|clarify\",\"task\":\"info|search|create_playlist|queue|play|edit_playlist|delete_playlist|delete_proposal|other\",\"sourceLanguage\":\"iso_or_name\",\"userGoal\":\"plain English description\",\"orchestrationPlan\":[\"step 1\",\"step 2\"],\"selectionMode\":\"ranked|random|balanced|favorites\",\"similar\":false,\"calmer\":false,\"energetic\":false,\"excludeCurrentArtist\":false,\"diverseArtists\":false,\"diverseAlbums\":false,\"deduplicateTracks\":true,\"allowVersions\":false,\"wantsOnlyLocal\":false,\"excludedArtists\":[],\"boostArtists\":[],\"excludedAlbums\":[],\"targetDurationMinutes\":0,\"targetDurationSeconds\":0,\"requestedTrackCount\":0,\"maxTracks\":0,\"maxTracksPerArtist\":0,\"maxTracksPerAlbum\":0,\"targetPlaylistName\":\"\",\"playlistOperation\":\"none\",\"rankingMode\":\"normal|favorites|most_played|recently_played|least_played\",\"retrievalQuery\":\"short translated music-library keywords\",\"confidence\":0.0}. " +
                "If the user asks for favorite/loved/liked tracks in any language, set rankingMode=favorites and put only concrete filters such as artist/genre in retrievalQuery, not generic preference words. " +
                "If the user asks for frequently played/most played/often listened tracks in any language, set rankingMode=most_played and put only concrete filters such as artist/genre in retrievalQuery. " +
                "If the user asks for a duration in any language, always fill targetDurationMinutes or targetDurationSeconds; do not leave duration at 0 when orchestrationPlan mentions a duration. " +
                "The model is the orchestrator command source: decide the task and what retrievalQuery the executor must use. " +
                "The active executor can search the local MusicBee library and can use explicit read-only internet lookup tools when the user asks for outside-library facts or similar artists. " +
                "If the user asks for similar artists/performers/bands in any language, plan internet lookup through ListenBrainz and set retrievalQuery to the seed artist name. " +
                "If the user asks to tell more details, history, biography, or outside-library background about an artist, plan local artist profile plus Wikipedia lookup; keep task=info and retrievalQuery as the artist name. " +
                "If the user asks for random tracks, set selectionMode=random. " +
                "If the user asks to add tracks to playlist X in any language, set task=edit_playlist, playlistOperation=append_tracks, targetPlaylistName=X, not create_playlist. " +
                "If the user asks to replace/rebuild playlist X in any language, set task=edit_playlist, playlistOperation=replace_tracks, targetPlaylistName=X. " +
                "If the playlist target or intent is ambiguous and cannot be safely inferred from conversation/history, set turnKind=clarify and ask a concise clarification in userGoal. " +
                "For follow-up requests such as 'more like that' after a previous proposal, fill retrievalQuery with the concrete previous subject/artist/genre plus the new requirement; do not leave the executor to infer it from now playing. " +
                "Use turnKind=follow_up only for passive questions about the previous answer. If the user asks to search/recommend/create/edit/queue anything, keep the executable task and provide a full retrievalQuery. " +
                "If the user asks for similar music to the current/now playing track, that is a new_task unless they explicitly discuss the previous assistant proposal; retrievalQuery must mention current artist/title if that is the intended seed. " +
                "If the user edits the previous proposal, set turnKind=refine_previous and preserve previous target duration unless the user changes it. " +
                "If the user wants to edit/delete an existing AI playlist, set task edit_playlist/delete_playlist and targetPlaylistName.";
        }

        private static string BuildIntentUserPrompt(string userMessage, TrackInfo nowPlaying, LibraryProfile profile, List<ConversationMessage> history, LastPlanContext lastPlan)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("User request:");
            builder.AppendLine(userMessage ?? "");
            builder.AppendLine();
            builder.AppendLine("Recent conversation:");
            AppendRecentMessages(builder, history);
            builder.AppendLine();
            builder.AppendLine("Previous proposed playlist context:");
            AppendLastPlan(builder, lastPlan);
            builder.AppendLine();
            if (nowPlaying != null)
            {
                builder.Append("Now playing: artist=").Append(nowPlaying.Artist);
                builder.Append("; title=").Append(nowPlaying.Title);
                builder.Append("; album=").Append(nowPlaying.Album);
                builder.Append("; genre=").Append(nowPlaying.Genre);
                builder.Append("; mood=").Append(nowPlaying.Mood);
                builder.AppendLine();
            }
            if (profile != null && profile.TrackCount > 0)
            {
                builder.AppendLine(profile.ToPromptSummary());
            }
            return builder.ToString();
        }

        private static void AppendRecentMessages(StringBuilder builder, List<ConversationMessage> history)
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
                string text = (message.Text ?? "").Replace("\r", " ").Replace("\n", " ");
                builder.AppendLine(text);
            }
        }

        private static void AppendLastPlan(StringBuilder builder, LastPlanContext lastPlan)
        {
            if (lastPlan == null || string.IsNullOrEmpty(lastPlan.OriginalRequest))
            {
                builder.AppendLine("- none");
                return;
            }
            builder.AppendLine("originalRequest: " + lastPlan.OriginalRequest);
            builder.AppendLine("proposalId: " + lastPlan.ProposalId);
            builder.AppendLine("intent: " + lastPlan.IntentSummary);
            builder.AppendLine("targetDurationSeconds: " + lastPlan.TargetDurationSeconds);
            builder.AppendLine("selectedTrackCount: " + lastPlan.SelectedTrackCount);
            if (!string.IsNullOrEmpty(lastPlan.TrackSnapshot))
            {
                builder.AppendLine("tracks:");
                builder.AppendLine(lastPlan.TrackSnapshot);
            }
        }

        private static string ExtractJson(string raw)
        {
            string text = (raw ?? "").Trim();
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return text;
        }

        private static string CleanQuery(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length > 160)
            {
                value = value.Substring(0, 160);
            }
            return value;
        }

        private static string CleanToken(string value, int maxLength)
        {
            value = (value ?? "").Trim();
            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }
            return value;
        }

        private static int GetInt(IDictionary<string, object> root, string key, int fallback)
        {
            object value;
            if (root == null || !root.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                int parsed;
                return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
            }
        }

        private static double GetDouble(IDictionary<string, object> root, string key, double fallback)
        {
            object value;
            if (root == null || !root.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                double parsed;
                return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
            }
        }

        private static void AddStringList(IDictionary<string, object> root, string key, List<string> target)
        {
            if (root == null || target == null)
            {
                return;
            }

            object value;
            IList list = root.TryGetValue(key, out value) ? value as IList : null;
            if (list == null)
            {
                return;
            }

            foreach (object item in list)
            {
                string text = Convert.ToString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    target.Add(text.Trim());
                }
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }

        private static double ClampDouble(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }
    }
}
