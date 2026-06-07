# Development Notes

Status: alpha.

## Roles

- Model: understands the user request, returns structured intent, asks for read-only tools, writes final user text.
- Orchestrator: executes read-only tools, validates actions, prepares previews, calls MusicBee only after confirmation.
- MusicBee API: source of truth for library, queue, playback, and playlists.

## Intent Contract

The model returns fields such as:

- `task`
- `playlistOperation`
- `retrievalQuery`
- `targetPlaylistName`
- `requestedTrackCount`
- `targetDurationSeconds`
- `selectionMode`

The orchestrator must not infer these from the user language.

## Action Contract

Supported write actions:

- `create_playlist`
- `queue_tracks_last`
- `queue_tracks_next`
- `play_track_now`
- `append_to_playlist`
- `replace_ai_playlist`
- `delete_ai_playlist`

All write actions require confirmation.

## Read-Only Tools

- `search_library`
- `search_artist_tracks`
- `get_library_facet`
- `get_artist_profile`
- `lookup_listenbrainz_similar_artists`
- `lookup_wikipedia`

## Known Technical Debt

- Many nullable warnings remain.
- UI is functional, not polished.
- Retrieval/ranking is still early.
- Release packaging is manual.
