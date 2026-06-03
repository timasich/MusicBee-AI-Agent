# Troubleshooting

## Plugin is enabled but menu item is missing

Close and restart MusicBee.

The plugin registers menu items during `PluginStartup`. Enabling/disabling the plugin in Preferences may not always rebuild the Tools menu.

## Access denied while installing

The MusicBee installation folder under `C:\Program Files (x86)` requires administrator rights.

Run PowerShell as Administrator:

```powershell
cd "C:\Users\timas\source\repos\MusicBee AI Agent Plugin"
.\install-plugin.ps1
```

## LM Studio says unexpected endpoint

Use `/v1` in Base URL:

```text
http://localhost:1234/v1
```

The plugin appends `/chat/completions`.

Wrong:

```text
http://localhost:1234
```

Right:

```text
http://localhost:1234/v1
```

## Changed model is not used

This should be fixed by settings hot reload behavior.

After changing settings, the current chat window is closed so the next opened chat creates a new provider with the latest settings.

If requests still use the previous model:

1. Close the chat window.
2. Reopen it from Tools.
3. Check LM Studio logs.

## JSON parser errors

Small local models can return malformed JSON.

Mitigation:

- enable Small local model mode;
- reduce Max tokens for small models;
- use a stronger instruction-following model;
- check `agent.log` in plugin storage.

## SQLite index is not created

Check:

- MusicBee was restarted after installing the plugin;
- plugin is enabled;
- `sqlite3.dll` exists in the MusicBee install folder;
- plugin persistent storage folder is writable.
- manual rebuild message shows `Snapshot tracks` greater than zero.

The current implementation uses native `sqlite3.dll` through P/Invoke. MusicBee normally ships this DLL.

If manual rebuild starts but no index appears, inspect `index.log` in the plugin persistent storage folder. The rebuild now captures the MusicBee library snapshot on the menu/UI thread, then writes SQLite on a background thread. This avoids calling MusicBee API from the background thread.

## Build warning about .NET targeting pack

MSBuild may warn that reference assemblies for the target framework are missing.

Install the matching .NET Framework Developer Pack / targeting pack on the development machine to remove the warning.
