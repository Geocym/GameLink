# GameLink v0.2.1 — Steam discovery test
Extract to a new directory and open GameLink.sln. Existing AppData settings are reused.
Build with .NET 10 on Windows. No API key or additional packages required.
Steam monitoring starts automatically; disable it in Settings if desired.
The old manual process monitor remains separate; leave it off for these tests.
Enable generic routing or configure the new Game lifecycle action and specific routing.

## Tests
1. Start Steam, then GameLink. Check installation, active account, library and installed app counts.
2. Launch a Steam game. Within about 3 seconds expect game.started in the log and Streamer.bot.
3. Close it. Expect game.stopped with the same gameId and gameName.
4. Try a game on a secondary drive. Launch an ordinary non-Steam program: no Steam event expected.
5. Start GameLink while a game is already running: status should show it, but no synthetic launch alert.
6. Start GameLink before Steam: account and catalogue refresh within about 30 seconds.
7. Disable Steam monitoring in Settings and re-enable it; existing games are baselined again.

Arguments: eventType, platform=Steam, gameName, gameId, steamAppId, eventId, timestamp,
eventJson, testMode=false, detectionSource=steam.registry. processName is empty intentionally.

## Limitations
Running detection uses undocumented HKCU Steam Apps Running values plus installed manifest matching
and Steam client process presence; it does not validate each game's executable. Non-Steam shortcuts,
streamed games, and games with missing/stale registry flags may be missed. Installed app counts
can include tools/DLC with manifests and directories. Catalogue and account refresh every 30 seconds;
running state every 3 seconds. No file watcher yet. No achievement detection yet.
Existing WebSocket delivery is unchanged except for lifecycle routing; failed events are logged,
not durably queued. Password storage is unchanged (plaintext). No Windows compilation or runtime
testing was possible in the authoring environment.
