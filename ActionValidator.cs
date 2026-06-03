namespace MusicBeePlugin
{
    public class ActionValidator
    {
        public PendingAction Validate(AiAction action, CandidateSet candidateSet)
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
    }
}
