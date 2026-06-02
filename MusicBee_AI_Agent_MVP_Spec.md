# MusicBee AI Agent — MVP Specification

## 1. Project Summary

**MusicBee AI Agent** is a MusicBee plugin that adds an AI chat panel inside the player. The user communicates with an AI agent in natural language and asks it to analyze the current track, inspect the local music library, create playlists, manage the Now Playing queue, and optionally search the internet for artist/track information or new music recommendations.

The project should support both **local LLMs** and **online models**. The first version should use an **OpenAI-compatible Chat Completions API**, so it can work with LM Studio, Ollama-compatible gateways, local OpenAI-compatible servers, and online providers.

The main architectural principle:

> The AI model must not control MusicBee directly. It should return structured intents/actions. The plugin validates them, shows a preview to the user, and only then applies confirmed actions through the MusicBee API.

---

## 2. Goal of the First Version

The first version should prove the basic agent workflow:

```text
User opens the AI chat panel in MusicBee
→ writes a natural language request
→ the plugin collects MusicBee context
→ the AI responds or proposes a structured action
→ the user confirms the action
→ the plugin applies the action in MusicBee
```

The MVP does **not** need to be a complete recommendation engine yet. It should provide a reliable foundation: chat UI, AI provider connection, current track reading, basic library search, playlist creation, and queue management.

---

## 3. MVP Features

### 3.1 Chat Panel

The plugin should add a dockable panel or standalone window with a simple chat UI.

Minimum UI:

```text
[Message history]

User: Find something similar to the current track
AI: I found 12 matching tracks...

[Action Preview]
[Add to queue] [Create playlist] [Cancel]

[Input field]
[Send]
```

The UI should support:

- User messages
- AI responses
- Structured action preview cards
- Confirmation buttons
- Error messages
- Basic settings access

---

### 3.2 AI Provider Settings

The plugin should include settings for an OpenAI-compatible provider.

Required settings:

```text
AI Provider:
- OpenAI-compatible endpoint

Connection:
- Base URL
- API Key
- Model name
- Temperature
- Max tokens
- Privacy mode
```

Suggested provider presets:

```text
- LM Studio
- Ollama / OpenAI-compatible gateway
- Custom OpenAI-compatible endpoint
- Future: OpenAI, Anthropic, Gemini, etc.
```

---

### 3.3 Context From MusicBee

The plugin should be able to read the following MusicBee context:

```text
- current track file path / URL
- title
- artist
- album
- album artist
- genre
- year
- BPM
- mood
- rating
- duration
- play count
- skip count
- last played
- current Now Playing queue summary
- found tracks from the local library
```

The plugin should **not** send the entire music library to the LLM. It should send only relevant context and candidate tracks.

---

### 3.4 Agent Tools

The AI agent should work through a limited set of tools. Tools are implemented by the plugin, not by the model.

#### Read-only tools

```text
get_now_playing
get_current_queue
search_library
find_similar_tracks_basic
get_track_metadata
get_playlist_list
get_playlist_tracks
```

#### Write tools for MVP

```text
queue_tracks_next
queue_tracks_last
create_playlist
play_track_now
```

#### Dangerous actions not allowed in MVP

```text
delete_playlist
delete_file
bulk_edit_tags
overwrite_user_playlist
clear_queue
change_rating
commit_tags_to_files
```

---

## 4. Agent Workflow

The agent should work in a controlled loop:

```text
1. User sends a message.
2. Plugin collects base context:
   - current track
   - queue summary
   - available tools
3. Request is sent to the LLM.
4. LLM returns either:
   - a normal text response
   - or a structured action / intent
5. If the action requires data, the plugin calls read-only tools:
   - search_library
   - find_similar_tracks_basic
   - get_playlist_tracks
6. Plugin sends candidate data back to the LLM if needed.
7. LLM produces final response and proposed action.
8. Plugin shows preview to the user.
9. User confirms.
10. Plugin applies the action through MusicBee API.
11. Result is logged.
```

The LLM must never call MusicBee directly. It can only request structured actions.

---

## 5. AI Response Format

For the first version, require the model to return JSON.

### Response with action

