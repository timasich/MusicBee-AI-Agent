# Manual Tests

Run these in MusicBee after installing the plugin.

## Settings

1. Open settings.
2. Set base URL, model, max tokens, and timeout.
3. Save and reopen chat.

Expected: new requests use the new provider settings.

## Chat Title

1. Start a new chat.
2. Send a playlist request.

Expected: the chat title changes after the first AI response.

## Playlist Create

Request a playlist with genre, duration, and track-count limits.

Expected: preview matches the requested constraints.

## Playlist Append

Request adding tracks to an existing playlist using an incomplete playlist name.

Expected: the model selects an existing playlist or asks for clarification.

## Similar Artists

Ask for artists similar to a known artist.

Expected: the trace shows a ListenBrainz lookup.

## Wikipedia

Ask for background about an artist.

Expected: the answer includes library context and Wikipedia information.
