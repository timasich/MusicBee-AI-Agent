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
            TestValidator();
            TestRanker();
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
