using System;
using System.Collections.Generic;
using System.IO;

namespace MusicBeePlugin
{
    public class LibraryIndexingService
    {
        private readonly string dataPath;

        public LibraryIndexingService(string dataPath)
        {
            this.dataPath = dataPath;
        }

        public string DatabasePath
        {
            get { return Path.Combine(dataPath, "library-index.sqlite"); }
        }

        public bool HasAnyIndexedTracks()
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    return index.GetTrackCount() > 0;
                }
            }
            catch (Exception ex)
            {
                Log("Index check failed: " + ex);
                return false;
            }
        }

        public void MarkChecked()
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    index.SetState("last_checked_at", DateTime.Now.ToString("s"));
                    Log("Library index already exists with " + index.GetTrackCount() + " tracks.");
                }
            }
            catch (Exception ex)
            {
                Log("Index mark checked failed: " + ex);
            }
        }

        public void RebuildIndexFromSnapshot(List<TrackInfo> tracks)
        {
            using (LibraryIndex index = new LibraryIndex(DatabasePath))
            {
                Log("Index rebuild started. Snapshot tracks: " + tracks.Count);

                index.BeginTransaction();
                try
                {
                    index.ClearTracks();
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        index.UpsertTrack(ToRecord(tracks[i], i + 1));
                    }

                    index.RebuildAggregates();
                    index.SetState("last_full_index_at", DateTime.Now.ToString("s"));
                    index.SetState("track_count", tracks.Count.ToString());
                    index.Commit();
                    Log("Index rebuild completed. Indexed tracks: " + tracks.Count);
                }
                catch
                {
                    index.Rollback();
                    throw;
                }
            }
        }

        private static TrackRecord ToRecord(TrackInfo track, int ordinal)
        {
            return new TrackRecord
            {
                TrackId = "mb_" + ordinal,
                FileUrl = track.FileUrl,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                AlbumArtist = track.AlbumArtist,
                Genre = track.Genre,
                Year = track.Year,
                Bpm = track.Bpm,
                Mood = track.Mood,
                Rating = track.Rating,
                Duration = track.Duration,
                PlayCount = track.PlayCount,
                SkipCount = track.SkipCount,
                LastPlayed = track.LastPlayed,
                LastIndexedAt = DateTime.Now
            };
        }

        private void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(dataPath);
                File.AppendAllText(Path.Combine(dataPath, "index.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
