# IntentParser Architecture

## Current State

`IntentParser` is now hybrid:

- local rule-based parsing is always executed first;
- an optional LLM extraction pass converts multilingual user text into a compact intent JSON;
- the plugin merges both results and validates every field locally;
- the LLM intent pass never selects track IDs, playlist names, or write actions.

This keeps the old deterministic fallback while allowing user requests in languages not covered by hardcoded keyword rules.

## Flow

1. `AgentController` reads now-playing context and the library profile.
2. `IntentParser.Parse(userMessage, nowPlaying, profile)` builds a local rule intent.
3. If an AI provider is available, `IntentParser` asks the model only for intent extraction.
4. The model must return compact JSON with fields such as `similar`, `diverseArtists`, `targetDurationMinutes`, and `retrievalQuery`.
5. The parser extracts JSON, clamps numeric limits, and ignores malformed output.
6. The local and LLM intents are merged.
7. `LibrarySearchService` uses `RetrievalQuery` when available, otherwise falls back to the original user text.

## Safety Rules

- LLM intent output is advisory.
- Local rules are the fallback if the LLM call fails or returns invalid JSON.
- Write actions are still produced only by the main agent response and validated by `ActionValidator`.
- Track selection still uses only locally prepared candidate IDs.
- Read-only tool search uses `ParseLocal` to avoid recursive model calls.

## Intent Fields

- `Task`: broad task hint such as `info`, `search`, `create_playlist`, `queue`, or `play`.
- `QueryText`: original user request.
- `RetrievalQuery`: short normalized search query for local library retrieval.
- `SourceLanguage`: language hint from the model.
- `Confidence`: model confidence, clamped to `0..1`.
- `WasLlmEnhanced`: true when the LLM extraction pass was successfully merged.
- `Similar`, `Calmer`, `Energetic`: musical relation and mood hints.
- `DiverseArtists`, `DiverseAlbums`, `DeduplicateTracks`: selection constraints.
- `MaxTracksPerArtist`, `MaxTracksPerAlbum`: diversity limits.
- `TargetDurationSeconds`: requested playlist duration.
- `MaxTracks`: retrieval/candidate budget hint.

## Manual Test Ideas

Use the same model/settings as normal chat and try:

- Russian: "Собери плейлист на 45 минут в жанре industrial metal, без повторов и с разными исполнителями".
- English: "Create a 30 minute calm focus playlist, avoid the current artist".
- Mixed language: "Добавь в очередь energetic rock, different artists, no duplicates".
- Non-English query not covered by rules: ask in another language for a genre/mood playlist and check whether the candidate shortlist matches the intended genre better than before.

Expected behavior:

- requests in unsupported languages should still produce relevant candidates if the model can extract `RetrievalQuery`;
- duplicate versions should remain filtered by local ranking/validation;
- explicit limits like one track per artist should override default diversity limits;
- if the intent extraction fails, the plugin should still work through local fallback rules.
