using System;

namespace MusicBeePlugin
{
    public class AdaptiveRetrievalBudget
    {
        public int CandidateCount;
        public int ScanLimit;
        public bool UseProfile;

        public static AdaptiveRetrievalBudget Create(PluginSettings settings, LibraryProfile profile, SearchIntent intent)
        {
            int librarySize = profile == null ? 0 : profile.TrackCount;
            bool durationRequest = intent != null && intent.TargetDurationSeconds > 0;
            bool rankingRequest = intent != null && !string.IsNullOrWhiteSpace(intent.RankingMode) && intent.RankingMode != "normal";

            AdaptiveRetrievalBudget budget = new AdaptiveRetrievalBudget();

            if (librarySize <= 0 || librarySize <= 500)
            {
                budget.CandidateCount = 40;
                budget.ScanLimit = 600;
                budget.UseProfile = false;
            }
            else if (librarySize <= 5000)
            {
                budget.CandidateCount = 40;
                budget.ScanLimit = 1200;
                budget.UseProfile = true;
            }
            else
            {
                budget.CandidateCount = 40;
                budget.ScanLimit = 2000;
                budget.UseProfile = true;
            }

            if (durationRequest)
            {
                int estimatedTracks = Math.Max(10, intent.TargetDurationSeconds / 210);
                budget.CandidateCount = Math.Min(500, estimatedTracks * 5);
                budget.ScanLimit = Math.Min(12000, estimatedTracks * 40);
            }

            if (rankingRequest)
            {
                budget.CandidateCount = intent.MaxTracks > 0 ? intent.MaxTracks : 60;
                budget.ScanLimit = librarySize > 0 ? librarySize : 12000;
            }

            if (!durationRequest && intent != null && intent.MaxTracks > 0)
            {
                budget.CandidateCount = intent.MaxTracks;
            }

            return budget;
        }
    }
}
