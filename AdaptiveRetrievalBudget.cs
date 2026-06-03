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
            bool smallModel = settings != null && settings.SmallLocalModelMode;
            bool durationRequest = intent != null && intent.TargetDurationSeconds > 0;

            AdaptiveRetrievalBudget budget = new AdaptiveRetrievalBudget();

            if (librarySize <= 0 || librarySize <= 500)
            {
                budget.CandidateCount = smallModel ? 12 : 40;
                budget.ScanLimit = smallModel ? 120 : 600;
                budget.UseProfile = false;
            }
            else if (librarySize <= 5000)
            {
                budget.CandidateCount = smallModel ? 12 : 40;
                budget.ScanLimit = smallModel ? 250 : 1200;
                budget.UseProfile = !smallModel;
            }
            else
            {
                budget.CandidateCount = smallModel ? 10 : 40;
                budget.ScanLimit = smallModel ? 300 : 2000;
                budget.UseProfile = true;
            }

            if (durationRequest)
            {
                budget.CandidateCount = smallModel ? 20 : 120;
                budget.ScanLimit = smallModel ? 500 : 4000;
            }

            if (intent != null && intent.MaxTracks > 0)
            {
                budget.CandidateCount = intent.MaxTracks;
            }

            return budget;
        }
    }
}
