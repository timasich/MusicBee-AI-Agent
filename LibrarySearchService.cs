using System;
using System.Collections.Generic;
using System.IO;

namespace MusicBeePlugin
{
    public class LibrarySearchService
    {
        private readonly MusicBeeApiAdapter musicBee;
        private readonly string dataPath;
        private readonly CandidateRanker ranker = new CandidateRanker();

        public LibrarySearchService(MusicBeeApiAdapter musicBee, string dataPath)
        {
            this.musicBee = musicBee;
            this.dataPath = dataPath;
        }

        public List<TrackInfo> Search(SearchIntent intent, TrackInfo nowPlaying)
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    if (index.GetTrackCount() > 0)
                    {
                        LibraryProfile profile = index.GetProfile();
                        AdaptiveRetrievalBudget budget = AdaptiveRetrievalBudget.Create(null, profile, intent);
                        string[] tokens = BuildSearchTokens(intent, nowPlaying);
                        List<TrackInfo> candidates = tokens.Length == 0
                            ? index.LoadTracksForSearch(budget.ScanLimit)
                            : index.SearchTracksByTokens(tokens, budget.ScanLimit);
                        return ranker.Rank(candidates, intent, nowPlaying, budget.CandidateCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Index search fallback: " + ex.Message);
            }

            return musicBee.SearchLibrary(intent, nowPlaying);
        }

        public List<TrackInfo> Search(SearchIntent intent, TrackInfo nowPlaying, PluginSettings settings)
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    if (index.GetTrackCount() > 0)
                    {
                        LibraryProfile profile = index.GetProfile();
                        AdaptiveRetrievalBudget budget = AdaptiveRetrievalBudget.Create(settings, profile, intent);
                        string[] tokens = BuildSearchTokens(intent, nowPlaying);
                        List<TrackInfo> candidates = tokens.Length == 0
                            ? index.LoadTracksForSearch(budget.ScanLimit)
                            : index.SearchTracksByTokens(tokens, budget.ScanLimit);
                        return ranker.Rank(candidates, intent, nowPlaying, budget.CandidateCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Index search fallback: " + ex.Message);
            }

            return musicBee.SearchLibrary(intent, nowPlaying);
        }

        public LibraryProfile GetProfile()
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    return index.GetProfile();
                }
            }
            catch (Exception ex)
            {
                Log("Could not read library profile: " + ex.Message);
                return new LibraryProfile();
            }
        }

        private string DatabasePath
        {
            get { return Path.Combine(dataPath, "library-index.sqlite"); }
        }

        private static string[] BuildSearchTokens(SearchIntent intent, TrackInfo nowPlaying)
        {
            if (intent != null && intent.Similar && nowPlaying != null)
            {
                return NormalizationService.Tokenize(nowPlaying.Artist, nowPlaying.AlbumArtist, nowPlaying.Genre, nowPlaying.Mood, intent.QueryText);
            }

            return NormalizationService.Tokenize(intent == null ? "" : intent.QueryText);
        }

        private void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(dataPath);
                File.AppendAllText(Path.Combine(dataPath, "search.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
