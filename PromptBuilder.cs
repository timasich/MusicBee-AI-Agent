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

        public string BuildUserPrompt(string userMessage, TrackInfo nowPlaying, List<TrackInfo> candidates, string toolResults, LibraryProfile profile)
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
    }
}
