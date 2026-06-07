using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MusicBeePlugin
{
    public class LibraryIndex : IDisposable
    {
        private IntPtr db;
        private readonly string databasePath;

        public LibraryIndex(string databasePath)
        {
            this.databasePath = databasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            int rc = SQLiteNative.sqlite3_open(databasePath, out db);
            if (rc != SQLiteNative.SQLITE_OK)
            {
                throw new InvalidOperationException("Could not open SQLite database: " + databasePath);
            }

            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA synchronous=NORMAL;");
            EnsureSchema();
        }

        public string DatabasePath
        {
            get { return databasePath; }
        }

        public void Dispose()
        {
            if (db != IntPtr.Zero)
            {
                SQLiteNative.sqlite3_close(db);
                db = IntPtr.Zero;
            }
        }

        public void EnsureSchema()
        {
            Execute(
                "CREATE TABLE IF NOT EXISTS tracks (" +
                "track_id TEXT PRIMARY KEY, " +
                "file_url TEXT NOT NULL UNIQUE, " +
                "title TEXT, artist TEXT, album TEXT, album_artist TEXT, genre TEXT, " +
                "year TEXT, bpm TEXT, mood TEXT, rating TEXT, duration TEXT, " +
                "play_count TEXT, skip_count TEXT, last_played TEXT, last_indexed_at TEXT);");

            Execute("CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);");
            Execute("CREATE INDEX IF NOT EXISTS idx_tracks_album_artist ON tracks(album_artist);");
            Execute("CREATE INDEX IF NOT EXISTS idx_tracks_genre ON tracks(genre);");

            Execute(
                "CREATE TABLE IF NOT EXISTS artists (" +
                "artist_key TEXT PRIMARY KEY, name TEXT, track_count INTEGER);");

            Execute(
                "CREATE TABLE IF NOT EXISTS albums (" +
                "album_key TEXT PRIMARY KEY, title TEXT, album_artist TEXT, track_count INTEGER);");

            Execute(
                "CREATE TABLE IF NOT EXISTS genres (" +
                "genre_key TEXT PRIMARY KEY, name TEXT, track_count INTEGER);");

            Execute(
                "CREATE TABLE IF NOT EXISTS playlists (" +
                "playlist_url TEXT PRIMARY KEY, name TEXT, is_ai_owned INTEGER DEFAULT 0);");

            Execute(
                "CREATE TABLE IF NOT EXISTS playlist_tracks (" +
                "playlist_url TEXT, track_id TEXT, position INTEGER, PRIMARY KEY(playlist_url, track_id, position));");

            Execute(
                "CREATE TABLE IF NOT EXISTS track_tokens (" +
                "track_id TEXT, token TEXT, source TEXT, PRIMARY KEY(track_id, token, source));");

            Execute(
                "CREATE TABLE IF NOT EXISTS index_state (" +
                "key TEXT PRIMARY KEY, value TEXT);");

            Execute(
                "CREATE TABLE IF NOT EXISTS library_profile (" +
                "key TEXT PRIMARY KEY, value TEXT);");

            Execute(
                "CREATE TABLE IF NOT EXISTS agent_actions (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, created_at TEXT, action_type TEXT, action_json TEXT, status TEXT);");
        }

        public int GetTrackCount()
        {
            string value = QuerySingleString("SELECT COUNT(*) FROM tracks;");
            int count;
            return int.TryParse(value, out count) ? count : 0;
        }

        public void BeginTransaction()
        {
            Execute("BEGIN TRANSACTION;");
        }

        public void Commit()
        {
            Execute("COMMIT;");
        }

        public void Rollback()
        {
            Execute("ROLLBACK;");
        }

        public void ClearTracks()
        {
            Execute("DELETE FROM track_tokens;");
            Execute("DELETE FROM artists;");
            Execute("DELETE FROM albums;");
            Execute("DELETE FROM genres;");
            Execute("DELETE FROM library_profile;");
            Execute("DELETE FROM tracks;");
        }

        public void UpsertTrack(TrackRecord track)
        {
            if (track == null || string.IsNullOrEmpty(track.FileUrl))
            {
                return;
            }

            string sql =
                "INSERT OR REPLACE INTO tracks (" +
                "track_id, file_url, title, artist, album, album_artist, genre, year, bpm, mood, rating, duration, play_count, skip_count, last_played, last_indexed_at) VALUES (" +
                Q(track.TrackId) + "," +
                Q(track.FileUrl) + "," +
                Q(track.Title) + "," +
                Q(track.Artist) + "," +
                Q(track.Album) + "," +
                Q(track.AlbumArtist) + "," +
                Q(track.Genre) + "," +
                Q(track.Year) + "," +
                Q(track.Bpm) + "," +
                Q(track.Mood) + "," +
                Q(track.Rating) + "," +
                Q(track.Duration) + "," +
                Q(track.PlayCount) + "," +
                Q(track.SkipCount) + "," +
                Q(track.LastPlayed) + "," +
                Q(track.LastIndexedAt.ToString("s")) + ");";
            Execute(sql);
            UpsertTrackTokens(track);
        }

        public List<TrackInfo> LoadTracksForSearch(int maxTracks)
        {
            string sql =
                "SELECT track_id,file_url,title,artist,album,album_artist,genre,year,bpm,mood,rating,duration,play_count,skip_count,last_played " +
                "FROM tracks LIMIT " + maxTracks + ";";
            return QueryTracks(sql);
        }

        public List<TrackInfo> SearchTracksByTokens(string[] tokens, int maxTracks)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return LoadTracksForSearch(maxTracks);
            }

            string inList = "";
            for (int i = 0; i < tokens.Length; i++)
            {
                if (i > 0) inList += ",";
                inList += Q(tokens[i]);
            }

            string sql =
                "SELECT DISTINCT t.track_id,t.file_url,t.title,t.artist,t.album,t.album_artist,t.genre,t.year,t.bpm,t.mood,t.rating,t.duration,t.play_count,t.skip_count,t.last_played " +
                "FROM tracks t INNER JOIN track_tokens tt ON tt.track_id = t.track_id WHERE tt.token IN (" + inList + ") LIMIT " + maxTracks + ";";
            return QueryTracks(sql);
        }

        public void RebuildAggregates()
        {
            Execute("DELETE FROM artists;");
            Execute("INSERT INTO artists(artist_key,name,track_count) SELECT lower(trim(artist)), artist, count(*) FROM tracks WHERE artist IS NOT NULL AND artist <> '' GROUP BY lower(trim(artist));");

            Execute("DELETE FROM albums;");
            Execute("INSERT INTO albums(album_key,title,album_artist,track_count) SELECT lower(trim(album || '|' || album_artist)), album, album_artist, count(*) FROM tracks WHERE album IS NOT NULL AND album <> '' GROUP BY lower(trim(album || '|' || album_artist));");

            Execute("DELETE FROM genres;");
            Execute("INSERT INTO genres(genre_key,name,track_count) SELECT lower(trim(genre)), genre, count(*) FROM tracks WHERE genre IS NOT NULL AND genre <> '' GROUP BY lower(trim(genre));");

            Execute("DELETE FROM library_profile;");
            SetProfile("track_count", GetTrackCount().ToString());
            SetProfile("top_artists", QueryNameCount("SELECT name, track_count FROM artists ORDER BY track_count DESC LIMIT 10;"));
            SetProfile("top_genres", QueryNameCount("SELECT name, track_count FROM genres ORDER BY track_count DESC LIMIT 10;"));
            SetProfile("top_albums", QueryNameCount("SELECT title, track_count FROM albums ORDER BY track_count DESC LIMIT 10;"));
            SetProfile("generated_at", DateTime.Now.ToString("s"));
        }

        public LibraryProfile GetProfile()
        {
            LibraryProfile profile = new LibraryProfile();
            profile.TrackCount = GetTrackCount();
            profile.TopArtists = GetProfileValue("top_artists");
            profile.TopGenres = GetProfileValue("top_genres");
            profile.TopAlbums = GetProfileValue("top_albums");
            profile.GeneratedAt = GetProfileValue("generated_at");
            return profile;
        }

        public List<LibraryFacetValue> GetFacetValues(string field, string query, int limit)
        {
            field = (field ?? "").Trim().ToLowerInvariant();
            query = (query ?? "").Trim();
            limit = Math.Max(1, Math.Min(500, limit <= 0 ? 80 : limit));

            if (field == "genres")
            {
                return QueryFacetValues("SELECT name, track_count FROM genres " + FacetWhere("name", query) + " ORDER BY track_count DESC, name LIMIT " + limit + ";");
            }
            if (field == "artists")
            {
                return QueryFacetValues("SELECT name, track_count FROM artists " + FacetWhere("name", query) + " ORDER BY track_count DESC, name LIMIT " + limit + ";");
            }
            if (field == "years")
            {
                return QueryFacetValues("SELECT year, COUNT(*) FROM tracks WHERE year IS NOT NULL AND year <> ''" + FacetAnd("year", query) + " GROUP BY year ORDER BY year DESC LIMIT " + limit + ";");
            }

            return new List<LibraryFacetValue>();
        }

        public void RegisterAiPlaylist(string playlistUrl, string name)
        {
            if (string.IsNullOrEmpty(playlistUrl))
            {
                return;
            }

            Execute("INSERT OR REPLACE INTO playlists(playlist_url, name, is_ai_owned) VALUES (" + Q(playlistUrl) + "," + Q(name) + ",1);");
        }

        public bool IsAiOwnedPlaylist(string playlistUrl)
        {
            string value = QuerySingleString("SELECT is_ai_owned FROM playlists WHERE playlist_url = " + Q(playlistUrl) + " LIMIT 1;");
            return value == "1";
        }

        public void SetState(string key, string value)
        {
            Execute("INSERT OR REPLACE INTO index_state(key, value) VALUES (" + Q(key) + "," + Q(value) + ");");
        }

        public string GetState(string key)
        {
            return QuerySingleString("SELECT value FROM index_state WHERE key = " + Q(key) + " LIMIT 1;");
        }

        private string QuerySingleString(string sql)
        {
            IntPtr stmt = IntPtr.Zero;
            try
            {
                int rc = SQLiteNative.sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero);
                if (rc != SQLiteNative.SQLITE_OK)
                {
                    throw new InvalidOperationException("SQLite prepare failed: " + sql);
                }

                rc = SQLiteNative.sqlite3_step(stmt);
                if (rc == SQLiteNative.SQLITE_ROW)
                {
                    IntPtr text = SQLiteNative.sqlite3_column_text(stmt, 0);
                    return PtrToStringUtf8(text);
                }

                return "";
            }
            finally
            {
                if (stmt != IntPtr.Zero)
                {
                    SQLiteNative.sqlite3_finalize(stmt);
                }
            }
        }

        private void Execute(string sql)
        {
            IntPtr error = IntPtr.Zero;
            int rc = SQLiteNative.sqlite3_exec(db, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out error);
            if (rc != SQLiteNative.SQLITE_OK)
            {
                string message = error == IntPtr.Zero ? "unknown SQLite error" : Marshal.PtrToStringAnsi(error);
                if (error != IntPtr.Zero)
                {
                    SQLiteNative.sqlite3_free(error);
                }
                throw new InvalidOperationException(message + " SQL: " + sql);
            }
        }

        private void UpsertTrackTokens(TrackRecord track)
        {
            Execute("DELETE FROM track_tokens WHERE track_id = " + Q(track.TrackId) + ";");
            InsertTokens(track.TrackId, "title", NormalizationService.Tokenize(track.Title));
            InsertTokens(track.TrackId, "artist", NormalizationService.Tokenize(track.Artist, track.AlbumArtist));
            InsertTokens(track.TrackId, "album", NormalizationService.Tokenize(track.Album));
            InsertTokens(track.TrackId, "genre", NormalizationService.SplitGenreTokens(track.Genre));
            InsertTokens(track.TrackId, "mood", NormalizationService.Tokenize(track.Mood));
            InsertToken(track.TrackId, "bpm_bucket", NormalizationService.BpmBucket(track.Bpm));
            InsertToken(track.TrackId, "year_bucket", NormalizationService.YearBucket(track.Year));
            InsertToken(track.TrackId, "rating_bucket", NormalizationService.RatingBucket(track.Rating));
            InsertToken(track.TrackId, "duration_bucket", NormalizationService.DurationBucket(track.Duration));
        }

        private void InsertTokens(string trackId, string source, string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                InsertToken(trackId, source, tokens[i]);
            }
        }

        private void InsertToken(string trackId, string source, string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return;
            }

            Execute("INSERT OR IGNORE INTO track_tokens(track_id, token, source) VALUES (" + Q(trackId) + "," + Q(token) + "," + Q(source) + ");");
        }

        private List<TrackInfo> QueryTracks(string sql)
        {
            List<TrackInfo> tracks = new List<TrackInfo>();
            IntPtr stmt = IntPtr.Zero;
            try
            {
                int rc = SQLiteNative.sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero);
                if (rc != SQLiteNative.SQLITE_OK)
                {
                    throw new InvalidOperationException("SQLite prepare failed: " + sql);
                }

                while (SQLiteNative.sqlite3_step(stmt) == SQLiteNative.SQLITE_ROW)
                {
                    TrackInfo track = new TrackInfo();
                    track.Id = Column(stmt, 0);
                    track.FileUrl = Column(stmt, 1);
                    track.Title = Column(stmt, 2);
                    track.Artist = Column(stmt, 3);
                    track.Album = Column(stmt, 4);
                    track.AlbumArtist = Column(stmt, 5);
                    track.Genre = Column(stmt, 6);
                    track.Year = Column(stmt, 7);
                    track.Bpm = Column(stmt, 8);
                    track.Mood = Column(stmt, 9);
                    track.Rating = Column(stmt, 10);
                    track.Duration = Column(stmt, 11);
                    track.PlayCount = Column(stmt, 12);
                    track.SkipCount = Column(stmt, 13);
                    track.LastPlayed = Column(stmt, 14);
                    tracks.Add(track);
                }
            }
            finally
            {
                if (stmt != IntPtr.Zero)
                {
                    SQLiteNative.sqlite3_finalize(stmt);
                }
            }

            return tracks;
        }

        private string QueryNameCount(string sql)
        {
            StringBuilder builder = new StringBuilder();
            IntPtr stmt = IntPtr.Zero;
            try
            {
                int rc = SQLiteNative.sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero);
                if (rc != SQLiteNative.SQLITE_OK)
                {
                    return "";
                }

                while (SQLiteNative.sqlite3_step(stmt) == SQLiteNative.SQLITE_ROW)
                {
                    if (builder.Length > 0) builder.Append("; ");
                    builder.Append(Column(stmt, 0));
                    builder.Append("=");
                    builder.Append(Column(stmt, 1));
                }
            }
            finally
            {
                if (stmt != IntPtr.Zero)
                {
                    SQLiteNative.sqlite3_finalize(stmt);
                }
            }

            return builder.ToString();
        }

        private List<LibraryFacetValue> QueryFacetValues(string sql)
        {
            List<LibraryFacetValue> values = new List<LibraryFacetValue>();
            IntPtr stmt = IntPtr.Zero;
            try
            {
                int rc = SQLiteNative.sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero);
                if (rc != SQLiteNative.SQLITE_OK)
                {
                    return values;
                }

                while (SQLiteNative.sqlite3_step(stmt) == SQLiteNative.SQLITE_ROW)
                {
                    LibraryFacetValue value = new LibraryFacetValue();
                    value.Value = Column(stmt, 0);
                    int count;
                    value.Count = int.TryParse(Column(stmt, 1), out count) ? count : 0;
                    if (!string.IsNullOrEmpty(value.Value))
                    {
                        values.Add(value);
                    }
                }
            }
            finally
            {
                if (stmt != IntPtr.Zero)
                {
                    SQLiteNative.sqlite3_finalize(stmt);
                }
            }

            return values;
        }

        private static string FacetWhere(string column, string query)
        {
            return string.IsNullOrEmpty(query) ? "" : "WHERE lower(" + column + ") LIKE " + Q("%" + query.ToLowerInvariant() + "%");
        }

        private static string FacetAnd(string column, string query)
        {
            return string.IsNullOrEmpty(query) ? "" : " AND lower(" + column + ") LIKE " + Q("%" + query.ToLowerInvariant() + "%");
        }

        private void SetProfile(string key, string value)
        {
            Execute("INSERT OR REPLACE INTO library_profile(key, value) VALUES (" + Q(key) + "," + Q(value) + ");");
        }

        private string GetProfileValue(string key)
        {
            return QuerySingleString("SELECT value FROM library_profile WHERE key = " + Q(key) + " LIMIT 1;");
        }

        private static string Column(IntPtr stmt, int column)
        {
            return PtrToStringUtf8(SQLiteNative.sqlite3_column_text(stmt, column));
        }

        private static string PtrToStringUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return "";
            }

            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
            {
                length++;
            }

            byte[] bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static byte[] Utf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
            return bytes;
        }

        private static string Q(string value)
        {
            if (value == null)
            {
                return "NULL";
            }

            return "'" + value.Replace("'", "''") + "'";
        }
    }

    internal static class SQLiteNative
    {
        public const int SQLITE_OK = 0;
        public const int SQLITE_ROW = 100;

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_open(string filename, out IntPtr db);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_close(IntPtr db);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void sqlite3_free(IntPtr ptr);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_step(IntPtr stmt);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
    }
}
