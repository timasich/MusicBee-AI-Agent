using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    public class TrackInfo
    {
        public string Id;
        public string FileUrl;
        public string Title;
        public string Artist;
        public string Album;
        public string AlbumArtist;
        public string Genre;
        public string Year;
        public string Bpm;
        public string Mood;
        public string Rating;
        public string Duration;
        public string PlayCount;
        public string SkipCount;
        public string LastPlayed;
        public int Score;
        public string ScoreReason;

        public string DisplayName
        {
            get
            {
                string artist = string.IsNullOrEmpty(Artist) ? "Unknown Artist" : Artist;
                string title = string.IsNullOrEmpty(Title) ? "Unknown Title" : Title;
                return artist + " - " + title;
            }
        }

        public int DurationSeconds
        {
            get { return ParseDurationSeconds(Duration); }
        }

        private static int ParseDurationSeconds(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            int seconds;
            if (int.TryParse(value, out seconds))
            {
                return seconds;
            }

            string[] parts = value.Split(':');
            if (parts.Length == 2)
            {
                int minutes;
                int sec;
                if (int.TryParse(parts[0], out minutes) && int.TryParse(parts[1], out sec))
                {
                    return minutes * 60 + sec;
                }
            }

            return 0;
        }
    }

    public class CandidateSet
    {
        private readonly Dictionary<string, TrackInfo> tracksById = new Dictionary<string, TrackInfo>();

        public IList<TrackInfo> Tracks
        {
            get { return new List<TrackInfo>(tracksById.Values); }
        }

        public int Count
        {
            get { return tracksById.Count; }
        }

        public void Add(TrackInfo track)
        {
            if (track != null && !string.IsNullOrEmpty(track.Id))
            {
                tracksById[track.Id] = track;
            }
        }

        public bool TryGet(string id, out TrackInfo track)
        {
            return tracksById.TryGetValue(id, out track);
        }

        public bool Contains(string id)
        {
            return tracksById.ContainsKey(id);
        }
    }

    public class AiChatResponse
    {
        public string Message;
        public List<AiAction> Actions = new List<AiAction>();
        public List<ToolRequest> ToolRequests = new List<ToolRequest>();
    }

    public class ToolRequest
    {
        public string Name;
        public string Query;
        public int Limit = 40;
    }

    public class AiAction
    {
        public string Type;
        public bool RequiresConfirmation = true;
        public string Title;
        public List<string> TrackIds = new List<string>();
        public string Explanation;
    }

    public class PendingAction
    {
        public AiAction Action;
        public List<TrackInfo> Tracks = new List<TrackInfo>();
        public string ValidationError;

        public bool IsValid
        {
            get { return string.IsNullOrEmpty(ValidationError); }
        }

        public int TotalDurationSeconds
        {
            get
            {
                int total = 0;
                foreach (TrackInfo track in Tracks)
                {
                    total += track.DurationSeconds;
                }
                return total;
            }
        }
    }

    public class SearchIntent
    {
        public bool Similar;
        public bool Calmer;
        public bool Energetic;
        public bool ExcludeCurrentArtist;
        public int TargetDurationSeconds;
        public int MaxTracks = 60;
        public string QueryText;
    }

    public class AgentResult
    {
        public string Message;
        public PendingAction PendingAction;
        public string Error;
    }

    public enum PrivacyMode
    {
        StrictLocal,
        MetadataOnly,
        FullOnline
    }
}
