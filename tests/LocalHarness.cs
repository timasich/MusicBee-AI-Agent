using System;
using System.Collections.Generic;
using MusicBeePlugin;

public class LocalHarness
{
    public static int Main()
    {
        try
        {
            TestParser();
            TestParserChatTitle();
            TestIntentParserHybrid();
            TestValidator();
            TestValidatorAllowsExplicitVersions();
            TestValidatorRejectsDurationMiss();
            TestValidatorRejectsTrackCountMiss();
            TestValidatorAcceptsTrackCountRange();
            TestValidatorRejectsArtistLimit();
            TestIntentParserSelectionMode();
            TestIntentParserRequestedTrackCount();
            TestIntentParserTrackCountRangeAndExcludedAlbum();
            TestAdaptiveBudgetForLargePlaylist();
            TestPromptBuilderCompactsCandidateGroups();
            TestParserToolRequestForListenBrainz();
            TestParserToolRequestForWikipedia();
            TestRanker();
            TestRankerAppliesSearchFilters();
            TestDiversityAndDedup();
            TestRemixCanonicalDedup();
            TestConversationStoreNewChatResetsPlan();
            TestConversationStoreDeleteConversation();
            TestRankingModes();
            Console.WriteLine("All local harness tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void TestParser()
    {
        AiResponseParser parser = new AiResponseParser();
        AiChatResponse response = parser.Parse("{\"message\":\"ok\",\"actions\":[]}");
        Assert(response.Message == "ok", "Parser message failed.");
        Assert(response.Actions.Count == 0, "Parser actions failed.");
    }

    private static void TestParserChatTitle()
    {
        AiResponseParser parser = new AiResponseParser();
        AiChatResponse response = parser.Parse("{\"message\":\"ok\",\"chatTitle\":\"Industrial playlist\",\"actions\":[]}");
        Assert(response.ChatTitle == "Industrial playlist", "Parser chatTitle failed.");
    }

    private static void TestIntentParserHybrid()
    {
        PluginSettings settings = new PluginSettings();
        FakeAiProvider provider = new FakeAiProvider("{\"task\":\"create_playlist\",\"sourceLanguage\":\"ru\",\"similar\":false,\"calmer\":false,\"energetic\":true,\"excludeCurrentArtist\":false,\"diverseArtists\":true,\"diverseAlbums\":true,\"deduplicateTracks\":true,\"targetDurationMinutes\":45,\"maxTracks\":30,\"maxTracksPerArtist\":1,\"maxTracksPerAlbum\":1,\"retrievalQuery\":\"industrial metal energetic\",\"confidence\":0.86}");
        IntentParser parser = new IntentParser(settings, provider);

        SearchIntent intent = parser.Parse("Build a 45 minute industrial metal playlist with different artists.", null, new LibraryProfile());

        Assert(intent.WasLlmEnhanced, "Intent parser did not mark LLM enhancement.");
        Assert(intent.DiverseArtists, "Intent parser did not merge diverse artist intent.");
        Assert(intent.DiverseAlbums, "Intent parser did not merge diverse album intent.");
        Assert(intent.MaxTracksPerArtist == 1, "Intent parser did not keep max tracks per artist.");
        Assert(intent.TargetDurationSeconds == 2700, "Intent parser did not parse model duration.");
        Assert(intent.RetrievalQuery == "industrial metal energetic", "Intent parser did not use model retrieval query.");
    }

    private static void TestValidator()
    {
        CandidateSet set = new CandidateSet();
        TrackInfo track = new TrackInfo();
        track.Id = "track_1";
        track.FileUrl = "file://track";
        set.Add(track);

        AiAction action = new AiAction();
        action.Type = "queue_tracks_last";
        action.RequiresConfirmation = true;
        action.TrackIds.Add("track_1");

        PendingAction pending = new ActionValidator().Validate(action, set);
        Assert(pending.IsValid, "Validator rejected valid action.");
        Assert(pending.Tracks.Count == 1, "Validator did not resolve track.");
    }

    private static void TestValidatorAllowsExplicitVersions()
    {
        CandidateSet set = new CandidateSet();
        TrackInfo original = Track("track_1", "file://one", "Artist", "Song", "Album", "Rock", "4:00", 90);
        TrackInfo remix = Track("track_2", "file://two", "Artist", "Song - Remix by DJ", "Album", "Rock", "4:00", 80);
        set.Add(original);
        set.Add(remix);

        AiAction action = new AiAction();
        action.Type = "create_playlist";
        action.RequiresConfirmation = true;
        action.TrackIds.Add("track_1");
        action.TrackIds.Add("track_2");

        PendingAction deduped = new ActionValidator().Validate(action, set);
        Assert(deduped.IsValid, "Validator should keep a safe deduped action.");
        Assert(deduped.Tracks.Count == 1, "Validator should remove duplicate versions by default.");

        action.AllowVersions = true;
        PendingAction accepted = new ActionValidator().Validate(action, set);
        Assert(accepted.IsValid, "Validator should allow explicit versions.");
        Assert(accepted.Tracks.Count == 2, "Validator did not keep explicit versions.");
    }

    private static void TestValidatorRejectsDurationMiss()
    {
        CandidateSet set = new CandidateSet();
        TrackInfo track = Track("track_1", "file://one", "Artist", "Song", "Album", "Rock", "4:00", 90);
        set.Add(track);

        AiAction action = new AiAction();
        action.Type = "create_playlist";
        action.RequiresConfirmation = true;
        action.TrackIds.Add("track_1");

        SearchIntent intent = new SearchIntent();
        intent.TargetDurationSeconds = 30 * 60;

        PendingAction pending = new ActionValidator().Validate(action, set, intent);
        Assert(!pending.IsValid, "Validator should reject a selection far from target duration.");
        Assert(pending.ValidationError.IndexOf("duration", StringComparison.OrdinalIgnoreCase) >= 0, "Duration validation error was not reported.");
    }

    private static void TestValidatorRejectsTrackCountMiss()
    {
        CandidateSet set = new CandidateSet();
        set.Add(Track("track_1", "file://one", "Artist", "Song 1", "Album", "Rock", "4:00", 90));
        set.Add(Track("track_2", "file://two", "Artist", "Song 2", "Album", "Rock", "4:00", 90));

        AiAction action = new AiAction();
        action.Type = "append_to_playlist";
        action.RequiresConfirmation = true;
        action.TrackIds.Add("track_1");
        action.TrackIds.Add("track_2");

        SearchIntent intent = new SearchIntent();
        intent.RequestedTrackCount = 1;

        PendingAction pending = new ActionValidator().Validate(action, set, intent);
        Assert(!pending.IsValid, "Validator should reject a selection with the wrong requested track count.");
        Assert(pending.ValidationError.IndexOf("requestedTracks", StringComparison.OrdinalIgnoreCase) >= 0, "Track-count validation error was not reported.");
    }

    private static void TestValidatorAcceptsTrackCountRange()
    {
        CandidateSet set = new CandidateSet();
        set.Add(Track("track_1", "file://one", "Artist", "Song 1", "Album", "Rock", "4:00", 90));
        set.Add(Track("track_2", "file://two", "Artist", "Song 2", "Album", "Rock", "4:00", 90));
        set.Add(Track("track_3", "file://three", "Artist", "Song 3", "Album", "Rock", "4:00", 90));

        AiAction action = new AiAction();
        action.Type = "create_playlist";
        action.RequiresConfirmation = true;
        action.TrackIds.Add("track_1");
        action.TrackIds.Add("track_2");
        action.TrackIds.Add("track_3");

        SearchIntent intent = new SearchIntent();
        intent.RequestedTrackCountMin = 3;
        intent.RequestedTrackCountMax = 4;

        PendingAction pending = new ActionValidator().Validate(action, set, intent);
        Assert(pending.IsValid, "Validator should accept track count inside requested range.");

        action.TrackIds.RemoveAt(action.TrackIds.Count - 1);
        PendingAction tooSmall = new ActionValidator().Validate(action, set, intent);
        Assert(!tooSmall.IsValid, "Validator should reject track count below requested range.");
        Assert(tooSmall.ValidationError.IndexOf("requestedTracksMin", StringComparison.OrdinalIgnoreCase) >= 0, "Range validation error was not reported.");
    }

    private static void TestValidatorRejectsArtistLimit()
    {
        CandidateSet set = new CandidateSet();
        set.Add(Track("track_1", "file://one", "Artist", "Song 1", "Album 1", "Rock", "4:00", 90));
        set.Add(Track("track_2", "file://two", "Artist", "Song 2", "Album 2", "Rock", "4:00", 80));

        AiAction action = new AiAction();
        action.Type = "create_playlist";
        action.RequiresConfirmation = true;
        action.AllowVersions = true;
        action.TrackIds.Add("track_1");
        action.TrackIds.Add("track_2");

        SearchIntent intent = new SearchIntent();
        intent.MaxTracksPerArtist = 1;

        PendingAction pending = new ActionValidator().Validate(action, set, intent);
        Assert(!pending.IsValid, "Validator should reject selections that exceed maxTracksPerArtist.");
        Assert(pending.ValidationError.IndexOf("maxTracksPerArtist", StringComparison.OrdinalIgnoreCase) >= 0, "Artist-limit validation error was not reported.");
    }

    private static void TestIntentParserSelectionMode()
    {
        PluginSettings settings = new PluginSettings();
        FakeAiProvider provider = new FakeAiProvider("{\"task\":\"create_playlist\",\"selectionMode\":\"random\",\"retrievalQuery\":\"Мумий Тролль\",\"maxTracks\":10,\"confidence\":0.9}");
        IntentParser parser = new IntentParser(settings, provider);

        SearchIntent intent = parser.Parse("Добавь 10 случайных треков Мумий Тролль", null, new LibraryProfile());

        Assert(intent.SelectionMode == "random", "Intent parser did not preserve selectionMode=random.");
        Assert(intent.MaxTracks == 10, "Intent parser did not preserve random request maxTracks.");
    }

    private static void TestIntentParserRequestedTrackCount()
    {
        PluginSettings settings = new PluginSettings();
        FakeAiProvider provider = new FakeAiProvider("{\"task\":\"edit_playlist\",\"selectionMode\":\"random\",\"retrievalQuery\":\"Mumiy Troll\",\"targetDurationMinutes\":24,\"requestedTrackCount\":7,\"maxTracks\":80,\"confidence\":0.9}");
        IntentParser parser = new IntentParser(settings, provider);

        SearchIntent intent = parser.Parse("user request text", null, new LibraryProfile());

        Assert(intent.RequestedTrackCount == 7, "Intent parser did not preserve requestedTrackCount.");
        Assert(intent.MaxTracks == 80, "Intent parser did not keep candidate maxTracks separate.");
        Assert(intent.TargetDurationSeconds == 1440, "Intent parser did not preserve target duration.");
    }

    private static void TestIntentParserTrackCountRangeAndExcludedAlbum()
    {
        PluginSettings settings = new PluginSettings();
        FakeAiProvider provider = new FakeAiProvider("{\"task\":\"create_playlist\",\"retrievalQuery\":\"Mike Shinoda\",\"maxTracks\":40,\"confidence\":0.9}");
        IntentParser parser = new IntentParser(settings, provider);

        SearchIntent intent = parser.Parse("Build a playlist of 30-40 songs by Mike Shinoda without albums Post Traumatic and without instrumental tracks.", null, new LibraryProfile());

        Assert(intent.RequestedTrackCount == 0, "Intent parser should use range instead of exact count for 30-40.");
        Assert(intent.RequestedTrackCountMin == 30, "Intent parser did not infer requested range minimum.");
        Assert(intent.RequestedTrackCountMax == 40, "Intent parser did not infer requested range maximum.");
        Assert(ContainsNormalized(intent.ExcludedAlbums, "Post Traumatic"), "Intent parser did not add generic excluded album hint.");
        Assert(intent.ExcludeInstrumental, "Intent parser did not add instrumental exclusion hint.");
        Assert(intent.MaxTracks >= 40, "Intent parser did not keep enough max tracks for requested range.");
    }

    private static void TestAdaptiveBudgetForLargePlaylist()
    {
        LibraryProfile profile = new LibraryProfile();
        profile.TrackCount = 10000;
        SearchIntent intent = new SearchIntent();
        intent.RequestedTrackCountMin = 30;
        intent.RequestedTrackCountMax = 40;

        PluginSettings settings = new PluginSettings();
        settings.ContextWindowTokens = 8192;
        AdaptiveRetrievalBudget budget = AdaptiveRetrievalBudget.Create(settings, profile, intent);

        Assert(budget.CandidateCount >= 160, "Adaptive budget did not expand candidates for a 30-40 track request.");
        Assert(budget.CandidateCount <= 500, "Adaptive budget exceeded the hard prompt candidate cap.");
        Assert(budget.ScanLimit > budget.CandidateCount, "Adaptive budget did not expand scan limit.");

        PluginSettings smallContext = new PluginSettings();
        smallContext.ContextWindowTokens = 4000;
        AdaptiveRetrievalBudget smaller = AdaptiveRetrievalBudget.Create(smallContext, profile, intent);
        Assert(smaller.CandidateCount < budget.CandidateCount, "Adaptive budget did not react to a smaller context window.");
    }

    private static void TestPromptBuilderCompactsCandidateGroups()
    {
        PromptBuilder builder = new PromptBuilder(new PluginSettings());
        List<TrackInfo> tracks = new List<TrackInfo>();
        tracks.Add(Track("track_1", "file://one", "Artist", "Song 1", "Shared Album", "Rock", "4:00", 90));
        tracks.Add(Track("track_2", "file://two", "Artist", "Song 2", "Shared Album", "Rock", "3:30", 80));

        string prompt = builder.BuildUserPrompt("Create playlist", null, tracks, "", null, new SearchIntent());

        int albumIndex = prompt.IndexOf("; album=Shared Album");
        Assert(prompt.IndexOf("Group 1: artist=Artist; album=Shared Album") >= 0, "Prompt builder did not write album group header.");
        Assert(albumIndex >= 0 && prompt.IndexOf("; album=Shared Album", albumIndex + 1) < 0, "Prompt builder repeated grouped album on track rows.");
        Assert(prompt.IndexOf("id=track_1") >= 0 && prompt.IndexOf("id=track_2") >= 0, "Prompt builder omitted track IDs in compact groups.");
    }

    private static void TestParserToolRequestForListenBrainz()
    {
        AiResponseParser parser = new AiResponseParser();
        AiChatResponse response = parser.Parse("{\"message\":\"searching\",\"toolRequests\":[{\"name\":\"lookup_listenbrainz_similar_artists\",\"query\":\"Pain\",\"limit\":10}],\"actions\":[]}");

        Assert(response.ToolRequests.Count == 1, "Parser did not read ListenBrainz tool request.");
        Assert(response.ToolRequests[0].Name == "lookup_listenbrainz_similar_artists", "Parser changed ListenBrainz tool name.");
        Assert(response.ToolRequests[0].Query == "Pain", "Parser changed ListenBrainz tool query.");
    }

    private static void TestParserToolRequestForWikipedia()
    {
        AiResponseParser parser = new AiResponseParser();
        AiChatResponse response = parser.Parse("{\"message\":\"searching\",\"toolRequests\":[{\"name\":\"lookup_wikipedia\",\"query\":\"ru: Pain\",\"limit\":10}],\"actions\":[]}");

        Assert(response.ToolRequests.Count == 1, "Parser did not read Wikipedia tool request.");
        Assert(response.ToolRequests[0].Name == "lookup_wikipedia", "Parser changed Wikipedia tool name.");
        Assert(response.ToolRequests[0].Query == "ru: Pain", "Parser changed Wikipedia tool query.");
    }

    private static void TestRanker()
    {
        TrackInfo now = new TrackInfo();
        now.FileUrl = "file://current";
        now.Artist = "Red";
        now.Genre = "Rock";

        TrackInfo similar = new TrackInfo();
        similar.FileUrl = "file://similar";
        similar.Artist = "Red";
        similar.Genre = "Rock";
        similar.Title = "Fight Inside";

        TrackInfo other = new TrackInfo();
        other.FileUrl = "file://other";
        other.Artist = "Other";
        other.Genre = "Jazz";
        other.Title = "Other";

        SearchIntent intent = new SearchIntent();
        intent.Similar = true;
        intent.MaxTracks = 2;

        List<TrackInfo> ranked = new CandidateRanker().Rank(new TrackInfo[] { other, similar }, intent, now, 2);
        Assert(ranked.Count > 0, "Ranker returned no candidates.");
        Assert(ranked[0].Artist == "Red", "Ranker did not rank similar track first.");
    }

    private static void TestRankerAppliesSearchFilters()
    {
        TrackInfo allowed = Track("track_1", "file://allowed", "Mike Shinoda", "Already Over", "Crimson Chapter", "Rock", "3:00", 0);
        TrackInfo excludedAlbum = Track("track_2", "file://album", "Mike Shinoda", "Open Door", "Post Traumatic", "Rock", "3:00", 0);
        TrackInfo instrumental = Track("track_3", "file://instrumental", "Mike Shinoda", "Fine Instrumental", "Single", "Instrumental", "3:00", 0);

        SearchIntent intent = new SearchIntent();
        intent.RetrievalQuery = "Mike Shinoda";
        intent.ExcludedAlbums.Add("Post Traumatic");
        intent.ExcludeInstrumental = true;

        List<TrackInfo> ranked = new CandidateRanker().Rank(new TrackInfo[] { allowed, excludedAlbum, instrumental }, intent, null, 10);

        Assert(ranked.Count == 1, "Ranker did not remove excluded album and instrumental tracks.");
        Assert(ranked[0].Title == "Already Over", "Ranker returned the wrong filtered track.");
    }

    private static void TestDiversityAndDedup()
    {
        TrackInfo now = new TrackInfo();
        now.FileUrl = "file://current";
        now.Artist = "Red";
        now.Genre = "Industrial Metal";

        TrackInfo a1 = new TrackInfo();
        a1.FileUrl = "file://a1";
        a1.Artist = "Pain";
        a1.Album = "A";
        a1.AlbumArtist = "Pain";
        a1.Genre = "Industrial Metal";
        a1.Title = "Same Song";

        TrackInfo a2 = new TrackInfo();
        a2.FileUrl = "file://a2";
        a2.Artist = "Pain";
        a2.Album = "B";
        a2.AlbumArtist = "Pain";
        a2.Genre = "Industrial Metal";
        a2.Title = "Same Song (Live)";

        TrackInfo b1 = new TrackInfo();
        b1.FileUrl = "file://b1";
        b1.Artist = "Static-X";
        b1.Album = "C";
        b1.AlbumArtist = "Static-X";
        b1.Genre = "Industrial Metal";
        b1.Title = "Other Song";

        SearchIntent intent = new SearchIntent();
        intent.Similar = true;
        intent.DiverseArtists = true;
        intent.DiverseAlbums = true;
        intent.DeduplicateTracks = true;
        intent.MaxTracksPerArtist = 1;
        intent.MaxTracksPerAlbum = 1;

        List<TrackInfo> ranked = new CandidateRanker().Rank(new TrackInfo[] { a1, a2, b1 }, intent, now, 3);
        Assert(ranked.Count == 2, "Ranker did not remove duplicate canonical title.");
        Assert(ranked[0].Artist != ranked[1].Artist, "Ranker did not diversify artists.");
    }

    private static void TestRemixCanonicalDedup()
    {
        TrackInfo original = Track("track_1", "file://one", "Artist", "Song", "Album", "Rock", "4:00", 90);
        TrackInfo remix = Track("track_2", "file://two", "Artist", "Song - Remix by DJ", "Album", "Rock", "4:00", 80);
        Assert(NormalizationService.CanonicalTrackKey(original) == NormalizationService.CanonicalTrackKey(remix), "Remix canonical key did not match original.");
    }

    private static void TestConversationStoreNewChatResetsPlan()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mb_agent_test_" + Guid.NewGuid().ToString("N"));
        ConversationStore store = new ConversationStore(dir);
        LastPlanContext context = new LastPlanContext();
        context.OriginalRequest = "old";
        context.SelectedTrackCount = 1;
        store.SaveLastPlan(context);
        store.NewConversation();
        Assert(store.LastPlan == null, "New chat did not reset last plan.");
    }

    private static void TestConversationStoreDeleteConversation()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mb_agent_test_" + Guid.NewGuid().ToString("N"));
        ConversationStore store = new ConversationStore(dir);
        string first = store.ActiveConversationId;
        store.SaveMessage("user", "first");
        string second = store.NewConversation();
        store.SaveMessage("user", "second");

        Assert(store.DeleteConversation(second), "ConversationStore did not delete active conversation.");
        Assert(store.ActiveConversationId != second, "ConversationStore kept deleted active conversation selected.");
        Assert(store.ListConversations().Count == 1, "ConversationStore did not remove deleted conversation from list.");

        Assert(store.DeleteConversation(first), "ConversationStore did not delete remaining conversation.");
        Assert(store.ListConversations().Count == 1, "ConversationStore should create a replacement chat after deleting the last one.");
        Assert(!string.IsNullOrEmpty(store.ActiveConversationId), "ConversationStore replacement active id is empty.");
    }

    private static void TestRankingModes()
    {
        CandidateRanker ranker = new CandidateRanker();
        List<TrackInfo> tracks = new List<TrackInfo>();
        TrackInfo favorite = Track("track_1", "file://favorite", "Artist", "Rated", "Album", "Rock", "4:00", 0);
        favorite.Rating = "5";
        favorite.PlayCount = "3";
        tracks.Add(favorite);
        TrackInfo played = Track("track_2", "file://played", "Artist", "Played", "Album", "Rock", "4:00", 0);
        played.Rating = "40";
        played.PlayCount = "80";
        tracks.Add(played);
        TrackInfo skipped = Track("track_3", "file://skipped", "Artist", "Skipped", "Album", "Rock", "4:00", 0);
        skipped.Rating = "0";
        skipped.PlayCount = "0";
        skipped.SkipCount = "5";
        tracks.Add(skipped);

        SearchIntent favorites = new SearchIntent();
        favorites.RankingMode = "favorites";
        favorites.MaxTracks = 2;
        List<TrackInfo> favoriteResult = ranker.Rank(tracks, favorites, null, 2);
        Assert(favoriteResult.Count == 2, "Favorites ranking did not return rated/played tracks.");
        Assert(favoriteResult[0].Title == "Rated", "Favorites ranking did not prioritize normalized rating.");

        SearchIntent mostPlayed = new SearchIntent();
        mostPlayed.RankingMode = "most_played";
        mostPlayed.MaxTracks = 2;
        List<TrackInfo> playedResult = ranker.Rank(tracks, mostPlayed, null, 2);
        Assert(playedResult.Count >= 1, "Most-played ranking returned no tracks.");
        Assert(playedResult[0].Title == "Played", "Most-played ranking did not prioritize play count.");
    }

    private static TrackInfo Track(string id, string fileUrl, string artist, string title, string album, string genre, string duration, int score)
    {
        TrackInfo track = new TrackInfo();
        track.Id = id;
        track.FileUrl = fileUrl;
        track.Artist = artist;
        track.Title = title;
        track.Album = album;
        track.AlbumArtist = artist;
        track.Genre = genre;
        track.Duration = duration;
        track.Score = score;
        track.ScoreReason = "test";
        return track;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool ContainsNormalized(List<string> values, string expected)
    {
        string expectedKey = NormalizationService.NormalizeKey(expected);
        foreach (string value in values)
        {
            if (NormalizationService.NormalizeKey(value) == expectedKey)
            {
                return true;
            }
        }
        return false;
    }

    private sealed class FakeAiProvider : IAiProvider
    {
        private readonly string response;

        public FakeAiProvider(string response)
        {
            this.response = response;
        }

        public string SendChat(string systemPrompt, string userPrompt)
        {
            return response;
        }

        public string SendChat(string systemPrompt, string userPrompt, System.Threading.CancellationToken cancellationToken)
        {
            return response;
        }
    }
}
