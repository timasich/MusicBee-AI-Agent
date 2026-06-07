# Agent Planner v2

## Current Direction

`Planned Mode` has been removed from the active runtime path.

The previous implementation used local planning components such as
`PlanningPolicy`, `GroupEvaluator`, `DurationOptimizer`, `PlaylistVerifier`, and
`RepairPlanner` to build playlist drafts locally. That made the plugin partly
responsible for musical decisions, which conflicts with the AI-first agent
architecture.

The desired flow is now:

1. Send the user request and full available chat history to the model.
2. Let the model request read-only orchestrator tools.
3. Let the orchestrator return factual MusicBee data and minimal facets.
4. Let the model select or refine tracks.
5. Let the orchestrator validate objective constraints.
6. Prepare a pending MusicBee action.
7. Apply it only after user confirmation.

## Active Planner Boundary

The model owns:

- intent understanding;
- orchestration planning;
- semantic playlist quality;
- final track selection;
- user-facing language.

The orchestrator owns:

- MusicBee API calls;
- indexed library search;
- minimal library facets;
- chunking/future staged retrieval;
- objective validation;
- action preview and confirmation.

## Minimal Facets

The active facet set is intentionally small:

- `tracks`
- `genres`
- `artists`
- `years`
- `custom_fields`

The model should request these before making genre/year/artist assumptions.

## Legacy Components

The local planned-playlist classes were deleted or narrowed out of the runtime.
The plugin no longer contains a parallel local planner that selects, repairs, or
semantically rewrites playlists outside the model-directed orchestration flow.

Removed classes/logic include:

- `AgentPlanner` and planned-mode helper classes;
- `ComplexityClassifier`
- `PlannedPlaylistAgent`
- `PlanningPolicy`
- `GroupDiscovery`
- `GroupEvaluator`
- `PlaylistDraftBuilder`
- `DurationOptimizer`
- `PlaylistVerifier`
- `RepairPlanner`
- `GenreIntentResolver`
- `GenreMatchPolicy`
- `MusicTaxonomy`
- `FavoriteTrackPolicy`
- `PoolDiagnosticsBuilder`
- `ConversationResolver`
- `RefinementParser`
- `TextLocalizer`
- legacy MusicBrainz/ListenBrainz discovery wrappers

Objective checks now live in `ActionValidator`, which validates model-selected
actions against concrete constraints such as confirmation, known track ids,
duplicate policy, target duration, artist limits, and album limits.

External providers are exposed only as explicit read-only tools. Similar artist
requests should use `lookup_listenbrainz_similar_artists`, which resolves the
seed artist through MusicBrainz and fetches ListenBrainz LB radio similar
artists. Artist history/biography/background requests can use
`lookup_wikipedia`, which returns a compact Wikipedia summary and source URL.
