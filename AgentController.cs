using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace MusicBeePlugin
{
    public class AgentController
    {
        private readonly MusicBeeApiAdapter musicBee;
        private readonly LibrarySearchService librarySearch;
        private readonly IAiProvider aiProvider;
        private readonly PluginSettings settings;
        private readonly AiOwnedPlaylistRegistry playlistRegistry;
        private readonly IntentParser intentParser;
        private readonly PromptBuilder promptBuilder;
        private readonly AiResponseParser responseParser = new AiResponseParser();
        private readonly ActionValidator actionValidator = new ActionValidator();
        private readonly ExternalLookupService externalLookup = new ExternalLookupService();
        private readonly ConversationStore conversationStore;
        private readonly string dataPath;
        private readonly Random random = new Random();

        public AgentController(MusicBeeApiAdapter musicBee, IAiProvider aiProvider, PluginSettings settings, string dataPath)
        {
            this.musicBee = musicBee;
            this.librarySearch = new LibrarySearchService(musicBee, dataPath);
            this.aiProvider = aiProvider;
            this.settings = settings;
            this.playlistRegistry = new AiOwnedPlaylistRegistry(dataPath);
            this.intentParser = new IntentParser(settings, aiProvider);
            this.promptBuilder = new PromptBuilder(settings);
            this.conversationStore = new ConversationStore(dataPath);
            this.dataPath = dataPath;
        }

        public string NewConversation()
        {
            string id = conversationStore.NewConversation();
            return conversationStore.GetConversationTitle(id);
        }

        public List<ConversationSummary> ListConversations()
        {
            return conversationStore.ListConversations();
        }

        public List<ConversationMessage> OpenConversation(string id)
        {
            conversationStore.OpenConversation(id);
            return conversationStore.GetMessages(id);
        }

        public void RenameConversation(string id, string title)
        {
            conversationStore.RenameConversation(id, title);
        }

        public AgentResult Send(string userMessage)
        {
            return Send(userMessage, CancellationToken.None, null);
        }

        public AgentResult Send(string userMessage, CancellationToken cancellationToken, Action<string> traceSink)
        {
            AgentResult result = new AgentResult();
            List<string> trace = result.OrchestratorTrace;
            try
            {
                AddTrace(trace, traceSink, "Accepted user request.");
                cancellationToken.ThrowIfCancellationRequested();
                conversationStore.SaveMessage("user", userMessage);
                LastPlanContext lastPlan = conversationStore.LastPlan;
                List<ConversationMessage> history = conversationStore.GetMessages(conversationStore.ActiveConversationId);
                bool needsChatTitle = !HasAssistantMessage(history);
                TrackInfo nowPlaying = musicBee.GetNowPlaying();
                AddTrace(trace, traceSink, "Read now playing: " + (nowPlaying == null ? "none" : nowPlaying.DisplayName) + ".");
                LibraryProfile profile = librarySearch.GetProfile();
                AddTrace(trace, traceSink, "Loaded library profile: " + (profile == null ? "none" : profile.TrackCount + " track(s)") + ".");
                AddTrace(trace, traceSink, "Sending request and conversation context to model for intent planning.");
                cancellationToken.ThrowIfCancellationRequested();
                SearchIntent intent = intentParser.Parse(userMessage, nowPlaying, profile, history, lastPlan, cancellationToken);
                RepairStateFromPreviousPlan(intent, lastPlan, trace, traceSink);
                AddTrace(trace, traceSink, "Model intent decision: turnKind=" + intent.TurnKind + ", task=" + intent.Task + ", query='" + intent.RetrievalQuery + "', rankingMode=" + intent.RankingMode + ", requestedTrackCount=" + intent.RequestedTrackCount + ", confidence=" + intent.Confidence + ".");
                AddPlanTrace(trace, traceSink, intent);
                if (!ValidateOrRepairIntent(intent, lastPlan, trace, traceSink))
                {
                    result.Message = "The requested operation is not supported by the MusicBee AI Agent yet.";
                    conversationStore.SaveMessage("assistant", result.Message);
                    return result;
                }

                string effectiveMessage = userMessage;
                if ((IsTurn(intent, "follow_up") || IsTurn(intent, "refine_previous")) && lastPlan == null && IsActionableIntent(intent))
                {
                    AddTrace(trace, traceSink, "Repaired turnKind to new_task because no previous proposal exists and the request is actionable.");
                    intent.TurnKind = "new_task";
                }

                if (IsTurn(intent, "follow_up") && lastPlan != null)
                {
                    AddTrace(trace, traceSink, "Model classified this as follow-up. Passing full conversation and available proposal context to the model.");
                }

                if (IsTurn(intent, "refine_previous") && lastPlan != null)
                {
                    if (intent.TargetDurationSeconds <= 0)
                    {
                        intent.TargetDurationSeconds = lastPlan.TargetDurationSeconds;
                        AddTrace(trace, traceSink, "Preserved previous target duration: " + lastPlan.TargetDurationSeconds + " second(s).");
                    }
                    if (string.IsNullOrWhiteSpace(intent.TargetPlaylistName) && intent.Task == "edit_playlist")
                    {
                        intent.Task = "create_playlist";
                        intent.PlaylistOperation = "none";
                        AddTrace(trace, traceSink, "Repaired refinement task from edit_playlist to proposal refinement because no target playlist was specified.");
                    }
                    effectiveMessage = lastPlan.OriginalRequest + ". Refinement: " + userMessage;
                    AddTrace(trace, traceSink, "Model classified this as refinement of previous proposal.");
                }

                if (IsPlaylistManagementIntent(intent))
                {
                    AddTrace(trace, traceSink, "Selected playlist management workflow.");
                    HandlePlaylistManagement(intent, userMessage, result, trace, traceSink, cancellationToken, needsChatTitle);
                    conversationStore.SaveMessage("assistant", result.Message);
                    Log("request", userMessage);
                    Log("trace", FormatTrace(trace));
                    return result;
                }

                if (IsTurn(intent, "clarify"))
                {
                    result.Message = string.IsNullOrEmpty(intent.UserGoal) ? "I need a little more detail before I can act on that." : intent.UserGoal;
                    conversationStore.SaveMessage("assistant", result.Message);
                    AddTrace(trace, traceSink, "Model requested clarification.");
                    Log("request", userMessage);
                    Log("trace", FormatTrace(trace));
                    return result;
                }

                AddTrace(trace, traceSink, "Selected model-directed orchestration workflow.");
                List<TrackInfo> candidates = BuildCandidates(intent, nowPlaying);
                if (IsTurn(intent, "refine_previous") && lastPlan != null)
                {
                    List<TrackInfo> previousTracks = ResolvePreviousProposalTracks(lastPlan);
                    candidates = MergeRefinementCandidates(previousTracks, candidates);
                    AddTrace(trace, traceSink, "Added previous proposal context to candidate pool: previous=" + previousTracks.Count + ", merged=" + candidates.Count + ".");
                }
                AddTrace(trace, traceSink, "Built simple candidate shortlist: " + candidates.Count + " track(s).");
                CandidateSet candidateSet = new CandidateSet();
                AddCandidates(candidateSet, candidates);

                string systemPrompt = promptBuilder.BuildSystemPrompt();
                AddTrace(trace, traceSink, "Sending simple prompt to model with " + candidates.Count + " candidate track(s).");
                cancellationToken.ThrowIfCancellationRequested();
                string toolResults = "";
                string raw = aiProvider.SendChat(systemPrompt, promptBuilder.BuildUserPrompt(userMessage, nowPlaying, candidates, toolResults, profile, intent, history, lastPlan, "", needsChatTitle), cancellationToken);
                AiChatResponse response = ParseAiResponseWithRepair(raw);
                AddTrace(trace, traceSink, "Received model response: actions=" + response.Actions.Count + ", toolRequests=" + response.ToolRequests.Count + ".");

                if (response.ToolRequests.Count > 0)
                {
                    toolResults = ExecuteReadOnlyTools(response.ToolRequests, candidateSet, nowPlaying);
                    AddTrace(trace, traceSink, "Executed read-only tool requests and sent follow-up prompt to model.");
                    raw = aiProvider.SendChat(systemPrompt, promptBuilder.BuildUserPrompt(userMessage, nowPlaying, new List<TrackInfo>(candidateSet.Tracks), toolResults, profile, intent, history, lastPlan, "", needsChatTitle), cancellationToken);
                    response = ParseAiResponseWithRepair(raw);
                    AddTrace(trace, traceSink, "Received model response after tool pass: actions=" + response.Actions.Count + ".");
                }

                result.Message = AddConstraintFeedback(response.Message, intent, candidates);
                if (response.Actions.Count > 0)
                {
                    result.PendingAction = actionValidator.Validate(response.Actions[0], candidateSet, intent);
                    AddTrace(trace, traceSink, "Validated model action: valid=" + result.PendingAction.IsValid + ".");
                    if (!result.PendingAction.IsValid)
                    {
                        string validationReport = BuildValidationReport(result.PendingAction, response.Actions[0], intent);
                        AddTrace(trace, traceSink, "Sending validation report to model for one correction pass: " + result.PendingAction.ValidationError);
                        raw = aiProvider.SendChat(systemPrompt, promptBuilder.BuildUserPrompt(userMessage, nowPlaying, new List<TrackInfo>(candidateSet.Tracks), toolResults, profile, intent, history, lastPlan, validationReport, needsChatTitle), cancellationToken);
                        response = ParseAiResponseWithRepair(raw);
                        AddTrace(trace, traceSink, "Received model response after validation repair: actions=" + response.Actions.Count + ", toolRequests=" + response.ToolRequests.Count + ".");
                        if (response.ToolRequests.Count > 0)
                        {
                            toolResults = toolResults + "\r\n" + ExecuteReadOnlyTools(response.ToolRequests, candidateSet, nowPlaying);
                            AddTrace(trace, traceSink, "Executed repair read-only tool requests and sent final correction prompt to model.");
                            raw = aiProvider.SendChat(systemPrompt, promptBuilder.BuildUserPrompt(userMessage, nowPlaying, new List<TrackInfo>(candidateSet.Tracks), toolResults, profile, intent, history, lastPlan, validationReport, needsChatTitle), cancellationToken);
                            response = ParseAiResponseWithRepair(raw);
                            AddTrace(trace, traceSink, "Received final model response after repair tools: actions=" + response.Actions.Count + ".");
                        }
                        result.Message = AddConstraintFeedback(response.Message, intent, candidates);
                        if (response.Actions.Count > 0)
                        {
                            result.PendingAction = actionValidator.Validate(response.Actions[0], candidateSet, intent);
                            AddTrace(trace, traceSink, "Validated repaired model action: valid=" + result.PendingAction.IsValid + ".");
                        }
                    }
                    SaveSimplePlanContext(effectiveMessage, intent, result.PendingAction, result.Message);
                }
                conversationStore.SaveMessage("assistant", result.Message);
                ApplyChatTitleIfNeeded(result, response, needsChatTitle);

                Log("request", userMessage);
                Log("response", raw);
                Log("trace", FormatTrace(trace));
            }
            catch (OperationCanceledException)
            {
                result.Error = "Request cancelled.";
                AddTrace(trace, traceSink, "Request cancelled by user.");
                Log("trace", FormatTrace(trace));
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                AddTrace(trace, traceSink, "Request failed: " + ex.Message);
                Log("error", ex.ToString());
                Log("trace", FormatTrace(trace));
            }

            return result;
        }

        public string Execute(PendingAction pending)
        {
            return Execute(pending, null, null);
        }

        public string Execute(PendingAction pending, string overrideType, List<TrackInfo> selectedTracks)
        {
            if (pending == null || pending.Action == null || !pending.IsValid)
            {
                return "Action is not valid.";
            }

            if (selectedTracks != null)
            {
                pending.Tracks = selectedTracks;
            }

            string actionType = string.IsNullOrEmpty(overrideType) ? pending.Action.Type : overrideType;

            if (actionType != "delete_ai_playlist" && (pending.Tracks == null || pending.Tracks.Count == 0))
            {
                return "No tracks selected.";
            }

            bool ok = false;
            string message;
            switch (actionType)
            {
                case "queue_tracks_last":
                    ok = musicBee.QueueLast(pending.Tracks);
                    message = ok ? "Added tracks to the end of Now Playing." : "MusicBee did not add the tracks.";
                    break;
                case "queue_tracks_next":
                    ok = musicBee.QueueNext(pending.Tracks);
                    message = ok ? "Queued tracks next." : "MusicBee did not queue the tracks.";
                    break;
                case "create_playlist":
                    string playlistName = playlistRegistry.NormalizeName(pending.Action.Title);
                    string playlistUrl = musicBee.CreatePlaylist(playlistName, pending.Tracks);
                    ok = !string.IsNullOrEmpty(playlistUrl);
                    if (ok)
                    {
                        playlistRegistry.Register(playlistUrl, playlistName);
                    }
                    message = ok ? "Created playlist: " + playlistName : "MusicBee did not create the playlist.";
                    break;
                case "replace_ai_playlist":
                    ok = musicBee.ReplacePlaylistTracks(pending.Action.PlaylistUrl, pending.Tracks);
                    message = ok ? "Updated playlist: " + pending.Action.PlaylistName : "MusicBee did not update the playlist.";
                    break;
                case "append_to_playlist":
                    ok = musicBee.AppendPlaylistTracks(pending.Action.PlaylistUrl, pending.Tracks);
                    message = ok ? "Appended tracks to playlist: " + pending.Action.PlaylistName : "MusicBee did not append the tracks.";
                    break;
                case "delete_ai_playlist":
                    ok = musicBee.DeletePlaylist(pending.Action.PlaylistUrl);
                    message = ok ? "Deleted playlist: " + pending.Action.PlaylistName : "MusicBee did not delete the playlist.";
                    break;
                case "play_track_now":
                    ok = pending.Tracks.Count == 1 && musicBee.PlayNow(pending.Tracks[0]);
                    message = ok ? "Started playback." : "MusicBee did not start playback.";
                    break;
                default:
                    message = "Unsupported action: " + actionType;
                    break;
            }

            Log("execute", actionType + ": " + message);
            return message;
        }

        private List<TrackInfo> BuildCandidates(SearchIntent intent, TrackInfo nowPlaying)
        {
            return librarySearch.Search(intent, nowPlaying, settings);
        }

        private void ApplyChatTitleIfNeeded(AgentResult result, AiChatResponse response, bool needsChatTitle)
        {
            if (!needsChatTitle || response == null || string.IsNullOrWhiteSpace(response.ChatTitle))
            {
                return;
            }

            ApplyChatTitleIfNeeded(result, response.ChatTitle, needsChatTitle);
        }

        private void ApplyChatTitleIfNeeded(AgentResult result, string chatTitle, bool needsChatTitle)
        {
            if (!needsChatTitle || string.IsNullOrWhiteSpace(chatTitle))
            {
                return;
            }

            string title = chatTitle.Trim();
            if (title.Length > 80)
            {
                title = title.Substring(0, 80).Trim();
            }
            if (title.Length == 0)
            {
                return;
            }
            conversationStore.RenameConversation(conversationStore.ActiveConversationId, title);
            result.ChatTitle = title;
        }

        private static bool HasAssistantMessage(List<ConversationMessage> history)
        {
            foreach (ConversationMessage message in history ?? new List<ConversationMessage>())
            {
                if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private List<TrackInfo> ResolvePreviousProposalTracks(LastPlanContext lastPlan)
        {
            List<TrackInfo> result = new List<TrackInfo>();
            if (lastPlan == null || string.IsNullOrWhiteSpace(lastPlan.TrackSnapshot))
            {
                return result;
            }

            string[] lines = lastPlan.TrackSnapshot.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                Dictionary<string, string> fields = ParseSnapshotLine(lines[i]);
                string artist = GetField(fields, "artist");
                string title = GetField(fields, "title");
                string album = GetField(fields, "album");
                TrackInfo resolved = ResolveTrackFromLibrary(artist, title, album);
                if (resolved == null)
                {
                    continue;
                }
                string key = string.IsNullOrEmpty(resolved.FileUrl) ? resolved.DisplayName : resolved.FileUrl;
                if (!seen.ContainsKey(key))
                {
                    seen[key] = true;
                    result.Add(resolved);
                }
            }
            return result;
        }

        private TrackInfo ResolveTrackFromLibrary(string artist, string title, string album)
        {
            SearchIntent intent = new SearchIntent();
            intent.QueryText = (artist + " " + title + " " + album).Trim();
            intent.RetrievalQuery = intent.QueryText;
            intent.MaxTracks = 20;
            List<TrackInfo> candidates = librarySearch.Search(intent, null, settings);
            string artistKey = NormalizationService.ArtistKey(artist);
            string titleKey = NormalizationService.NormalizeKey(title);
            string albumKey = NormalizationService.NormalizeKey(album);
            foreach (TrackInfo track in candidates)
            {
                if (!string.IsNullOrEmpty(artistKey) && NormalizationService.ArtistKey(track.Artist) != artistKey)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(titleKey) && NormalizationService.NormalizeKey(track.Title) != titleKey)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(albumKey) && NormalizationService.NormalizeKey(track.Album) != albumKey)
                {
                    continue;
                }
                return track;
            }
            return candidates.Count == 0 ? null : candidates[0];
        }

        private static List<TrackInfo> MergeRefinementCandidates(List<TrackInfo> previousTracks, List<TrackInfo> newCandidates)
        {
            List<TrackInfo> result = new List<TrackInfo>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            AddUniqueTracks(result, seen, previousTracks);
            AddUniqueTracks(result, seen, newCandidates);
            ReassignIds(result, "track_");
            return result;
        }

        private static void AddUniqueTracks(List<TrackInfo> target, Dictionary<string, bool> seen, List<TrackInfo> tracks)
        {
            foreach (TrackInfo track in tracks ?? new List<TrackInfo>())
            {
                string key = string.IsNullOrEmpty(track.FileUrl) ? NormalizationService.CanonicalTrackKey(track) : track.FileUrl;
                if (string.IsNullOrEmpty(key) || seen.ContainsKey(key))
                {
                    continue;
                }
                seen[key] = true;
                target.Add(track);
            }
        }

        private static Dictionary<string, string> ParseSnapshotLine(string line)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string text = (line ?? "").Trim();
            if (text.StartsWith("- "))
            {
                text = text.Substring(2);
            }
            string[] parts = text.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                int separator = part.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }
                result[part.Substring(0, separator).Trim()] = part.Substring(separator + 1).Trim();
            }
            return result;
        }

        private static string GetField(Dictionary<string, string> fields, string name)
        {
            string value;
            return fields != null && fields.TryGetValue(name, out value) ? value : "";
        }

        private void SaveSimplePlanContext(string userMessage, SearchIntent intent, PendingAction pending, string message)
        {
            if (pending == null || !pending.IsValid)
            {
                return;
            }
            LastPlanContext context = new LastPlanContext();
            context.OriginalRequest = userMessage;
            context.IntentSummary = IntentSummary(intent);
            context.DiagnosticsSummary = "simple mode; model-selected action";
            context.VerificationSummary = message;
            context.SelectedTrackCount = pending.Tracks.Count;
            context.TotalDurationSeconds = pending.TotalDurationSeconds;
            context.TargetDurationSeconds = intent == null ? 0 : intent.TargetDurationSeconds;
            context.TrackSnapshot = BuildTrackSnapshot(pending.Tracks);
            conversationStore.SavePlaylistProposal(context);
        }

        private static string IntentSummary(SearchIntent intent)
        {
            if (intent == null)
            {
                return "";
            }
            return "task=" + intent.Task + ", query=" + intent.RetrievalQuery + ", rankingMode=" + intent.RankingMode + ", requestedTrackCount=" + intent.RequestedTrackCount + ", duration=" + intent.TargetDurationSeconds;
        }

        private static string AddConstraintFeedback(string message, SearchIntent intent, List<TrackInfo> candidates)
        {
            if (intent == null || !intent.DiverseArtists)
            {
                return message;
            }

            int artistCount = CountDistinctArtists(candidates);
            if (artistCount >= 3)
            {
                return message;
            }

            string note = "Local note: I found only " + artistCount + " matching artist(s) in the current local shortlist, so artist diversity may be limited.";
            return string.IsNullOrEmpty(message) ? note : message + "\r\n" + note;
        }

        private static string BuildValidationReport(PendingAction pending, AiAction action, SearchIntent intent)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("valid=false");
            builder.AppendLine("error=" + (pending == null ? "No pending action." : pending.ValidationError));
            builder.AppendLine("actionType=" + (action == null ? "" : action.Type));
            int targetSeconds = pending == null ? 0 : pending.TargetDurationSeconds;
            int actualSeconds = pending == null ? 0 : pending.TotalDurationSeconds;
            builder.AppendLine("targetSeconds=" + targetSeconds);
            builder.AppendLine("actualSeconds=" + actualSeconds);
            if (targetSeconds > 0)
            {
                builder.AppendLine("missingSeconds=" + Math.Max(0, targetSeconds - actualSeconds));
                builder.AppendLine("excessSeconds=" + Math.Max(0, actualSeconds - targetSeconds));
            }
            if (intent != null)
            {
                builder.AppendLine("originalConstraints=deduplicateTracks:" + intent.DeduplicateTracks +
                    "; diverseArtists:" + intent.DiverseArtists +
                    "; diverseAlbums:" + intent.DiverseAlbums +
                    "; requestedTrackCount:" + intent.RequestedTrackCount +
                    "; maxTracksPerArtist:" + intent.MaxTracksPerArtist +
                    "; maxTracksPerAlbum:" + intent.MaxTracksPerAlbum +
                    "; selectionMode:" + intent.SelectionMode +
                    "; retrievalQuery:" + intent.RetrievalQuery);
            }
            builder.AppendLine("Correction rule: preserve prior valid proposal tracks unless the user requested their removal; add more candidates through provided trackIds only.");
            return builder.ToString();
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

        private string ExecuteReadOnlyTools(List<ToolRequest> requests, CandidateSet candidateSet, TrackInfo nowPlaying)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < requests.Count; i++)
            {
                ToolRequest request = requests[i];
                int limit = Math.Max(1, Math.Min(80, request.Limit));
                builder.AppendLine("Tool: " + request.Name);
                if (request.Name == "get_now_playing")
                {
                    promptBuilder.AppendTrack(builder, nowPlaying);
                }
                else if (request.Name == "get_current_queue")
                {
                    AddToolTracks(builder, musicBee.GetQueueSummary(limit), candidateSet);
                }
                else if (request.Name == "find_similar_tracks_basic")
                {
                    SearchIntent intent = new SearchIntent();
                    intent.Similar = true;
                    intent.QueryText = request.Query;
                    intent.MaxTracks = limit;
                    AddToolTracks(builder, librarySearch.Search(intent, nowPlaying, settings), candidateSet);
                }
                else if (request.Name == "search_library")
                {
                    SearchIntent intent = intentParser.ParseLocal(request.Query);
                    intent.MaxTracks = limit;
                    AddToolTracks(builder, librarySearch.Search(intent, nowPlaying, settings), candidateSet);
                }
                else if (request.Name == "get_library_facet")
                {
                    string facet;
                    string query;
                    ParseFacetRequest(request.Query, out facet, out query);
                    AddLibraryFacet(builder, facet, query, limit, candidateSet, nowPlaying);
                }
                else if (request.Name == "get_artist_profile")
                {
                    AddArtistProfile(builder, request.Query, limit);
                }
                else if (request.Name == "lookup_listenbrainz_similar_artists")
                {
                    try
                    {
                        builder.AppendLine(externalLookup.LookupListenBrainzSimilarArtists(request.Query, limit));
                    }
                    catch (Exception ex)
                    {
                        builder.AppendLine("ListenBrainz lookup failed: " + ex.Message);
                    }
                }
                else if (request.Name == "lookup_wikipedia")
                {
                    try
                    {
                        builder.AppendLine(externalLookup.LookupWikipediaSummary(request.Query, limit));
                    }
                    catch (Exception ex)
                    {
                        builder.AppendLine("Wikipedia lookup failed: " + ex.Message);
                    }
                }
                else
                {
                    builder.AppendLine("Unsupported read-only tool.");
                }
            }
            return builder.ToString();
        }

        private void AddArtistProfile(StringBuilder builder, string artistName, int limit)
        {
            List<TrackInfo> tracks = librarySearch.SearchArtistTracks(artistName, Math.Max(40, limit), settings);
            builder.AppendLine("Artist profile from local library: " + (artistName ?? ""));
            if (tracks.Count == 0)
            {
                builder.AppendLine("- no local tracks found");
                return;
            }

            Dictionary<string, int> albums = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> genres = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> years = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int totalSeconds = 0;
            foreach (TrackInfo track in tracks)
            {
                Count(albums, track.Album);
                Count(genres, track.Genre);
                Count(years, track.Year);
                totalSeconds += track.DurationSeconds;
            }
            builder.AppendLine("- localTrackCount=" + tracks.Count);
            builder.AppendLine("- localDurationSeconds=" + totalSeconds);
            builder.AppendLine("- albums=" + TopCounts(albums, 12));
            builder.AppendLine("- genres=" + TopCounts(genres, 8));
            builder.AppendLine("- years=" + TopCounts(years, 12));
            builder.AppendLine("- sampleTracks:");
            int count = Math.Min(12, tracks.Count);
            for (int i = 0; i < count; i++)
            {
                promptBuilder.AppendTrack(builder, tracks[i]);
            }
        }

        private static void Count(Dictionary<string, int> counts, string value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            int count;
            counts.TryGetValue(value, out count);
            counts[value] = count + 1;
        }

        private static string TopCounts(Dictionary<string, int> counts, int limit)
        {
            List<KeyValuePair<string, int>> items = new List<KeyValuePair<string, int>>(counts);
            items.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int count = b.Value.CompareTo(a.Value);
                if (count != 0) return count;
                return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            int take = Math.Min(Math.Max(1, limit), items.Count);
            if (take == 0)
            {
                return "none";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < take; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(items[i].Key).Append("=").Append(items[i].Value);
            }
            return builder.ToString();
        }

        private void AddLibraryFacet(StringBuilder builder, string facet, string query, int limit, CandidateSet candidateSet, TrackInfo nowPlaying)
        {
            facet = (facet ?? "").Trim().ToLowerInvariant();
            builder.AppendLine("Facet: " + (string.IsNullOrEmpty(facet) ? "none" : facet));
            if (facet == "tracks")
            {
                SearchIntent intent = intentParser.ParseLocal(query);
                intent.MaxTracks = limit;
                AddToolTracks(builder, librarySearch.Search(intent, nowPlaying, settings), candidateSet);
                return;
            }

            List<LibraryFacetValue> values;
            if (facet == "custom_fields")
            {
                values = musicBee.GetCustomFields();
            }
            else if (facet == "genres" || facet == "artists" || facet == "years")
            {
                values = librarySearch.GetFacetValues(facet, query, limit);
            }
            else
            {
                builder.AppendLine("Unsupported facet. Supported facets: tracks, genres, artists, years, custom_fields.");
                return;
            }

            AppendFacetValues(builder, values, limit);
        }

        private static void AppendFacetValues(StringBuilder builder, List<LibraryFacetValue> values, int limit)
        {
            if (values == null || values.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            int count = Math.Min(values.Count, Math.Max(1, limit));
            for (int i = 0; i < count; i++)
            {
                LibraryFacetValue value = values[i];
                builder.Append("- value=").Append(value.Value);
                if (value.Count > 0)
                {
                    builder.Append("; count=").Append(value.Count);
                }
                builder.AppendLine();
            }
        }

        private static void ParseFacetRequest(string raw, out string facet, out string query)
        {
            raw = (raw ?? "").Trim();
            facet = raw;
            query = "";
            int separator = raw.IndexOf(':');
            if (separator >= 0)
            {
                facet = raw.Substring(0, separator).Trim();
                query = raw.Substring(separator + 1).Trim();
                return;
            }

            string[] supported = new string[] { "custom_fields", "tracks", "genres", "artists", "years" };
            for (int i = 0; i < supported.Length; i++)
            {
                if (raw.StartsWith(supported[i] + " ", StringComparison.OrdinalIgnoreCase))
                {
                    facet = supported[i];
                    query = raw.Substring(supported[i].Length).Trim();
                    return;
                }
            }
        }

        private void AddToolTracks(StringBuilder builder, List<TrackInfo> tracks, CandidateSet candidateSet)
        {
            int nextId = candidateSet.Count + 1;
            foreach (TrackInfo track in tracks)
            {
                track.Id = "track_" + nextId;
                nextId++;
            }

            AddCandidates(candidateSet, tracks);
            foreach (TrackInfo track in tracks)
            {
                promptBuilder.AppendTrack(builder, track);
            }
        }

        private static void AddCandidates(CandidateSet candidateSet, List<TrackInfo> tracks)
        {
            foreach (TrackInfo track in tracks)
            {
                candidateSet.Add(track);
            }
        }

        private AiChatResponse ParseAiResponseWithRepair(string raw)
        {
            try
            {
                return responseParser.Parse(raw);
            }
            catch (Exception ex)
            {
                Log("json-parse-error", ex.Message + " raw=" + raw);
                string repaired = TryRepairJson(raw);
                Log("json-repair", repaired);
                return responseParser.Parse(repaired);
            }
        }

        private string TryRepairJson(string raw)
        {
            string local = TryLocalRepair(raw);
            if (!string.IsNullOrEmpty(local))
            {
                return local;
            }

            string repairSystem =
                "You repair JSON for a MusicBee plugin. Return only valid compact JSON. " +
                "Allowed shape: {\"message\":\"text\",\"actions\":[]}. " +
                "If action data is incomplete or invalid, return actions: []. No markdown.";
            string repairUser = "Repair this invalid model output into valid JSON:\n" + (raw ?? "");
            return aiProvider.SendChat(repairSystem, repairUser);
        }

        private static string TryLocalRepair(string raw)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
            {
                return "{\"message\":\"The model returned an empty response.\",\"actions\":[]}";
            }

            if (text.IndexOf("\"message\"") >= 0 && text.IndexOf("\"actions\"") < 0 && text.IndexOf("\"trackIds\"") < 0)
            {
                string message = ExtractBrokenMessage(text);
                return "{\"message\":\"" + EscapeJsonString(message) + "\",\"actions\":[]}";
            }

            return "";
        }

        private static string ExtractBrokenMessage(string text)
        {
            int key = text.IndexOf("\"message\"");
            if (key < 0)
            {
                return "The model returned invalid JSON.";
            }

            int colon = text.IndexOf(':', key);
            int firstQuote = colon >= 0 ? text.IndexOf('"', colon + 1) : -1;
            int secondQuote = firstQuote >= 0 ? text.IndexOf('"', firstQuote + 1) : -1;
            if (firstQuote >= 0 && secondQuote > firstQuote)
            {
                string value = text.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return "I found information, but the model returned invalid JSON.";
        }

        private static string EscapeJsonString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private void Log(string kind, string value)
        {
            try
            {
                Directory.CreateDirectory(dataPath);
                string line = DateTime.Now.ToString("s") + " [" + kind + "] " + value + Environment.NewLine;
                File.AppendAllText(Path.Combine(dataPath, "agent.log"), line);
            }
            catch
            {
            }
        }

        private static string FormatTrace(List<string> trace)
        {
            if (trace == null || trace.Count == 0)
            {
                return "";
            }
            return string.Join(" | ", trace.ToArray());
        }

        private static void AddTrace(List<string> trace, Action<string> traceSink, string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }
            if (trace != null)
            {
                trace.Add(line);
            }
            if (traceSink != null)
            {
                traceSink(line);
            }
        }

        private static void AddPlanTrace(List<string> trace, Action<string> traceSink, SearchIntent intent)
        {
            if (intent == null || intent.OrchestrationPlan == null)
            {
                return;
            }
            for (int i = 0; i < intent.OrchestrationPlan.Count; i++)
            {
                AddTrace(trace, traceSink, "Model plan step " + (i + 1) + ": " + intent.OrchestrationPlan[i]);
            }
        }

        private static void RepairStateFromPreviousPlan(SearchIntent intent, LastPlanContext lastPlan, List<string> trace, Action<string> traceSink)
        {
            if (intent == null || intent.TargetDurationSeconds > 0 || lastPlan == null || !IsTurn(intent, "refine_previous") || lastPlan.TargetDurationSeconds <= 0)
            {
                return;
            }

            intent.TargetDurationSeconds = lastPlan.TargetDurationSeconds;
            AddTrace(trace, traceSink, "Restored target duration from previous proposal state: " + lastPlan.TargetDurationSeconds + " second(s).");
        }


        private static bool ValidateOrRepairIntent(SearchIntent intent, LastPlanContext lastPlan, List<string> trace, Action<string> traceSink)
        {
            if (intent == null)
            {
                return false;
            }

            string task = (intent.Task ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(task))
            {
                intent.Task = "search";
                return true;
            }

            string allowed = "|info|search|create_playlist|queue|play|edit_playlist|delete_playlist|delete_proposal|other|";
            if (allowed.IndexOf("|" + task + "|", StringComparison.OrdinalIgnoreCase) < 0)
            {
                AddTrace(trace, traceSink, "Rejected model task outside allowlist: " + intent.Task + ".");
                return false;
            }

            if (lastPlan == null && (IsTurn(intent, "follow_up") || IsTurn(intent, "refine_previous")) && IsActionableIntent(intent))
            {
                intent.TurnKind = "new_task";
            }
            return true;
        }


        private static bool IsActionableIntent(SearchIntent intent)
        {
            if (intent == null)
            {
                return false;
            }

            string task = (intent.Task ?? "").ToLowerInvariant();
            return task == "search" || task == "create_playlist" || task == "queue" || task == "play" ||
                task == "edit_playlist" || task == "delete_playlist" || task == "delete_proposal" ||
                !string.IsNullOrEmpty(intent.RetrievalQuery);
        }

        private static bool IsPureFollowUpQuestion(SearchIntent intent)
        {
            if (intent == null)
            {
                return true;
            }

            string task = (intent.Task ?? "").ToLowerInvariant();
            string operation = (intent.PlaylistOperation ?? "").ToLowerInvariant();
            return (task == "info" || task == "other" || string.IsNullOrEmpty(task)) &&
                operation != "update_tracks" &&
                operation != "append_tracks" &&
                operation != "replace_tracks" &&
                operation != "delete_playlist" &&
                operation != "delete_proposal";
        }

        private static bool IsPlaylistManagementIntent(SearchIntent intent)
        {
            if (intent == null)
            {
                return false;
            }

            string task = (intent.Task ?? "").ToLowerInvariant();
            string operation = (intent.PlaylistOperation ?? "").ToLowerInvariant();
            return task == "edit_playlist" || task == "delete_playlist" || task == "delete_proposal" ||
                operation == "delete_playlist" || operation == "update_tracks" || operation == "append_tracks" ||
                operation == "replace_tracks" || operation == "delete_proposal";
        }

        private void HandlePlaylistManagement(SearchIntent intent, string userMessage, AgentResult result, List<string> trace, Action<string> traceSink, CancellationToken cancellationToken, bool needsChatTitle)
        {
            if (intent == null)
            {
                result.Message = "No playlist operation was found.";
                return;
            }

            string task = (intent.Task ?? "").ToLowerInvariant();
            string operation = (intent.PlaylistOperation ?? "").ToLowerInvariant();
            if (task == "delete_proposal" || operation == "delete_proposal")
            {
                conversationStore.SaveLastPlan(null);
                result.Message = "The current agent proposal has been cleared.";
                AddTrace(trace, traceSink, "Cleared active playlist proposal state.");
                return;
            }

            List<PlaylistRecord> playlists = musicBee.GetPlaylists();
            AddTrace(trace, traceSink, "Loaded MusicBee playlists for model selection: " + playlists.Count + ".");
            string selectionMessage;
            string selectionChatTitle;
            AiAction playlistCommand = SelectPlaylistCommandWithModel(intent, userMessage, playlists, trace, traceSink, cancellationToken, needsChatTitle, out selectionMessage, out selectionChatTitle);
            ApplyChatTitleIfNeeded(result, selectionChatTitle, needsChatTitle);
            PlaylistRecord playlist = FindPlaylistByUrl(playlists, playlistCommand == null ? "" : playlistCommand.PlaylistUrl);
            if (playlist == null)
            {
                result.Message = string.IsNullOrWhiteSpace(selectionMessage) ? "Please specify the playlist name." : selectionMessage;
                AddTrace(trace, traceSink, "Model did not select a valid playlist command.");
                return;
            }

            AiAction action = new AiAction();
            action.RequiresConfirmation = true;
            action.PlaylistUrl = playlist.PlaylistUrl;
            action.PlaylistName = playlist.Name;
            action.Title = playlist.Name;

            if (task == "delete_playlist" || operation == "delete_playlist")
            {
                action.Type = "delete_ai_playlist";
                action.Explanation = "Delete playlist '" + playlist.Name + "'.";
                result.PendingAction = actionValidator.Validate(action, new CandidateSet());
                result.Message = "Confirm deletion of playlist: " + playlist.Name;
                AddTrace(trace, traceSink, "Prepared delete action for playlist '" + playlist.Name + "'.");
                return;
            }

            List<TrackInfo> currentTracks = musicBee.GetPlaylistTracks(playlist.PlaylistUrl, 300);
            CandidateSet set = new CandidateSet();
            AddCandidates(set, currentTracks);
            List<TrackInfo> additions = BuildPlaylistEditCandidates(intent, userMessage, playlist, currentTracks, trace, traceSink, cancellationToken);
            AddCandidates(set, additions);
            PlaylistEditConstraints constraints = BuildPlaylistEditConstraints(intent, playlistCommand);
            AddTrace(trace, traceSink, "Playlist edit constraints: actionType=" + constraints.ActionType +
                ", requestedTrackCount=" + constraints.RequestedTrackCount +
                ", targetSeconds=" + constraints.TargetDurationSeconds +
                ", toleranceSeconds=" + constraints.DurationToleranceSeconds +
                ", selectionMode=" + constraints.SelectionMode + ".");

            if (ShouldUseOrchestratorSelection(constraints, additions))
            {
                List<TrackInfo> selected = SelectConstrainedTracks(additions, constraints, trace, traceSink);
                if (selected == null || selected.Count == 0)
                {
                    result.Message = "I found candidate tracks, but could not satisfy the requested count and duration together. Please loosen one of the limits.";
                    AddTrace(trace, traceSink, "Orchestrator constrained selection failed before preview.");
                    return;
                }

                AiAction selectedAction = BuildPlaylistEditAction(playlist, constraints.ActionType, selected, constraints);
                result.PendingAction = actionValidator.Validate(selectedAction, set, intent);
                if (!result.PendingAction.IsValid)
                {
                    result.Message = "I found candidate tracks, but the selected set did not pass validation: " + result.PendingAction.ValidationError;
                    AddTrace(trace, traceSink, "Orchestrator constrained action failed validation: " + result.PendingAction.ValidationError);
                    result.PendingAction = null;
                    return;
                }

                result.Message = constraints.ActionType == "append_to_playlist"
                    ? "I prepared tracks to append to playlist '" + playlist.Name + "'."
                    : "I prepared a replacement for playlist '" + playlist.Name + "'.";
                AddTrace(trace, traceSink, "Prepared " + constraints.ActionType + " action through orchestrator selection with " + selected.Count + " track(s), duration=" + SumDuration(selected) + " second(s).");
                return;
            }

            string system = "You edit an existing MusicBee playlist. Return only valid JSON. " +
                "Allowed actions: append_to_playlist or replace_ai_playlist. Use only provided trackIds. Keep write action requiresConfirmation=true. " +
                "For append_to_playlist, include only tracks to add. For replace_ai_playlist, include the full final playlist track list. " +
                "Never return all search candidates unless the user explicitly asked for all of them. " +
                "Obey requestedTrackCount, targetSeconds, minSeconds, and maxSeconds exactly when present. " +
                "Shape: {\"message\":\"text\",\"chatTitle\":\"optional first chat title\",\"actions\":[{\"type\":\"append_to_playlist|replace_ai_playlist\",\"requiresConfirmation\":true,\"title\":\"text\",\"trackIds\":[\"track_1\"],\"explanation\":\"text\"}]}";
            string prompt = BuildPlaylistEditPrompt(userMessage, playlist, currentTracks, additions, constraints);
            if (needsChatTitle)
            {
                prompt = "This is the first assistant response in this chat. Include chatTitle in the final JSON response: a concise chat title based on the user's request, in the user's language when practical.\r\n\r\n" + prompt;
            }
            string raw = aiProvider.SendChat(system, prompt, cancellationToken);
            AiChatResponse response = ParseAiResponseWithRepair(raw);
            ApplyChatTitleIfNeeded(result, response, needsChatTitle);
            if (response.Actions.Count == 0)
            {
                result.Message = string.IsNullOrEmpty(response.Message) ? "The model did not return an editable playlist action." : response.Message;
                AddTrace(trace, traceSink, "Model did not return a playlist edit action.");
                return;
            }

            if (response.Actions[0].Type != "append_to_playlist" && response.Actions[0].Type != "replace_ai_playlist")
            {
                result.Message = "The model returned an unsupported playlist edit action.";
                AddTrace(trace, traceSink, "Rejected unsupported playlist edit action type: " + response.Actions[0].Type + ".");
                return;
            }
            response.Actions[0].PlaylistUrl = playlist.PlaylistUrl;
            response.Actions[0].PlaylistName = playlist.Name;
            result.PendingAction = actionValidator.Validate(response.Actions[0], set, intent);
            if (!result.PendingAction.IsValid && HasMachineSelectionConstraints(constraints))
            {
                AddTrace(trace, traceSink, "Model playlist edit action failed constraints; trying orchestrator constrained repair.");
                List<TrackInfo> selected = SelectConstrainedTracks(additions, constraints, trace, traceSink);
                if (selected != null && selected.Count > 0)
                {
                    AiAction repairedAction = BuildPlaylistEditAction(playlist, constraints.ActionType, selected, constraints);
                    PendingAction repaired = actionValidator.Validate(repairedAction, set, intent);
                    if (repaired.IsValid)
                    {
                        result.PendingAction = repaired;
                        response.Actions[0] = repairedAction;
                        result.Message = "I corrected the selection to match the requested count and duration.";
                        AddTrace(trace, traceSink, "Orchestrator constrained repair succeeded with " + selected.Count + " track(s), duration=" + SumDuration(selected) + " second(s).");
                        return;
                    }
                }
            }
            result.Message = string.IsNullOrEmpty(response.Message) ? "Prepared an updated AI playlist proposal." : response.Message;
            AddTrace(trace, traceSink, "Prepared " + response.Actions[0].Type + " action for playlist '" + playlist.Name + "' with valid=" + result.PendingAction.IsValid + (result.PendingAction.IsValid ? "" : ", error=" + result.PendingAction.ValidationError) + ".");
        }

        private List<TrackInfo> BuildPlaylistEditCandidates(SearchIntent intent, string userMessage, PlaylistRecord playlist, List<TrackInfo> currentTracks, List<string> trace, Action<string> traceSink, CancellationToken cancellationToken)
        {
            List<TrackInfo> result = new List<TrackInfo>();
            try
            {
                string system =
                    "You plan read-only MusicBee searches for editing a playlist. Return only valid JSON. " +
                    "Allowed toolRequests: search_artist_tracks or search_library. " +
                    "For explicit artist requests, use search_artist_tracks with the exact artist name and requested limit. " +
                    "Shape: {\"message\":\"text\",\"toolRequests\":[{\"name\":\"search_artist_tracks|search_library\",\"query\":\"text\",\"limit\":20}],\"actions\":[]}.";
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("User request:");
                builder.AppendLine(userMessage ?? "");
                builder.AppendLine();
                builder.AppendLine("Parsed intent: task=" + intent.Task + "; query=" + intent.RetrievalQuery + "; goal=" + intent.UserGoal);
                builder.AppendLine("Selected playlist: " + (playlist == null ? "" : playlist.Name));
                builder.AppendLine();
                builder.AppendLine("Current playlist sample:");
                AppendPromptTracks(builder, currentTracks, 80);

                AddTrace(trace, traceSink, "Sending playlist edit search-planning request to model.");
                string raw = aiProvider.SendChat(system, builder.ToString(), cancellationToken);
                AiChatResponse response = ParseAiResponseWithRepair(raw);
                AddTrace(trace, traceSink, "Playlist edit model requested " + response.ToolRequests.Count + " read-only search tool(s).");
                result.AddRange(ExecutePlaylistEditToolRequests(response.ToolRequests, trace, traceSink));
            }
            catch (Exception ex)
            {
                AddTrace(trace, traceSink, "Playlist edit search-planning failed: " + ex.Message);
            }

            if (result.Count == 0)
            {
                SearchIntent editIntent = intent.Clone();
                editIntent.MaxTracks = Math.Max(80, currentTracks == null ? 80 : currentTracks.Count);
                result = librarySearch.Search(editIntent, musicBee.GetNowPlaying(), settings);
                AddTrace(trace, traceSink, "Fallback playlist edit search returned " + result.Count + " candidate track(s).");
            }

            ReassignIds(result, "edit_track_");
            return result;
        }

        private List<TrackInfo> ExecutePlaylistEditToolRequests(List<ToolRequest> requests, List<string> trace, Action<string> traceSink)
        {
            List<TrackInfo> result = new List<TrackInfo>();
            Dictionary<string, bool> files = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (ToolRequest request in requests ?? new List<ToolRequest>())
            {
                int limit = Math.Max(1, Math.Min(80, request.Limit <= 0 ? 20 : request.Limit));
                int searchLimit = Math.Min(80, Math.Max(limit, limit * 3));
                List<TrackInfo> tracks;
                if (request.Name == "search_artist_tracks")
                {
                    tracks = librarySearch.SearchArtistTracks(request.Query, searchLimit, settings);
                    AddTrace(trace, traceSink, "search_artist_tracks('" + request.Query + "', requested=" + limit + ", candidates=" + searchLimit + ") returned " + tracks.Count + " track(s).");
                }
                else if (request.Name == "search_library")
                {
                    SearchIntent searchIntent = intentParser.ParseLocal(request.Query);
                    searchIntent.MaxTracks = searchLimit;
                    tracks = librarySearch.Search(searchIntent, musicBee.GetNowPlaying(), settings);
                    AddTrace(trace, traceSink, "search_library('" + request.Query + "', requested=" + limit + ", candidates=" + searchLimit + ") returned " + tracks.Count + " track(s).");
                }
                else
                {
                    AddTrace(trace, traceSink, "Unsupported playlist edit tool request: " + request.Name + ".");
                    continue;
                }

                foreach (TrackInfo track in tracks)
                {
                    string key = string.IsNullOrEmpty(track.FileUrl) ? track.DisplayName : track.FileUrl;
                    if (!files.ContainsKey(key))
                    {
                        files[key] = true;
                        result.Add(track);
                    }
                }
            }
            return result;
        }

        private static void ReassignIds(List<TrackInfo> tracks, string prefix)
        {
            for (int i = 0; i < (tracks == null ? 0 : tracks.Count); i++)
            {
                tracks[i].Id = prefix + (i + 1);
            }
        }

        private AiAction SelectPlaylistCommandWithModel(SearchIntent intent, string userMessage, List<PlaylistRecord> playlists, List<string> trace, Action<string> traceSink, CancellationToken cancellationToken, bool needsChatTitle, out string message, out string chatTitle)
        {
            message = "";
            chatTitle = "";
            if (playlists == null || playlists.Count == 0)
            {
                return null;
            }

            string system =
                "You choose one existing MusicBee playlist for a requested playlist-management operation. Return only valid JSON. " +
                "Use exactly one playlistUrl copied from the provided list. Do not invent URLs. " +
                "The user may provide an incomplete playlist name; choose the best existing playlist by name and conversation context when there is one clear match. " +
                "If there are multiple plausible matches or no plausible match, return actions: [] and ask a concise clarification in the user's language, listing likely playlist names. " +
                "Allowed action types: delete_ai_playlist, append_to_playlist, or replace_ai_playlist. " +
                "Use append_to_playlist when the user asks to add tracks to the end of a playlist. Use replace_ai_playlist only when the user explicitly asks to replace/remove/rebuild playlist content. " +
                "Shape: {\"message\":\"text\",\"chatTitle\":\"optional first chat title\",\"actions\":[{\"type\":\"delete_ai_playlist|append_to_playlist|replace_ai_playlist\",\"requiresConfirmation\":true,\"playlistUrl\":\"exact provided url\",\"playlistName\":\"name\",\"title\":\"name\",\"trackIds\":[],\"explanation\":\"why this playlist\"}]}";
            StringBuilder builder = new StringBuilder();
            if (needsChatTitle)
            {
                builder.AppendLine("This is the first assistant response in this chat. Include chatTitle in the final JSON response: a concise chat title based on the user's request, in the user's language when practical.");
                builder.AppendLine();
            }
            builder.AppendLine("User request:");
            builder.AppendLine(userMessage ?? "");
            builder.AppendLine();
            builder.AppendLine("Parsed intent:");
            builder.AppendLine("task=" + intent.Task + "; operation=" + intent.PlaylistOperation + "; targetPlaylistName=" + intent.TargetPlaylistName + "; goal=" + intent.UserGoal);
            builder.AppendLine();
            builder.AppendLine("Full conversation:");
            List<ConversationMessage> history = conversationStore.GetMessages(conversationStore.ActiveConversationId);
            for (int i = 0; i < history.Count; i++)
            {
                builder.Append("- ").Append(history[i].Role).Append(": ").AppendLine((history[i].Text ?? "").Replace("\r", " ").Replace("\n", " "));
            }
            builder.AppendLine();
            builder.AppendLine("Available playlists:");
            for (int i = 0; i < playlists.Count; i++)
            {
                PlaylistRecord playlist = playlists[i];
                builder.Append("- playlistUrl=").Append(playlist.PlaylistUrl);
                builder.Append("; name=").Append(playlist.Name);
                builder.AppendLine();
            }

            AddTrace(trace, traceSink, "Sending playlist list to model for final command selection.");
            string raw = aiProvider.SendChat(system, builder.ToString(), cancellationToken);
            AiChatResponse response = ParseAiResponseWithRepair(raw);
            message = response.Message;
            chatTitle = response.ChatTitle;
            if (response.Actions.Count == 0)
            {
                AddTrace(trace, traceSink, "Playlist command model returned no action.");
                return null;
            }

            AiAction action = response.Actions[0];
            PlaylistRecord selected = FindPlaylistByUrl(playlists, action.PlaylistUrl);
            if (selected == null)
            {
                AddTrace(trace, traceSink, "Playlist command model returned an unknown playlistUrl.");
                return null;
            }

            action.PlaylistName = selected.Name;
            action.Title = selected.Name;
            return action;
        }

        private static PlaylistRecord FindPlaylistByUrl(List<PlaylistRecord> playlists, string playlistUrl)
        {
            foreach (PlaylistRecord playlist in playlists ?? new List<PlaylistRecord>())
            {
                if (playlist != null && string.Equals(playlist.PlaylistUrl, playlistUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return playlist;
                }
            }
            return null;
        }

        private PlaylistRecord FindAiPlaylist(string requestedName)
        {
            List<PlaylistRecord> playlists = musicBee.GetPlaylists();
            PlaylistRecord fallback = null;
            string requested = NormalizationService.NormalizeKey(requestedName);
            foreach (PlaylistRecord playlist in playlists)
            {
                if (playlist == null || string.IsNullOrEmpty(playlist.PlaylistUrl) || !playlistRegistry.IsAiOwned(playlist.PlaylistUrl))
                {
                    continue;
                }
                if (fallback == null)
                {
                    fallback = playlist;
                }
                string name = NormalizationService.NormalizeKey(playlist.Name);
                if (string.IsNullOrEmpty(requested) || name.IndexOf(requested) >= 0 || requested.IndexOf(name) >= 0)
                {
                    return playlist;
                }
            }
            return string.IsNullOrEmpty(requested) ? fallback : null;
        }

        private PlaylistEditConstraints BuildPlaylistEditConstraints(SearchIntent intent, AiAction playlistCommand)
        {
            PlaylistEditConstraints constraints = new PlaylistEditConstraints();
            constraints.ActionType = DeterminePlaylistEditActionType(intent, playlistCommand);
            constraints.RequestedTrackCount = intent == null ? 0 : intent.RequestedTrackCount;
            constraints.TargetDurationSeconds = intent == null ? 0 : intent.TargetDurationSeconds;
            constraints.DurationToleranceSeconds = DurationToleranceSeconds(constraints.TargetDurationSeconds);
            constraints.SelectionMode = intent == null ? "" : intent.SelectionMode;
            constraints.RandomSelection = string.Equals(NormalizationService.NormalizeKey(constraints.SelectionMode), "random", StringComparison.OrdinalIgnoreCase);
            return constraints;
        }

        private static string DeterminePlaylistEditActionType(SearchIntent intent, AiAction playlistCommand)
        {
            if (playlistCommand != null && (playlistCommand.Type == "append_to_playlist" || playlistCommand.Type == "replace_ai_playlist"))
            {
                return playlistCommand.Type;
            }

            string operation = intent == null ? "" : (intent.PlaylistOperation ?? "").ToLowerInvariant();
            if (operation == "append_tracks" || operation == "add_tracks" || operation == "update_tracks")
            {
                return "append_to_playlist";
            }
            if (operation == "replace_tracks" || operation == "replace_playlist" || operation == "rebuild_playlist")
            {
                return "replace_ai_playlist";
            }
            return "replace_ai_playlist";
        }

        private static bool ShouldUseOrchestratorSelection(PlaylistEditConstraints constraints, List<TrackInfo> additions)
        {
            return additions != null && additions.Count > 0 &&
                (constraints.RandomSelection || HasMachineSelectionConstraints(constraints));
        }

        private static bool HasMachineSelectionConstraints(PlaylistEditConstraints constraints)
        {
            return constraints != null && (constraints.RequestedTrackCount > 0 || constraints.TargetDurationSeconds > 0);
        }

        private List<TrackInfo> SelectConstrainedTracks(List<TrackInfo> candidates, PlaylistEditConstraints constraints, List<string> trace, Action<string> traceSink)
        {
            List<TrackInfo> pool = DeduplicateTracks(candidates);
            int requestedCount = constraints.RequestedTrackCount;
            if (requestedCount <= 0 && constraints.TargetDurationSeconds <= 0)
            {
                return new List<TrackInfo>();
            }
            if (requestedCount > 0 && pool.Count < requestedCount)
            {
                AddTrace(trace, traceSink, "Not enough candidate tracks for requested count: requested=" + requestedCount + ", candidates=" + pool.Count + ".");
                return null;
            }

            List<TrackInfo> best = null;
            int bestDistance = int.MaxValue;
            int attempts = constraints.RandomSelection ? 3000 : 1200;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                List<TrackInfo> shuffled = Shuffle(pool);
                List<TrackInfo> selected = requestedCount > 0
                    ? TakeCount(shuffled, requestedCount)
                    : TakeUntilDuration(shuffled, constraints.TargetDurationSeconds + constraints.DurationToleranceSeconds);
                if (selected.Count == 0)
                {
                    continue;
                }

                int total = SumDuration(selected);
                int distance = constraints.TargetDurationSeconds > 0 ? Math.Abs(total - constraints.TargetDurationSeconds) : 0;
                if (constraints.TargetDurationSeconds <= 0 || distance <= constraints.DurationToleranceSeconds)
                {
                    AddTrace(trace, traceSink, "Constrained selector found valid set on attempt " + (attempt + 1) + ": count=" + selected.Count + ", duration=" + total + ".");
                    return selected;
                }
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = selected;
                }
            }

            if (best != null)
            {
                AddTrace(trace, traceSink, "Constrained selector best miss: count=" + best.Count + ", duration=" + SumDuration(best) + ", distance=" + bestDistance + ".");
            }
            return null;
        }

        private static List<TrackInfo> DeduplicateTracks(List<TrackInfo> tracks)
        {
            List<TrackInfo> result = new List<TrackInfo>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (TrackInfo track in tracks ?? new List<TrackInfo>())
            {
                string fileKey = string.IsNullOrEmpty(track.FileUrl) ? "" : "file:" + track.FileUrl;
                string canonicalKey = NormalizationService.CanonicalTrackKey(track);
                if (string.IsNullOrEmpty(fileKey) && string.IsNullOrEmpty(canonicalKey))
                {
                    continue;
                }
                if ((!string.IsNullOrEmpty(fileKey) && seen.ContainsKey(fileKey)) ||
                    (!string.IsNullOrEmpty(canonicalKey) && seen.ContainsKey("canonical:" + canonicalKey)))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(fileKey))
                {
                    seen[fileKey] = true;
                }
                if (!string.IsNullOrEmpty(canonicalKey))
                {
                    seen["canonical:" + canonicalKey] = true;
                }
                result.Add(track);
            }
            return result;
        }

        private List<TrackInfo> Shuffle(List<TrackInfo> tracks)
        {
            List<TrackInfo> copy = new List<TrackInfo>(tracks ?? new List<TrackInfo>());
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                TrackInfo temp = copy[i];
                copy[i] = copy[j];
                copy[j] = temp;
            }
            return copy;
        }

        private static List<TrackInfo> TakeCount(List<TrackInfo> tracks, int count)
        {
            List<TrackInfo> result = new List<TrackInfo>();
            for (int i = 0; i < tracks.Count && result.Count < count; i++)
            {
                result.Add(tracks[i]);
            }
            return result;
        }

        private static List<TrackInfo> TakeUntilDuration(List<TrackInfo> tracks, int maxSeconds)
        {
            List<TrackInfo> result = new List<TrackInfo>();
            int total = 0;
            foreach (TrackInfo track in tracks ?? new List<TrackInfo>())
            {
                int duration = track.DurationSeconds;
                if (duration <= 0)
                {
                    continue;
                }
                if (total + duration > maxSeconds && result.Count > 0)
                {
                    continue;
                }
                result.Add(track);
                total += duration;
                if (total >= maxSeconds)
                {
                    break;
                }
            }
            return result;
        }

        private static AiAction BuildPlaylistEditAction(PlaylistRecord playlist, string actionType, List<TrackInfo> selected, PlaylistEditConstraints constraints)
        {
            AiAction action = new AiAction();
            action.Type = string.IsNullOrEmpty(actionType) ? "append_to_playlist" : actionType;
            action.RequiresConfirmation = true;
            action.PlaylistUrl = playlist.PlaylistUrl;
            action.PlaylistName = playlist.Name;
            action.Title = playlist.Name;
            action.TargetDurationSeconds = constraints.TargetDurationSeconds;
            action.Explanation = "Selected by orchestrator constraints: count=" + selected.Count + ", durationSeconds=" + SumDuration(selected) + ".";
            foreach (TrackInfo track in selected)
            {
                action.TrackIds.Add(track.Id);
            }
            return action;
        }

        private static int SumDuration(List<TrackInfo> tracks)
        {
            int total = 0;
            foreach (TrackInfo track in tracks ?? new List<TrackInfo>())
            {
                total += track.DurationSeconds;
            }
            return total;
        }

        private static int DurationToleranceSeconds(int targetSeconds)
        {
            if (targetSeconds <= 0)
            {
                return 0;
            }
            if (targetSeconds <= 45 * 60)
            {
                return 90;
            }
            if (targetSeconds <= 2 * 3600)
            {
                return 180;
            }
            return 300;
        }

        private string BuildPlaylistEditPrompt(string userMessage, PlaylistRecord playlist, List<TrackInfo> currentTracks, List<TrackInfo> additions, PlaylistEditConstraints constraints)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("User request:");
            builder.AppendLine(userMessage ?? "");
            builder.AppendLine();
            builder.AppendLine("Target playlist: " + (playlist == null ? "" : playlist.Name));
            builder.AppendLine("Constraint ledger:");
            builder.AppendLine("actionType: " + constraints.ActionType);
            builder.AppendLine("requestedTrackCount: " + constraints.RequestedTrackCount);
            builder.AppendLine("targetSeconds: " + constraints.TargetDurationSeconds);
            builder.AppendLine("minSeconds: " + Math.Max(0, constraints.TargetDurationSeconds - constraints.DurationToleranceSeconds));
            builder.AppendLine("maxSeconds: " + (constraints.TargetDurationSeconds <= 0 ? 0 : constraints.TargetDurationSeconds + constraints.DurationToleranceSeconds));
            builder.AppendLine("selectionMode: " + constraints.SelectionMode);
            builder.AppendLine();
            builder.AppendLine("Current playlist tracks:");
            AppendPromptTracks(builder, currentTracks, 160);
            builder.AppendLine();
            builder.AppendLine("Additional library candidates:");
            AppendPromptTracks(builder, additions, 220);
            return builder.ToString();
        }

        private class PlaylistEditConstraints
        {
            public string ActionType;
            public int RequestedTrackCount;
            public int TargetDurationSeconds;
            public int DurationToleranceSeconds;
            public string SelectionMode;
            public bool RandomSelection;
        }

        private void AppendPromptTracks(StringBuilder builder, List<TrackInfo> tracks, int limit)
        {
            if (tracks == null || tracks.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            int count = Math.Min(limit, tracks.Count);
            for (int i = 0; i < count; i++)
            {
                TrackInfo track = tracks[i];
                builder.Append("- id=").Append(track.Id);
                builder.Append("; artist=").Append(track.Artist);
                builder.Append("; title=").Append(track.Title);
                builder.Append("; album=").Append(track.Album);
                builder.Append("; genre=").Append(track.Genre);
                builder.Append("; duration=").Append(track.Duration);
                builder.AppendLine();
            }
        }

        private static bool IsTurn(SearchIntent intent, string value)
        {
            return string.Equals(intent == null ? "" : intent.TurnKind, value, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTrackSnapshot(List<TrackInfo> tracks)
        {
            if (tracks == null || tracks.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            int count = Math.Min(80, tracks.Count);
            for (int i = 0; i < count; i++)
            {
                TrackInfo track = tracks[i];
                builder.Append("- id=").Append(track.Id);
                builder.Append("; artist=").Append(track.Artist);
                builder.Append("; title=").Append(track.Title);
                builder.Append("; album=").Append(track.Album);
                builder.Append("; genre=").Append(track.Genre);
                builder.Append("; durationSeconds=").Append(track.DurationSeconds);
                builder.AppendLine();
            }
            return builder.ToString();
        }
    }
}
