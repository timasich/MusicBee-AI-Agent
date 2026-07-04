using System;

namespace MusicBeePlugin
{
    public class AdaptiveRetrievalBudget
    {
        private const int DefaultCandidateCount = 80;
        private const int MinPromptCandidateCount = 80;
        private const int HardMaxPromptCandidateCount = 500;
        private const int ReservedPromptTokens = 2500;
        private const int EstimatedCompactTrackTokens = 30;

        public int CandidateCount;
        public int ScanLimit;
        public bool UseProfile;

        public static AdaptiveRetrievalBudget Create(PluginSettings settings, LibraryProfile profile, SearchIntent intent)
        {
            int librarySize = profile == null ? 0 : profile.TrackCount;
            bool durationRequest = intent != null && intent.TargetDurationSeconds > 0;
            bool rankingRequest = intent != null && !string.IsNullOrWhiteSpace(intent.RankingMode) && intent.RankingMode != "normal";
            int requestedSelection = RequestedSelectionCount(intent);
            int maxPromptCandidateCount = MaxPromptCandidateCount(settings);

            AdaptiveRetrievalBudget budget = new AdaptiveRetrievalBudget();

            if (librarySize <= 0 || librarySize <= 500)
            {
                budget.CandidateCount = DefaultCandidateCount;
                budget.ScanLimit = 800;
                budget.UseProfile = false;
            }
            else if (librarySize <= 5000)
            {
                budget.CandidateCount = DefaultCandidateCount;
                budget.ScanLimit = 2500;
                budget.UseProfile = true;
            }
            else
            {
                budget.CandidateCount = DefaultCandidateCount;
                budget.ScanLimit = 5000;
                budget.UseProfile = true;
            }

            if (requestedSelection > 0)
            {
                int candidateTarget = Math.Max(DefaultCandidateCount, requestedSelection * 4);
                budget.CandidateCount = Math.Min(maxPromptCandidateCount, candidateTarget);
                budget.ScanLimit = Math.Max(budget.ScanLimit, requestedSelection * 160);
            }

            if (durationRequest)
            {
                int estimatedTracks = Math.Max(10, intent.TargetDurationSeconds / 210);
                budget.CandidateCount = Math.Min(maxPromptCandidateCount, Math.Max(budget.CandidateCount, estimatedTracks * 5));
                budget.ScanLimit = Math.Max(budget.ScanLimit, estimatedTracks * 160);
            }

            if (rankingRequest)
            {
                budget.CandidateCount = Math.Min(maxPromptCandidateCount, Math.Max(budget.CandidateCount, requestedSelection > 0 ? requestedSelection * 4 : DefaultCandidateCount));
                budget.ScanLimit = librarySize > 0 ? librarySize : 12000;
            }

            if (!durationRequest && requestedSelection <= 0 && intent != null && intent.MaxTracks > DefaultCandidateCount)
            {
                budget.CandidateCount = Math.Min(maxPromptCandidateCount, intent.MaxTracks);
            }

            if (librarySize > 0)
            {
                budget.ScanLimit = Math.Min(librarySize, budget.ScanLimit);
                budget.CandidateCount = Math.Min(librarySize, budget.CandidateCount);
            }
            else
            {
                budget.ScanLimit = Math.Min(12000, budget.ScanLimit);
            }
            budget.ScanLimit = Math.Max(budget.CandidateCount, budget.ScanLimit);
            return budget;
        }

        private static int MaxPromptCandidateCount(PluginSettings settings)
        {
            int contextTokens = settings == null ? 8192 : settings.ContextWindowTokens;
            if (contextTokens <= ReservedPromptTokens)
            {
                return MinPromptCandidateCount;
            }

            int estimated = (contextTokens - ReservedPromptTokens) / EstimatedCompactTrackTokens;
            if (estimated < MinPromptCandidateCount)
            {
                return MinPromptCandidateCount;
            }
            return Math.Min(HardMaxPromptCandidateCount, estimated);
        }

        private static int RequestedSelectionCount(SearchIntent intent)
        {
            if (intent == null)
            {
                return 0;
            }
            if (intent.RequestedTrackCountMax > 0)
            {
                return intent.RequestedTrackCountMax;
            }
            if (intent.RequestedTrackCount > 0)
            {
                return intent.RequestedTrackCount;
            }
            return 0;
        }
    }
}
