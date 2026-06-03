# MusicBee AI Agent Plugin

MusicBee AI Agent is an experimental MusicBee plugin that adds an AI chat assistant to the player.

The assistant can read MusicBee context, ask an OpenAI-compatible chat completions endpoint for a structured response, preview proposed actions, and execute only confirmed safe actions through the MusicBee API.

## Current status

This is an MVP plus early architecture work.

Working:

- MusicBee plugin shell.
- Standalone chat window.
- Settings window.
- OpenAI-compatible provider.
- Current track context.
- Basic local library search.
- Basic similarity scoring.
- Action preview.
- Create playlist.
- Queue next / queue last.
- Play now.
- Small local model mode.
- JSON repair pass.
- Initial SQLite-backed library index foundation.

Not finished:

- Full retrieval/ranking architecture.
- Advanced library profile.
- AI-owned playlist registry.
- Unit tests.
- Release packaging.
- Dockable MusicBee panel.

## Requirements

- MusicBee desktop installation.
- Plugin DLL name must start with `MB_`.
- MusicBee runs as a 32-bit process, so build/use x86.
- OpenAI-compatible model endpoint such as LM Studio, Ollama-compatible gateway, or an online provider.

## Build

From the repository root:

```powershell
& C:\Windows\Microsoft.NET\Framework\v4.0.30319\msbuild.exe CSharpDll.sln /p:Configuration=Release /p:Platform=x86 /v:m
```

Output:

```text
bin\x86\Release\MB_AI_Agent.dll
```

## Install

Close MusicBee, then run PowerShell as Administrator:

```powershell
cd "C:\Users\timas\source\repos\MusicBee AI Agent Plugin"
.\install-plugin.ps1
```

Start MusicBee again.

Menu items:

- `Tools -> MusicBee AI Agent - Open Chat`
- `Tools -> MusicBee AI Agent - Settings`

## LM Studio example

Settings:

- Base URL: `http://localhost:1234/v1`
- API Key: empty
- Model: model id shown in LM Studio
- Small local model mode: enabled for small local models

For LAN testing, use the host IP:

```text
http://192.168.x.x:1234/v1
```

## Safety model

The LLM never controls MusicBee directly.

Allowed write actions:

- `create_playlist`
- `queue_tracks_last`
- `queue_tracks_next`
- `play_track_now`

Forbidden in MVP:

- delete files;
- delete playlists;
- overwrite user playlists;
- clear queue;
- bulk edit tags;
- commit tags to files.

All write actions require preview and confirmation.
