# Smoke tests

Run these after installing `MB_AI_Agent.dll` into the MusicBee `Plugins` folder and restarting MusicBee.

## 1. Plugin visibility

Steps:

1. Open MusicBee.
2. Go to Preferences -> Plugins.
3. Find `MusicBee AI Agent`.

Expected:

- Plugin is visible.
- Plugin is enabled.

## 2. Settings

Steps:

1. Open `Tools -> MusicBee AI Agent - Settings`.
2. Set Base URL, Model, Max tokens, and Timeout.
3. Click OK.
4. Open settings again.

Expected:

- Changed values are persisted.
- Model value is the new value.

## 3. Settings hot reload

Steps:

1. Open chat.
2. Open settings.
3. Change model name.
4. Click OK.
5. Reopen chat from the Tools menu.
6. Send a simple message.

Expected:

- The request uses the newly configured model.
- MusicBee restart is not required.

## 4. Current track

Steps:

1. Start playback.
2. Open chat.
3. Ask: `Какая музыка играет сейчас?`

Expected:

- Assistant mentions the current artist/title.
- No action preview is shown.

## 5. Similar playlist

Steps:

1. Start playback.
2. Ask: `Составь плейлист похожих песен`.

Expected:

- Action preview appears.
- Preview lists candidate tracks.
- User can uncheck tracks.
- `Create Playlist` creates a playlist only from selected tracks.

## 6. Queue actions

Steps:

1. Ask: `Найди похожие песни`.
2. In preview, click `Queue Last`.

Expected:

- Selected tracks are added to Now Playing.

## 7. SQLite index creation

Steps:

1. Start MusicBee with the plugin installed.
2. Wait for startup background work to finish.
3. Check plugin persistent storage folder for `library-index.sqlite`.

Expected:

- SQLite index file exists.
- It is not zero bytes.
- `index.log` contains `Index rebuild completed`.

## 8. First chat title

Steps:

1. Click `New Chat`.
2. Send a specific first request, for example a playlist request for a genre or artist.
3. Wait for the first assistant response.

Expected:

- The chat title changes from the default title to a concise title related to the request.
- The title is visible in the current chat header and in the chat list.

## 9. Manual index rebuild

Steps:

1. Open `Tools -> MusicBee AI Agent - Rebuild Library Index`.
2. Confirm that the message shows `Snapshot tracks: N`.
3. Wait until disk activity stops.
4. Check `index.log`.

Expected:

- `N` is greater than zero.
- `library-index.sqlite` exists.
- `index.log` contains `Index rebuild started` and `Index rebuild completed`.

## 10. AI-owned playlist registry

Steps:

1. Ask: `Составь плейлист похожих песен`.
2. Confirm `Create Playlist`.
3. Check playlist name in MusicBee.
4. Check `library-index.sqlite` with any SQLite viewer if available.

Expected:

- Playlist name starts with `AI -`.
- Created playlist is registered in `playlists` with `is_ai_owned = 1`.

## 11. Local harness

Steps:

1. Build the plugin.
2. Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-local-tests.ps1
```

Expected:

- Output: `All local harness tests passed.`

## 12. Adaptive retrieval smoke test

Steps:

1. Rebuild library index.
2. Ask: `Найди похожие песни`.
3. Ask: `Составь плейлист похожих песен на 2 часа`.

Expected:

- Similar search uses index-backed candidates.
- Duration request can return more candidates than a small non-duration request.
- If index search fails, `search.log` contains fallback details and the old MusicBee API search still works.

## 13. Diversity and deduplication

Steps:

1. Rebuild the library index after adding test music from multiple artists.
2. Ask: `Составь плейлист нескольких исполнителей в жанрах Industrial/Heavy metal на 1 час. Хочу разные песни разных альбомов разных исполнителей без повторов`.
3. Inspect the preview before confirming.

Expected:

- Preview should include multiple artists when the local library has them.
- Repeated versions of the same song should be reduced.
- If too few matching artists exist locally, the assistant message should mention that diversity is limited by the local shortlist.
