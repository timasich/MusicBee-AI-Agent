namespace MusicBeePlugin
{
    public class ActionValidator
    {
        public PendingAction Validate(AiAction action, CandidateSet candidateSet)
        {
            return Validate(action, candidateSet, null);
        }

        public PendingAction Validate(AiAction action, CandidateSet candidateSet, SearchIntent intent)
        {
            PendingAction pending = new PendingAction();
            pending.Action = action;
            pending.TargetDurationSeconds = TargetDuration(action, intent);
            System.Collections.Generic.Dictionary<string, bool> selectedFiles = new System.Collections.Generic.Dictionary<string, bool>();
            System.Collections.Generic.Dictionary<string, bool> selectedCanonical = new System.Collections.Generic.Dictionary<string, bool>();

            if (action == null)
            {
                pending.ValidationError = "No action.";
                return pending;
            }

            if (action.Type != "create_playlist" && action.Type != "queue_tracks_last" && action.Type != "queue_tracks_next" && action.Type != "play_track_now" && action.Type != "delete_ai_playlist" && action.Type != "replace_ai_playlist" && action.Type != "append_to_playlist")
            {
                pending.ValidationError = "Unsupported action type: " + action.Type;
                return pending;
            }

            if (!action.RequiresConfirmation)
            {
                pending.ValidationError = "Write action must require confirmation.";
                return pending;
            }

            if (action.Type == "delete_ai_playlist")
            {
                if (string.IsNullOrEmpty(action.PlaylistUrl))
                {
                    pending.ValidationError = "Delete action does not include a playlist.";
                }
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

                if (!string.IsNullOrEmpty(track.FileUrl))
                {
                    if (selectedFiles.ContainsKey(track.FileUrl))
                    {
                        continue;
                    }
                    selectedFiles[track.FileUrl] = true;
                }

                string canonical = NormalizationService.CanonicalTrackKey(track);
                if (!action.AllowVersions && !string.IsNullOrEmpty(canonical))
                {
                    if (selectedCanonical.ContainsKey(canonical))
                    {
                        continue;
                    }
                    selectedCanonical[canonical] = true;
                }

                pending.Tracks.Add(track);
            }

            if (pending.Tracks.Count == 0)
            {
                pending.ValidationError = "Action only contained duplicate tracks.";
                return pending;
            }

            ValidateObjectiveConstraints(pending, intent);
            return pending;
        }

        private static int TargetDuration(AiAction action, SearchIntent intent)
        {
            if (action != null && action.TargetDurationSeconds > 0)
            {
                return action.TargetDurationSeconds;
            }
            return intent == null ? 0 : intent.TargetDurationSeconds;
        }

        private static void ValidateObjectiveConstraints(PendingAction pending, SearchIntent intent)
        {
            if (pending == null || intent == null || !pending.IsValid)
            {
                return;
            }

            if (pending.TargetDurationSeconds > 0)
            {
                int total = pending.TotalDurationSeconds;
                int tolerance = DurationTolerance(pending.TargetDurationSeconds);
                if (total > 0 && System.Math.Abs(total - pending.TargetDurationSeconds) > tolerance)
                {
                    pending.ValidationError = "Selected duration is outside the requested tolerance. targetSeconds=" +
                        pending.TargetDurationSeconds + ", actualSeconds=" + total + ", toleranceSeconds=" + tolerance + ".";
                    return;
                }
            }

            int requestedMin = intent.RequestedTrackCountMin;
            int requestedMax = intent.RequestedTrackCountMax;
            if (requestedMin > 0 || requestedMax > 0)
            {
                if (requestedMin <= 0)
                {
                    requestedMin = requestedMax;
                }
                if (requestedMax <= 0)
                {
                    requestedMax = requestedMin;
                }
                if (pending.Tracks.Count < requestedMin || pending.Tracks.Count > requestedMax)
                {
                    pending.ValidationError = "Selected track count is outside the requested range. requestedTracksMin=" +
                        requestedMin + ", requestedTracksMax=" + requestedMax + ", actualTracks=" + pending.Tracks.Count + ".";
                    return;
                }
            }
            else if (intent.RequestedTrackCount > 0 && pending.Tracks.Count != intent.RequestedTrackCount)
            {
                pending.ValidationError = "Selected track count does not match the requested count. requestedTracks=" +
                    intent.RequestedTrackCount + ", actualTracks=" + pending.Tracks.Count + ".";
                return;
            }

            if (intent.MaxTracksPerArtist > 0 && !WithinLimit(pending.Tracks, intent.MaxTracksPerArtist, true))
            {
                pending.ValidationError = "Selected tracks exceed maxTracksPerArtist=" + intent.MaxTracksPerArtist + ".";
                return;
            }

            if (intent.MaxTracksPerAlbum > 0 && !WithinLimit(pending.Tracks, intent.MaxTracksPerAlbum, false))
            {
                pending.ValidationError = "Selected tracks exceed maxTracksPerAlbum=" + intent.MaxTracksPerAlbum + ".";
            }
        }

        private static int DurationTolerance(int targetSeconds)
        {
            if (targetSeconds <= 45 * 60)
            {
                return 90;
            }
            if (targetSeconds <= 2 * 3600)
            {
                return 180;
            }
            return 300;
        }

        private static bool WithinLimit(System.Collections.Generic.List<TrackInfo> tracks, int limit, bool artist)
        {
            System.Collections.Generic.Dictionary<string, int> counts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (TrackInfo track in tracks ?? new System.Collections.Generic.List<TrackInfo>())
            {
                string key = artist ? NormalizationService.ArtistKey(track.Artist) : NormalizationService.AlbumKey(track.Album, track.AlbumArtist);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                int count;
                counts.TryGetValue(key, out count);
                count++;
                if (count > limit)
                {
                    return false;
                }
                counts[key] = count;
            }
            return true;
        }
    }
}
