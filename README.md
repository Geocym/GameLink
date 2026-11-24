# GameLink 0.1 — source prototype
Windows Forms / .NET 10. No third-party packages required.

## Build on Windows
Install the .NET 10 SDK and optionally Visual Studio with the desktop workload.
Open GameLink.sln or run:

```powershell
dotnet build GameLink.sln -c Release
dotnet run --project GameLink/GameLink.csproj
```

## Configure
Enable Streamer.bot WebSocket server on localhost. Create an action named `Game Monitor - Receive Event`. In GameLink Settings set the URL (default ws://127.0.0.1:8080/), password if enabled, and action names or GUIDs. Password is saved in plaintext in the current user's AppData settings folder; do not reuse a sensitive password. Both generic and specific routes can be enabled, in which case both distinct targets receive events. Test event is a synthetic achievement with testMode=true; use a harmless action for testing. The server acknowledgment means request accepted, not action completed.

For process monitoring enter comma-separated executable names (without path), save and click Start game monitor. Already-running matching processes are reported at first scan. Save settings stops monitoring; start again manually. No other processes are forwarded. Process ID is not a platform game ID.

## Streamer.bot arguments
`eventType`, `eventId`, `platform`, `gameName`, `gameId`, `processName`, `timestamp`, `eventJson`, plus event-specific data fields. Achievement test supplies `achievementName`, `achievementDescription`, `testMode`.

## Current limits
Source only; not compiled or runtime-tested in this environment. Modern Streamer.bot Hello handshake required. Reconnect timer and reconnect on send are implemented, but failed events are logged and not retried or persisted (avoids duplicate effects when acknowledgment is lost). No Steam/Xbox achievement detection or memory telemetry yet. Process monitor is explicit allowlist, not automatic platform identification. No startup registration or automated tests yet. Idle connection status may be stale until next request. Monitor runs on UI timer. Close/minimize hides to tray; exit via tray menu.

## References
https://docs.streamer.bot/api/websocket/requests
https://docs.streamer.bot/api/websocket/guide/authentication
