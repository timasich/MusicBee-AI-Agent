using System;
using System.IO;

namespace MusicBeePlugin
{
    public class AiOwnedPlaylistRegistry
    {
        private readonly string dataPath;

        public AiOwnedPlaylistRegistry(string dataPath)
        {
            this.dataPath = dataPath;
        }

        public string NormalizeName(string title)
        {
            string name = string.IsNullOrEmpty(title) ? "AI Playlist" : title.Trim();
            if (!name.StartsWith("AI - ", StringComparison.OrdinalIgnoreCase))
            {
                name = "AI - " + name;
            }
            return name;
        }

        public void Register(string playlistUrl, string name)
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    index.RegisterAiPlaylist(playlistUrl, name);
                }
            }
            catch (Exception ex)
            {
                Log("Could not register AI-owned playlist: " + ex.Message);
            }
        }

        public bool IsAiOwned(string playlistUrl)
        {
            try
            {
                using (LibraryIndex index = new LibraryIndex(DatabasePath))
                {
                    return index.IsAiOwnedPlaylist(playlistUrl);
                }
            }
            catch
            {
                return false;
            }
        }

        private string DatabasePath
        {
            get { return Path.Combine(dataPath, "library-index.sqlite"); }
        }

        private void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(dataPath);
                File.AppendAllText(Path.Combine(dataPath, "playlist-registry.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