```json
{
  "message": "I found several tracks similar to the current one, but a little calmer.",
  "actions": [
    {
      "type": "create_playlist",
      "requiresConfirmation": true,
      "title": "AI - Similar but Calmer",
      "trackIds": ["track_1", "track_2", "track_3"],
      "explanation": "The tracks were selected by similar genre, artist relation, BPM, rating, and low skip count."
    }
  ]
}
```

### Response without action

```json
{
  "message": "Now playing: Radiohead - Weird Fishes. In your library, this track has a high rating and a low skip count.",
  "actions": []
}
```

Important rules:

- The model must not invent file paths.
- The model must choose only from `trackIds` provided by the plugin.
- The plugin must validate all `trackIds` before executing any action.
- Any write action should be previewed before execution.

---

## 6. Internal Architecture

Suggested architecture:

```text
MusicBee AI Plugin
│
├── UI
│   ├── ChatPanel
│   ├── MessageList
│   ├── ActionPreviewCard
│   └── SettingsPanel
│
├── Agent
│   ├── AgentController
│   ├── PromptBuilder
│   ├── AiResponseParser
│   ├── ToolDispatcher
│   └── ActionValidator
│
├── AI Providers
│   ├── IAiProvider
│   ├── OpenAiCompatibleProvider
│   ├── LmStudioProvider
│   └── OllamaProvider
│
├── MusicBee Integration
│   ├── MusicBeeApiAdapter
│   ├── NowPlayingService
│   ├── LibrarySearchService
│   ├── QueueService
│   └── PlaylistService
│
├── Data
│   ├── PluginSettings
│   ├── LocalIndexDb
│   ├── TrackCache
│   └── ActionHistory
│
└── Internet / External Info
    ├── IWebSearchProvider
    ├── ArtistInfoService
    ├── TrackInfoService
    └── RecommendationDiscoveryService
```

---

## 7. Suggested Data Model

SQLite is recommended, but the MVP can start with a small in-memory or file-based cache. The architecture should leave room for SQLite indexing.

Minimum tables:

```sql
CREATE TABLE tracks (
    id TEXT PRIMARY KEY,
    file_url TEXT NOT NULL,
    title TEXT,
    artist TEXT,
    album TEXT,
    album_artist TEXT,
    genre TEXT,
    year INTEGER,
    bpm REAL,
    mood TEXT,
    rating INTEGER,
    duration_sec INTEGER,
    play_count INTEGER,
    skip_count INTEGER,
    last_played TEXT,
    last_indexed_at TEXT
);

CREATE TABLE chat_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL,
    role TEXT NOT NULL,
    content TEXT NOT NULL
);

CREATE TABLE ai_actions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL,
    user_prompt TEXT,
    action_type TEXT,
    action_json TEXT,
    status TEXT
);
```

Future tables:

```sql
CREATE TABLE track_features (
    track_id TEXT PRIMARY KEY,
    energy REAL,
    danceability REAL,
    brightness REAL,
    loudness REAL,
    vocalness REAL,
    embedding_vector BLOB
);
```

---

## 8. Internet Access

Internet access should be a separate module, not part of the MusicBee API adapter.

Potential future functions:

```text
- find artist information
- find album information
- find track information
- find similar artists
- suggest new music outside the local library
- explain a genre, scene, label, or music movement
- compare local listening profile with external recommendations
```

The MVP may include only a placeholder interface for internet search. The first functional version can simply return text recommendations without downloading music or integrating with streaming services.

---

## 9. Privacy Modes

The plugin should support different privacy levels.

### Strict Local

```text
- Use only local library data
- Use only local LLM providers
- Do not send anything to external services
```

### Metadata Only

```text
- Online model/search can receive artist/title/album/genre
- Do not send local file paths
- Do not send detailed listening history
```

### Full Online

```text
- User explicitly allows extended context
- Online services may receive richer metadata and listening summaries
- Still avoid sending local file paths unless absolutely necessary
```

For online mode, local paths should be removed or replaced with internal track IDs.

---

## 10. Safety Levels for Actions

### Safe

Can be executed after normal confirmation, and may later be allowed automatically:

```text
- show information
- find tracks
- create a new AI playlist
- add tracks to the end of the queue
```

### Medium

Always requires user confirmation:

```text
- play now
- queue next
- replace an AI-owned playlist
- change shuffle/repeat/crossfade settings
```

