using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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
        private readonly string dataPath;

        public AgentController(MusicBeeApiAdapter musicBee, IAiProvider aiProvider, PluginSettings settings, string dataPath)
        {
            this.musicBee = musicBee;
            this.librarySearch = new LibrarySearchService(musicBee, dataPath);
            this.aiProvider = aiProvider;
            this.settings = settings;
            this.playlistRegistry = new AiOwnedPlaylistRegistry(dataPath);
            this.intentParser = new IntentParser(settings);
            this.promptBuilder = new PromptBuilder(settings);
            this.dataPath = dataPath;
        }

        public AgentResult Send(string userMessage)
        {
            AgentResult result = new AgentResult();
            try
            {
                TrackInfo nowPlaying = musicBee.GetNowPlaying();
                SearchIntent intent = intentParser.Parse(userMessage);
                List<TrackInfo> candidates = BuildCandidates(intent, nowPlaying);
                CandidateSet candidateSet = new CandidateSet();
                AddCandidates(candidateSet, candidates);

                string systemPrompt = promptBuilder.BuildSystemPrompt();
                LibraryProfile profile = librarySearch.GetProfile();
                string raw = aiProvider.SendChat(systemPrompt, promptBuilder.BuildUserPrompt(userMessage, nowPlaying, candidates, "", profile));
                AiChatResponse response = ParseAiResponseWithRepair(raw);

                if (!settings.SmallLocalModelMode && response.ToolRequests.Count > 0)
                {
                    string toolResults = ExecuteReadOnlyTools(response.ToolRequests, candidateSet, nowPlaying);
                    raw = aiProvider.SendChat(systemPrompt, promptBuilder.BuildUserPrompt(userMessage, nowPlaying, new List<TrackInfo>(candidateSet.Tracks), toolResults, profile));
                    response = ParseAiResponseWithRepair(raw);
                }

                result.Message = response.Message;
                if (response.Actions.Count > 0)
                {
                    result.PendingAction = actionValidator.Validate(response.Actions[0], candidateSet);
                }

                Log("request", userMessage);
                Log("response", raw);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Log("error", ex.ToString());
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

            if (pending.Tracks == null || pending.Tracks.Count == 0)
            {
                return "No tracks selected.";
            }

            string actionType = string.IsNullOrEmpty(overrideType) ? pending.Action.Type : overrideType;
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

        private SearchIntent BuildIntent(string userMessage)
        {
            string text = (userMessage ?? "").ToLowerInvariant();
            SearchIntent intent = new SearchIntent();
            intent.QueryText = userMessage;
            intent.MaxTracks = settings.SmallLocalModelMode ? 12 : 40;
            intent.Similar = ContainsAny(text, new string[] { "similar", "like current", "\\u043f\\u043e\\u0445\\u043e\\u0436", "\\u043f\\u043e\\u0434\\u043e\\u0431\\u043d" });
            intent.Calmer = ContainsAny(text, new string[] { "calm", "calmer", "quiet", "focus", "\\u0441\\u043f\\u043e\\u043a\\u043e\\u0439", "\\u0444\\u043e\\u043a\\u0443\\u0441" });
            intent.Energetic = ContainsAny(text, new string[] { "energy", "energetic", "faster", "dance", "\\u0431\\u043e\\u0434\\u0440", "\\u044d\\u043d\\u0435\\u0440\\u0433" });
            intent.ExcludeCurrentArtist = ContainsAny(text, new string[] { "different artist", "not same artist", "\\u0434\\u0440\\u0443\\u0433\\u043e\\u0439 \\u0438\\u0441\\u043f\\u043e\\u043b\\u043d\\u0438\\u0442\\u0435\\u043b" });
            intent.TargetDurationSeconds = ParseTargetDurationSeconds(text);
            if (intent.TargetDurationSeconds > 0)
            {
                intent.MaxTracks = settings.SmallLocalModelMode ? 20 : 120;
            }
            return intent;
        }

        private static bool ContainsAny(string text, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.IndexOf(DecodeEscapes(needles[i])) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string DecodeEscapes(string value)
        {
            return value.IndexOf("\\u") < 0 ? value : Regex.Unescape(value);
        }

        private static int ParseTargetDurationSeconds(string text)
        {
            Match match = Regex.Match(text, "(\\d+)\\s*(hour|hours|hr|h|minute|minutes|min|m)");
            if (match.Success)
            {
                int value = int.Parse(match.Groups[1].Value);
                string unit = match.Groups[2].Value;
                return unit.StartsWith("h") ? value * 3600 : value * 60;
            }

            match = Regex.Match(text, "(\\d+)\\s*(\\u0447\\u0430\\u0441|\\u043c\\u0438\\u043d)");
            if (match.Success)
            {
                int value = int.Parse(match.Groups[1].Value);
                string unit = match.Groups[2].Value;
                return unit.IndexOf(Regex.Unescape("\\u0447")) >= 0 ? value * 3600 : value * 60;
            }

            return 0;
        }

        private string BuildSystemPrompt()
        {
            if (settings.SmallLocalModelMode)
            {
                return
                    "You are a MusicBee assistant. Return only compact valid JSON. " +
                    "Use this exact shape: {\"message\":\"text\",\"actions\":[]}. " +
                    "For a write action use: {\"message\":\"text\",\"actions\":[{\"type\":\"create_playlist|queue_tracks_last|queue_tracks_next|play_track_now\",\"requiresConfirmation\":true,\"title\":\"text\",\"trackIds\":[\"track_1\"],\"explanation\":\"text\"}]}. " +
                    "Use only provided trackIds. Never invent file paths. No markdown. No extra keys.";
            }

            return
                "You are an AI assistant inside MusicBee. " +
                "Return only valid JSON, with no markdown. " +
                "Action schema: {\"message\":\"text\",\"actions\":[{\"type\":\"create_playlist|queue_tracks_last|queue_tracks_next|play_track_now\",\"requiresConfirmation\":true,\"title\":\"text\",\"trackIds\":[\"track_1\"],\"explanation\":\"text\"}]}. " +
                "If more local data is needed, return {\"message\":\"text\",\"toolRequests\":[{\"name\":\"get_now_playing|search_library|find_similar_tracks_basic|get_current_queue\",\"query\":\"text\",\"limit\":40}],\"actions\":[]}. " +
                "Never invent file paths. Never use track IDs that are not provided. " +
                "Dangerous actions are forbidden. All write actions require confirmation.";
        }

        private string BuildUserPrompt(string userMessage, TrackInfo nowPlaying, List<TrackInfo> candidates, string toolResults, LibraryProfile profile)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("User request:");
            builder.AppendLine(userMessage ?? "");
            builder.AppendLine();
            builder.AppendLine("Privacy mode: " + settings.PrivacyMode);
            if (profile != null && profile.TrackCount > 0 && !settings.SmallLocalModelMode)
            {
                builder.AppendLine(profile.ToPromptSummary());
            }
            if (settings.SmallLocalModelMode)
            {
                builder.AppendLine("Small local model mode: use the simplest valid JSON. Do not request tools.");
            }
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

        private void AppendTrack(StringBuilder builder, TrackInfo track)
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
            if (!settings.SmallLocalModelMode)
            {
                builder.Append("; albumArtist=").Append(track.AlbumArtist);
                builder.Append("; year=").Append(track.Year);
                builder.Append("; bpm=").Append(track.Bpm);
                builder.Append("; mood=").Append(track.Mood);
                builder.Append("; rating=").Append(track.Rating);
                builder.Append("; playCount=").Append(track.PlayCount);
                builder.Append("; skipCount=").Append(track.SkipCount);
                builder.Append("; reason=").Append(track.ScoreReason);
            }
            if (settings.PrivacyMode == PrivacyMode.FullOnline)
            {
                builder.Append("; fileUrl=").Append(track.FileUrl);
            }
            builder.AppendLine();
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
                    SearchIntent intent = intentParser.Parse(request.Query);
                    intent.MaxTracks = limit;
                    AddToolTracks(builder, librarySearch.Search(intent, nowPlaying, settings), candidateSet);
                }
                else
                {
                    builder.AppendLine("Unsupported read-only tool.");
                }
            }
            return builder.ToString();
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

        private AiChatResponse ParseAiResponse(string raw)
        {
            IDictionary<string, object> root = SimpleJson.Parse(ExtractJson(raw)) as IDictionary<string, object>;
            if (root == null)
            {
                throw new FormatException("Model response is not a JSON object.");
            }

            AiChatResponse response = new AiChatResponse();
            response.Message = SimpleJson.GetString(root, "message");

            object actionsValue;
            IList actions = root.TryGetValue("actions", out actionsValue) ? actionsValue as IList : null;
            if (actions != null)
            {
                foreach (object item in actions)
                {
                    IDictionary<string, object> actionObject = item as IDictionary<string, object>;
                    if (actionObject == null)
                    {
                        continue;
                    }

                    AiAction action = new AiAction();
                    action.Type = SimpleJson.GetString(actionObject, "type");
                    action.RequiresConfirmation = SimpleJson.GetBool(actionObject, "requiresConfirmation", true);
                    action.Title = SimpleJson.GetString(actionObject, "title");
                    action.Explanation = SimpleJson.GetString(actionObject, "explanation");

                    object idsValue;
                    IList ids = actionObject.TryGetValue("trackIds", out idsValue) ? idsValue as IList : null;
                    if (ids != null)
                    {
                        foreach (object id in ids)
                        {
                            action.TrackIds.Add(Convert.ToString(id));
                        }
                    }

                    response.Actions.Add(action);
                }
            }

            object toolRequestsValue;
            IList toolRequests = root.TryGetValue("toolRequests", out toolRequestsValue) ? toolRequestsValue as IList : null;
            if (toolRequests != null)
            {
                foreach (object item in toolRequests)
                {
                    IDictionary<string, object> toolObject = item as IDictionary<string, object>;
                    if (toolObject == null)
                    {
                        continue;
                    }

                    ToolRequest request = new ToolRequest();
                    request.Name = SimpleJson.GetString(toolObject, "name");
                    request.Query = SimpleJson.GetString(toolObject, "query");
                    int limit;
                    if (int.TryParse(SimpleJson.GetString(toolObject, "limit"), out limit))
                    {
                        request.Limit = limit;
                    }
                    response.ToolRequests.Add(request);
                }
            }

            return response;
        }

        private PendingAction Validate(AiAction action, CandidateSet candidateSet)
        {
            PendingAction pending = new PendingAction();
            pending.Action = action;

            if (action == null)
            {
                pending.ValidationError = "No action.";
                return pending;
            }

            if (action.Type != "create_playlist" && action.Type != "queue_tracks_last" && action.Type != "queue_tracks_next" && action.Type != "play_track_now")
            {
                pending.ValidationError = "Unsupported action type: " + action.Type;
                return pending;
            }

            if (!action.RequiresConfirmation)
            {
                pending.ValidationError = "Write action must require confirmation.";
                return pending;
            }

            if (action.TrackIds == null || action.TrackIds.Count == 0)
            {
                pending.ValidationError = "Action does not include tracks.";
                return pending;
            }

            if (action.Type == "play_track_now" && action.TrackIds.Count != 1)
            {
                pending.ValidationError = "play_track_now requires exactly one track.";
                return pending;
            }

            foreach (string id in action.TrackIds)
            {
                TrackInfo track;
                if (!candidateSet.TryGet(id, out track))
                {
                    pending.ValidationError = "Unknown trackId: " + id;
                    return pending;
                }
                pending.Tracks.Add(track);
            }

            return pending;
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
    }
}
