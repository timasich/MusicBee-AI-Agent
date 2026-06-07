# Orchestrator v2

## Target Behavior

The plugin is an AI-first MusicBee agent. The model understands the user request,
plans the work, asks for more context when needed, selects final tracks, and
speaks to the user in the user's language.

The orchestrator executes explicit model commands. It uses English for internal
tool names, parameters, traces, and validation messages, except for artist,
album, track, playlist, and MusicBee field names.

## Conversation Context

The full chat history is sent with the current request when it fits the model
context window. The orchestrator does not expose separate
`get_conversation_summary` or `get_recent_user_requests` tools.

Playlist proposals that the user has not accepted are still persisted as chat
artifacts/proposals and can be referenced by the model through the conversation
history or future proposal lookup tools.

## Minimal Library Facets

The orchestrator exposes only the minimal static/semi-static library facets that
are useful for avoiding model guesses:

- `tracks`
- `genres`
- `artists`
- `years`
- `custom_fields`

Other metadata should be requested from MusicBee through targeted searches or
future API-backed tools only when the model needs it.

## Read-Only Tools

Current read-only tools:

- `get_now_playing`
- `get_current_queue`
- `search_library`
- `find_similar_tracks_basic`
- `get_library_facet`
- `get_artist_profile`
- `lookup_listenbrainz_similar_artists`
- `lookup_wikipedia`

`lookup_listenbrainz_similar_artists` is the explicit internet lookup path for
similar artist requests. It resolves the seed artist through MusicBrainz and
then fetches ListenBrainz LB radio similar artists. It is read-only.

`lookup_wikipedia` is the explicit internet lookup path for artist background,
history, biography, and other outside-library facts. It searches Wikipedia in
the requested language, falls back to English when needed, and returns a compact
summary plus source URL. It is read-only.

For `get_library_facet`, the `query` parameter must name one of the supported
facets. A filter can be appended after a colon, for example:

- `genres: metal`
- `artists: radiohead`
- `years: 199`
- `tracks: industrial metal`
- `custom_fields`

The `tracks` facet returns candidate tracks with usable `trackIds`. The
`custom_fields` facet returns MusicBee custom field slots with display names and
a configured flag. The other facets return value/count pairs.

## Playlist Workflow

The model and orchestrator can prepare actions, but no MusicBee write action is
executed without user confirmation. Because writes are confirmation-gated,
automatic backup playlists are not part of this architecture.

Allowed prepared write actions:

- `create_playlist`
- `queue_tracks_last`
- `queue_tracks_next`
- `play_track_now`

Playlist edit actions remain gated by validation and confirmation.

Requests that ask to add tracks to an existing playlist must use the playlist
management workflow even when the playlist name is incomplete. The orchestrator
sends the current playlist list to the model for selection. If there is no
single confident match, the model should ask the user for clarification instead
of creating a new playlist.

## Large Candidate Sets

When a request matches more tracks than can fit into the model context, the
orchestrator should move toward staged selection:

1. Return a candidate set id, total count, and compact diagnostics.
2. Send candidates in chunks.
3. Let the model select/reject tracks from each chunk.
4. Combine the selected tracks.
5. Validate objective constraints such as duration, duplicates, artist limits,
   album limits, missing tracks, and unavailable files.
6. If validation fails, send the validation report back to the model for a
   correction pass. The model may request more read-only tool data before
   returning the corrected action.
7. Prepare a pending action for user confirmation.

The current implementation has the first read-only tool pass, validation repair,
and explicit tool expansion during repair. Multi-chunk candidate paging is still
a follow-up.

## Removed Local Decision Policy

The orchestrator should not use hardcoded genre taxonomies, local mood mappings,
or hidden playlist repair rules to make musical decisions. It can rank exact
metadata/text matches and listening statistics, but the model is responsible for
semantic music judgment.