### Dangerous

Not allowed in MVP:

```text
- delete files
- delete playlists
- clear queue
- overwrite user playlists
- bulk edit tags
- commit tag changes to audio files
```

---

## 11. First User Scenarios

### Scenario 1 — Current Track

```text
User:
What is playing now?

Agent:
Now playing: Artist - Track. It is from Album, genre Genre, rating 80, and you have played it 12 times.
```

Required tools:

```text
get_now_playing
```

---

### Scenario 2 — Similar Tracks

```text
User:
Find something similar to the current track.

Plugin:
- get_now_playing
- search_library by artist/genre/mood/BPM
- send candidates to LLM

Agent:
I found 15 similar tracks. I can add them to the queue or create a playlist.
```

Required tools:

```text
get_now_playing
find_similar_tracks_basic
queue_tracks_last
create_playlist
```

---

### Scenario 3 — Playlist by Request

```text
User:
Create a one-hour playlist for calm focused work.

Plugin:
- LLM extracts criteria
- search_library returns candidates
- LLM selects final tracks from candidates
- plugin shows preview

User:
Create it.

Plugin:
- create_playlist
```

Required tools:

```text
search_library
create_playlist
```

---

### Scenario 4 — New Music From the Internet

```text
User:
Suggest something new based on what I have been listening to recently.

Plugin:
- gets local top artists / genres summary
- sends anonymized summary to online/web module if allowed
- receives recommendations
- displays them separately from local library tracks
```

Required future modules:

```text
RecommendationDiscoveryService
IWebSearchProvider
PrivacyFilter
```

---

## 12. What Not To Do In MVP

Do not implement these in the first version:

```text
- complex audio embeddings
- automatic tag writing
- bulk library organization
- sending the entire library to the LLM
- direct LLM access to MusicBee API
- music downloader
- streaming service integration
- full AutoDJ replacement
- destructive actions
```

---

## 13. Main MVP Result

The user should feel:

> “I have a music assistant inside MusicBee. It understands what is playing, can inspect my local library, can create a playlist by request, and applies changes only after my confirmation.”

---

## 14. Short Prompt for Codex

```text
Create the first version of a MusicBee AI Agent plugin.

The plugin should add an AI chat panel to MusicBee. The user can type natural language requests. The plugin sends the request plus MusicBee context to an OpenAI-compatible chat completions endpoint, receives either a normal text answer or a structured JSON action, shows a preview, and applies confirmed safe actions through the MusicBee API.

MVP features:
1. Dockable or standalone chat UI panel.
2. Settings for OpenAI-compatible provider:
   - base URL
   - API key
   - model name
   - temperature
   - privacy mode
3. Read current track metadata from MusicBee.
4. Search local library by basic metadata.
5. Create a playlist from selected tracks.
6. Add selected tracks to Now Playing queue.
7. Use structured action responses from the model.
8. Validate all actions before executing.
9. Do not allow destructive actions in MVP.
10. Do not allow the model to invent file paths. The model can only choose from track IDs provided by the plugin.

Architecture:
- UI layer: ChatPanel, SettingsPanel, ActionPreviewCard.
- Agent layer: AgentController, PromptBuilder, AiResponseParser, ToolDispatcher, ActionValidator.
- AI provider layer: IAiProvider and OpenAiCompatibleProvider.
- MusicBee integration layer: MusicBeeApiAdapter, NowPlayingService, LibrarySearchService, QueueService, PlaylistService.
- Data layer: settings, optional SQLite track cache, chat/action history.

The LLM must never call MusicBee directly. It returns structured intents/actions. The plugin validates and executes them.
```

---

## 15. Recommended First Implementation Order

```text
1. Build and run the sample MusicBee plugin.
2. Add basic plugin settings for AI provider.
3. Implement OpenAiCompatibleProvider.
4. Add simple chat window/panel.
5. Add get_now_playing context.
6. Make first prompt: user message + current track.
7. Parse JSON responses from the model.
8. Add action preview card.
9. Implement queue_tracks_last.
10. Implement create_playlist.
11. Add basic library search.
12. Add find_similar_tracks_basic.
13. Add logging for actions and errors.
14. Add privacy mode filtering.
```
