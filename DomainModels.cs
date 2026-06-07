using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    public class TrackRecord
    {
        public string TrackId;
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
        public DateTime LastIndexedAt;
    }

    public class ArtistRecord
    {
        public string ArtistKey;
        public string Name;
        public int TrackCount;
    }

    public class AlbumRecord
    {
        public string AlbumKey;
        public string Title;
        public string AlbumArtist;
        public int TrackCount;
    }

    public class PlaylistRecord
    {
        public string PlaylistUrl;
        public string Name;
        public bool IsAiOwned;
    }

    public class TrackFeatures
    {
        public string TrackId;
        public string BpmBucket;
        public string YearBucket;
        public string RatingBucket;
        public string DurationBucket;
        public string LanguageHint;
    }

    public class CandidateTrack
    {
        public TrackRecord Track;
        public int Score;
        public readonly List<CandidateScore> Scores = new List<CandidateScore>();
    }

    public class CandidateScore
    {
        public string Source;
        public int Score;
        public string Reason;
    }

    public class ActionRequest
    {
        public string Type;
        public string Title;
        public readonly List<string> TrackIds = new List<string>();
        public bool RequiresConfirmation = true;
        public string Explanation;
    }

    public class LibraryProfile
    {
        public int TrackCount;
        public string TopArtists;
        public string TopGenres;
        public string TopAlbums;
        public string GeneratedAt;

        public string ToPromptSummary()
        {
            return "Library profile: tracks=" + TrackCount +
                "; top artists=" + TopArtists +
                "; top genres=" + TopGenres +
                "; top albums=" + TopAlbums +
                "; generatedAt=" + GeneratedAt;
        }
    }

    public class LibraryFacetValue
    {
        public string Value;
        public int Count;
    }

    public class ExternalArtistRecommendation
    {
        public string ArtistName;
        public string ArtistMbid;
        public int ListenCount;
        public string Source;
    }
}
