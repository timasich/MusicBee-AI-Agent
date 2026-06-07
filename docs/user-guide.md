# User Guide

Status: alpha.

## Setup

1. Build or download `MB_AI_Agent.dll`.
2. Copy it to the MusicBee plugins folder.
3. Restart MusicBee.
4. Open `Tools -> MusicBee AI Agent - Settings`.
5. Set an OpenAI-compatible endpoint, model, token limit, and timeout.

## Use

Open `Tools -> MusicBee AI Agent - Open Chat`.

Example requests:

- Create a 30 minute rock playlist.
- Queue similar tracks after the current song.
- Add 10 random tracks by an artist to an existing playlist.
- Tell me about this artist.
- Suggest similar artists.

## Confirmation

Every write action is previewed first. Confirm only after reviewing the track list.

## Internet

The agent can use:

- MusicBrainz and ListenBrainz for similar artists.
- Wikipedia for short background information.

These tools are read-only.
