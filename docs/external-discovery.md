# External Discovery

External recommendation policy was removed from the hidden local runtime during
the AI-first orchestrator cleanup.

External lookup is now exposed only as explicit read-only orchestrator tools.
The active external tools are:

- `lookup_listenbrainz_similar_artists`
- `lookup_wikipedia`

`lookup_listenbrainz_similar_artists` resolves the seed artist through
MusicBrainz, then fetches similar artists from ListenBrainz LB radio.

`lookup_wikipedia` searches Wikipedia in the requested language and returns a
compact article summary plus source URL.

Both tools return factual source data to the model; the model decides how to use
that data and explains it to the user in the user's language.

Future additions should follow the same shape:

- targeted MusicBrainz entity lookups beyond artist MBID resolution
