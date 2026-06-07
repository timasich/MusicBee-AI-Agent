# MusicBee AI Agent

Experimental alpha MusicBee plugin that adds an AI music assistant.

The model understands the request. The plugin executes only structured, validated, user-confirmed actions.

## Status

Alpha. Expect bugs, rough UI, and changing behavior.

## Features

- Chat with an OpenAI-compatible model.
- Read current track and local library metadata.
- Search tracks, artists, genres, years, and custom fields.
- Create playlists.
- Queue tracks next or last.
- Play one selected track.
- Edit existing playlists through previewed actions.
- Use MusicBrainz/ListenBrainz for similar artists.
- Use Wikipedia for short artist/album/track background.

## Safety And Privacy

- The model does not call MusicBee directly.
- Write actions always show a preview and require confirmation.
- Local audio files are not uploaded.
- The configured model receives chat history and selected music metadata.
- Internet lookups are read-only and only use music metadata needed for the lookup.
- Do not use this alpha with private libraries if you do not trust your configured model endpoint.

## Requirements

- MusicBee for Windows.
- x86 build target.
- OpenAI-compatible chat completions endpoint.

## Build

```powershell
msbuild CSharpDll.sln /p:Configuration=Release /p:Platform=x86
```

Output:

```text
bin\x86\Release\MB_AI_Agent.dll
```

## Install

Copy `MB_AI_Agent.dll` to the MusicBee plugins folder, restart MusicBee, then open:

- `Tools -> MusicBee AI Agent - Open Chat`
- `Tools -> MusicBee AI Agent - Settings`

## Settings

- Base URL, for example `http://localhost:1234/v1`
- API Key, optional for local servers
- Model
- Max tokens
- Timeout seconds

## Docs

- [User guide](docs/user-guide.md)
- [Development notes](docs/development/architecture.md)
- [Manual tests](docs/development/manual-tests.md)

## Donations

- [Ko-fi](https://ko-fi.com/timasich)
- [Donation Alerts](https://www.donationalerts.com/r/timasich)

## License

[MIT](LICENSE).
