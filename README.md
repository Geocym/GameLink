# GameLink

**Version 0.3.2 — Windows / .NET 10 / WinForms**

GameLink is a local-first gaming companion and streaming middleware application. It detects Steam game sessions and achievements, enriches them with available metadata, and forwards events to Streamer.bot over WebSocket. It also provides a searchable achievement library, dashboard, diagnostics and persistent event history.

This source release includes a self-contained Windows x64 publishing profile and ZIP packaging script. See [distribution instructions](DISTRIBUTION.md). Version 0.3.2 requires Windows validation.

## Requirements and build

- Windows with the .NET 10 SDK for building from source.
- Visual Studio with the .NET desktop development workload (optional if using the CLI).
- Steam installed and signed in for Steam monitoring; local achievement caches must be available.
- Streamer.bot with its WebSocket server enabled for event delivery. GameLink can browse local achievements without it.
- Internet access is optional for public rarity and artwork enrichment. No Steam API key, additional account, administrator access or database server is required.

Open `GameLink.sln` in Visual Studio, restore NuGet packages and build, or run from the repository root:

```powershell
dotnet restore GameLink.sln
dotnet build GameLink.sln -c Release
dotnet run --project GameLink/GameLink.csproj -c Release
```

Dependencies: `Microsoft.Data.Sqlite` 10.0.0 and `SQLitePCLRaw.lib.e_sqlite3` 2.1.14. SQLite is embedded; its native runtime is supplied by NuGet. A framework-dependent build needs the .NET 10 Windows Desktop runtime; the self-contained publish profile bundles it. Portable data mode is available; an installer is deferred.

## Quick start

1. In Streamer.bot enable the WebSocket server and note its address, port and optional authentication password.
2. Create an action named `Game Monitor - Receive Event` (or choose an existing action). Use a harmless action while testing.
3. Launch GameLink, open Settings → Streamer.bot and enter the URL (default `ws://127.0.0.1:8080/`) and password if required.
4. Under Event routing, enable generic routing and enter the action name, or use Browse when connected. Save changes. Connect using the main toolbar if needed; startup and retry also attempt connection automatically.
5. Use Diagnostics → Send test event. Check the Streamer.bot action history and GameLink Event history. The synthetic event has `testMode=true`.
6. Launch a Steam game and earn an achievement. Steam monitoring starts automatically when enabled. Existing running games are baselined without launch alerts.

GameLink is the WebSocket **client**, Streamer.bot is the **server**. `DoAction` sends events; `GetActions` populates the action picker. Acknowledgment means the request was accepted, not that all sub-actions completed. Use a trusted local connection; the default `ws://` is unencrypted.

## Configuration reference

| Settings category | Options / defaults |
|---|---|
| Appearance | System (default), Light, Dark |
| Streamer.bot | URL above, optional password, retry interval 15 seconds (5–300) |
| Event routing | Generic enabled, specific disabled; generic action above; achievement, game lifecycle and telemetry destinations initially blank |
| Platforms | Steam enabled; offline recovery Disabled; optional comma-separated process names |
| Achievements | Public metadata enabled; Uncommon ≤25%, Rare ≤10%, Epic ≤5%, Legendary ≤1% |
| Logging | Persistent logging enabled; Information minimum; 2 MB per file, 4 retained archives |
| Storage | History enabled; 90 days, 50,000 events; storage usage and open data folder |

Apply saves without closing; Save & Close saves and closes; Cancel discards unapplied changes. Browse stores an action GUID and displays its name; opening Event routing while connected refreshes linked names. Manual editing removes the previous link. Generic and specific routing may both be enabled; identical target strings are deduplicated, but a name and GUID referring to the same action may still deliver twice. Specific achievement routing covers all `achievement.*` events; game lifecycle covers Steam start, stop and session summary. The telemetry destination is reserved for future use.

Optional Windows process monitoring uses explicit executable names (comma-separated, with or without `.exe`) and the Start game monitor button. It reports process IDs, not Steam AppIDs; these process events currently use generic routing. No process memory is read.

### Offline achievement recovery

Settings → Platforms → Offline achievement recovery:

- **Disabled:** baseline local progress without sending recovery alerts.
- **Individual events:** send each recovered `achievement.unlocked` and any completion event, as in earlier releases.
- **Summary per game:** send one `achievement.recovered_summary` per game per recovery scan, even if only one achievement was recovered. A recovered completion is represented in the summary rather than a separate completion alert. Live unlocks and live completion events remain unchanged.

Existing enabled recovery settings migrate to Individual events; disabled settings remain disabled. Recovery compares account-specific saved snapshots against local Steam caches on the initial scan of an account context. First-seen caches are baselined; missing caches or timestamps can prevent recovery. Changes made after that initial scan do not retroactively recover already-baselined achievements. Summary mode records the summary in event history, not separate individual delivery records.

## Event reference

| Event type | Meaning |
|---|---|
| `game.started` / `game.stopped` | Steam game lifecycle |
| `game.session_summary` | Monitored session duration and achievement counts |
| `achievement.unlocked` | Live or individually recovered achievement; also used by synthetic test |
| `achievement.game_completed` | All known achievements unlocked |
| `achievement.recovered_summary` | Offline recovery batch for one game |
| `game.process.started` / `game.process.stopped` | Explicit Windows process monitor |

Every DoAction request includes `eventType`, `eventId`, `platform`, `gameName`, `gameId`, `processName`, `timestamp`, `eventJson`, plus event-specific data flattened into arguments. `eventJson` contains the canonical object (`event`, `eventId`, `platform`, `timestamp`, `game`, `data`). Timestamps are ISO 8601; event timestamp is detection time, not necessarily actual unlock time.

### Achievement and session arguments

Availability varies by event and local/online metadata. Do not assume optional fields exist.

- Identity: `steamAppId`, `achievementId`, `achievementName`, `achievementDescription`, `achievementMetadataSource`, `detectionSource`, `testMode`.
- Artwork: `achievementIcon`, `achievementIconLocked`, `achievementIconLocalPath`, `gameStoreUrl`, `gameHeaderImage`, `gameHeaderLocalPath` (absolute cached header path, when available).
- Rarity: `achievementRarity` (global unlock percentage), `achievementRarityLabel`, `isRareAchievement`, `isHiddenAchievement`.
- Progress: `achievementsTotal`, `achievementsUnlocked`, `achievementsRemaining`, `achievementCompletionPercentage`, `isGameCompleted`, `isFirstAchievement`.
- Timing: `unlockTimestamp`, `isRecoveredAchievement`, `gameSessionDurationSeconds`, `sessionAchievementsUnlocked`, `sessionRareAchievementsUnlocked`. Recovered unlocks are excluded from live session counts.
- Delivery: `eventQueuedTimestamp`, `eventDeliveredTimestamp` (send attempt time), `eventDeliveryAttempts`, `eventDeliveryDelaySeconds`, `isDelayedEvent`, `isRecoveredEvent` (queue restored after restart, distinct from offline achievement recovery).

### Summary arguments (0.2.7.1)

In addition to common event and available game progress fields:

| Argument | Type / meaning |
|---|---|
| `recoveredAchievementCount` | Integer, number of recovered unlocks |
| `recoveredAchievementNames` | String, newline-separated names |
| `recoveredAchievementsJson` | JSON string containing an array of individual achievement data objects, including original unlock timestamps and available metadata |
| `recoveredGameCompleted` | Boolean, completion detected during this recovery |
| `isRecoveredAchievement` | Boolean, true |

For example, a Streamer.bot action can branch on `eventType == achievement.recovered_summary` and use `%gameName%`, `%recoveredAchievementCount%` and `%recoveredAchievementNames%` in an alert. Parse `recoveredAchievementsJson` in C# if individual metadata is needed. Existing actions that assume every achievement event has `achievementName` should branch on event type before enabling summary mode.

## Application pages

- Dashboard: running Steam games, session timers, completion progress, recent human-readable activity and shortcuts to game achievements. Statistics are current application session / bounded recent activity, not lifetime totals.
- Achievements: game-first artwork cards, progress, search, filters, sorting, virtualised achievement cards, copy details and temporary spoiler reveal/hide controls. Hidden locked achievements are concealed by default.
- Event history: SQLite-backed search (up to 500 results), JSON inspection/copy, delivery outcomes and clear history.
- Platforms: Steam discovery and account status.
- Diagnostics: colour-coded severity/category filters and test event.
- Settings: separate themed window. Close/minimise the main window to hide to tray; use tray Exit to actually stop monitoring.

## Local data and privacy

Default root: `%AppData%\GameLink`. Back up this folder while GameLink is exited before upgrading.

| Path | Purpose |
|---|---|
| `settings.json` | Configuration; password encrypted with Windows DPAPI for current user |
| `history.db` | SQLite event records and delivery attempts |
| `history/` | Account-specific achievement snapshots (not the event database) |
| `queue/` | Pending events and destination references |
| `rejected/` | Explicitly rejected destinations and payloads |
| `metadata/` | Cached global rarity |
| `artwork/` | Cached game and achievement images |
| `logs/` | Rotating plain-text diagnostic logs |

History retention never deletes pending queue entries. Clear history compacts the database; pending events can reappear in history when delivery is attempted. Disabling history stops new persistent records, not deletion of old records. Logs rotate on subsequent writes; artwork and rejected files do not yet have automatic retention. Passwords may need re-entry after moving to another Windows account or machine. Event payloads and logs can contain account/game activity; review before sharing.

GameLink reads local Steam registry/library/cache data. Optional enrichment requests public Steam rarity and artwork endpoints; no Steam credentials are requested. Events are sent to the configured Streamer.bot endpoint.

## Reliability and troubleshooting

- Connection unavailable: check WebSocket server, address, password and Diagnostics. Queued events retry automatically; destinations are captured when queued.
- Rejected action: verify the action exists and routing is correct. Rejections are archived separately; automatic/manual replay UI is not yet implemented.
- Missing achievement: verify Steam has written its local cache and schema. Newly seen games are baselined. Recovery requires a prior snapshot; recently closed games are monitored for about two minutes.
- Missing artwork or rarity: enable online metadata, check network access and cache diagnostics. Missing data is not equivalent to zero rarity.
- Delivery is at-least-once, not exactly-once. An acknowledgment lost after execution can result in duplicate alerts; use `eventId` for deduplication where appropriate. Snapshot persistence precedes queue persistence, so an abrupt crash during enrichment can lose a newly detected event. History writes are best-effort and synchronous.
- Large recovery batches still enrich individual achievements before summarising; initial uncached artwork may delay delivery.
- Native scrollbars and some input controls may retain Windows styling. Xbox, memory telemetry, startup registration, portable data mode, rejected replay and historical dashboard statistics are not yet implemented.

## Changelog

| Version | Changes |
|---|---|
| 0.1 | WinForms/tray foundation, WebSocket routing and explicit process monitor |
| 0.2.1 | Automatic Steam discovery and lifecycle events |
| 0.2.2 | Steam cache diagnostics |
| 0.2.2.1 | Binary achievement detection, baselines and WebSocket keepalive |
| 0.2.3 | Rarity, artwork, progress, session and completion metadata |
| 0.2.3.1 | Artwork download/cache fixes and diagnostic test |
| 0.2.4 | Persistent queue, snapshots, rarity cache and log rotation |
| 0.2.4.1 | Optional offline recovery, DPAPI, retry settings and rejected destinations |
| 0.2.5 | Dashboard, achievement browser and session event history |
| 0.2.5.1 | Game-first virtualised cards, artwork and readable activity |
| 0.2.5.2 | Artwork cache stability, in-place reveal and copy |
| 0.2.6 | Themes, action discovery and individual spoiler concealment |
| 0.2.6.1 | Standalone settings, themed navigation, action names/GUIDs, dashboard shortcuts |
| 0.2.7 | SQLite history, structured diagnostics, retention, rarity labels and dark title bars |
| 0.2.7.1 | Offline recovery summary mode and updated documentation |

## Roadmap

0.2.8: startup/tray improvements, branding, portable data mode, historical statistics and storage refinements. 0.2.9: distribution and beta preparation. 0.3: Xbox integration. Later: configurable game profiles and permitted read-only telemetry. These are plans, not implemented features.

## References and testing

- [Streamer.bot WebSocket requests](https://docs.streamer.bot/api/websocket/requests)
- [Streamer.bot authentication](https://docs.streamer.bot/api/websocket/guide/authentication)
- `V0.2.7.1-TESTING.md` for this release; earlier testing notes are retained in the source archive.

## v0.2.8 — Startup and portable operation

General settings include start with Windows (current-user Run registry entry), start minimised, automatic connection and monitoring, close to tray and connection-loss notifications. Startup registration is applied when saving settings; disable it before moving the executable. The tray menu supports pause/resume monitoring and Settings. Pausing stops detection, not queued delivery; resuming can start a new detection session.

For portable operation, exit GameLink and create an empty `portable.flag` beside GameLink.exe. The application then uses `Data` beside the executable for all GameLink data. No automatic migration occurs. To migrate, close GameLink and copy the contents of `%AppData%\GameLink` into `Data` (including any SQLite sidecar files). Do not run two copies concurrently. Use a writable folder. Remove the flag while closed to return to standard mode. Steam itself is still discovered on the host PC. Re-enter the encrypted password after moving to another Windows account or computer.

The dashboard adds seven-day recorded Steam activity totals (recovery events counted on detection date, completed sessions counted on completion date). Event History supports previous/next pages of 500 results and JSON export of the displayed page. These are not lifetime Steam statistics. Rejected replay, cache cleanup, detailed storage breakdown and dark scrollbars remain planned.

## v0.2.8.1 — Data mode and connection reliability

- Settings is shared between the main window and tray menu; only one settings dialog can be opened.
- The Settings footer allows selection of Standard or Portable storage. Select whether to copy current data or use destination data, then Apply or Save & Close. Exit completely and relaunch to apply. Selecting the active mode and saving cancels a pending change.
- Copying stages files and checks the copied SQLite database before activating the new mode. Existing destination data requires confirmation and is renamed to a sibling `.backup-*` directory. Source data is preserved. Destination changes after confirmation abort migration. The executable directory must be writable to schedule a change. Failed requests are renamed to `migration.pending.json.failed-*` when possible; review Diagnostics and schedule again. Interrupted copies may leave staging or backup directories; do not delete these until you verify your data. Historical payloads retain original artwork paths; queued and rejected payload artwork paths are rebased during copying. Old inactive data can contain stale snapshots and pending events; using it may cause duplicate alerts.
- An idle connection is checked every 30 seconds with a correlated `GetInfo` request and five-second response timeout. Busy connections skip checks. Reconnection follows the existing retry interval. Unexpected shutdowns may not be detected immediately; pending events remain queued. A correlated rejection also establishes responsiveness.

### v0.2.9 — Reliability foundation

- Steam scan journal persists detected events and snapshot before snapshot replacement. Interrupted scans recover when monitoring resumes; stable event IDs and durable queue receipts prevent repeat enqueue after a completed handoff.
- Delivery receipts retain accepted destinations independently of history retention for new deliveries. Replay keeps its original parent event; replacement queue entries retain the selected rejected filenames for interrupted cleanup.
- Migration recovery journal protects the destination replacement phase; interrupted initial staging may leave a staging directory.
- Diagnostics adds an allowlisted support ZIP and local unhandled exception reports. Raw logs, settings and event payloads are excluded from the ZIP. Crash reports are local and may contain sensitive details.
- Storage size scanning and maintenance run on background tasks. SQLite contention can still affect other database operations.
- See [v0.2.9 testing](V0.2.9-TESTING.md) for recovery tests and limitations. Delivery remains at least once; receipts are retained without automatic pruning. No new dependencies.

### v0.2.8.4 — Replay routing hotfix

- Replay resolves current routes and generates new requests, retaining the original event ID and timestamp.
- Optional skipping of accepted destinations uses retained delivery history; missing history or changed action references can cause duplicates.
- Replay metadata: `isReplayedEvent`, `eventReplayCount`, `eventReplayTimestamp`, `originalEventId`.
- Fixed achievement alphabetical sorting method reference.

### v0.2.8.3 — Build and dependency hotfix

- Corrected SettingsForm constructor accessibility.
- Renamed the achievement name helper to avoid hiding Control.Name.
- Removed redundant ProtectedData package reference.
- Pinned native SQLite dependency to 2.1.14 to address GHSA-2m69-gcr7-jv3q.
- Restore, build and runtime verification required.

### v0.2.8.2 — History and storage management

Event History adds a latest-per-destination delivery status filter and full filtered JSON export. Rejected events can be retried or dismissed through a dedicated dialog, including records retained beyond history retention. Retry uses the original rejected destination only; fix that destination before replay. A pending event with the same ID must finish before another destination can be retried. Manual replay can trigger live alerts and retains at-least-once delivery semantics. Storage settings add a usage breakdown, artwork cache cleanup, persistent log cleanup and SQLite compaction. Pending events and achievement snapshots are protected. Existing crash-consistency limitations remain; cleanup and export are synchronous and artwork may be recreated by active downloads.

### v0.2.9.1 — First-run experience

- Optional six-step setup for installations without settings.json in the active data location. Existing settings are preserved.
- Standard/portable selection, Steam discovery, WebSocket authentication and action discovery, generic routing and startup preferences. Skip saves defaults; closing without finishing exits setup.
- `gameHeaderLocalPath` is refreshed from the active artwork cache when queued, replayed and delivered. No extra network request; omitted if missing or invalid. Existing remote URLs remain unchanged.
- See [testing guide](V0.2.9.1-TESTING.md).

### v0.3.0 — RetroAchievements API foundation

- Opt-in provider, encrypted API key, independent polling, account-scoped snapshots and existing event routing.
- API-only; emulator lifecycle and platform-aware achievement browser are deferred. See [testing](V0.3.0-TESTING.md).

### v0.2.9.2 — Distribution and header artwork

- Steam lifecycle and other Steam events receive the remote header URL and cached local path when available. Delivery can populate missing headers with online metadata enabled, independently of dashboard rendering. Bounded CDN attempts and a two-minute retry cooldown protect delivery.
- Windows x64 self-contained single-file publish profile and ZIP packaging script. See [distribution](DISTRIBUTION.md) and [testing](V0.2.9.2-TESTING.md).

### v0.3.1 — Local RetroAchievements assistance
- Optional local monitoring, process-based installation discovery and explicit folder overrides. API-only remains available.
- RetroArch log lifecycle and award parsing, RASnes9x award hints, bounded confirmation retries, session events and artwork. Session summary uses the existing `game.session_summary` event.
- See [v0.3.1 testing](V0.3.1-TESTING.md) for limitations and validation steps.

### v0.3.2 — Extended RA progression

Cached artwork is used directly in recent activity. Adds mode counts, upgrade hints, estimated mode-specific rarity and experimental all-achievement set milestones. See [testing and limitations](V0.3.2-TESTING.md).

## v0.3.3

Multi-platform dashboard, visited-set RA achievement cache, platform/console filters, separate mode progression, weekly statistics and rarity display fixes. See V0.3.3-TESTING.md for scope and limitations.

## v0.3.3.1

Card layout refinement, on-demand RA badge cache repair, library rarity and rarity accents. New RA rarity values use total distinct players rather than mode-specific populations. Existing event history is not rewritten. See V0.3.3.1-TESTING.md.

## v0.3.3.2
Steam library rarity refresh, dynamic active-card container height, RetroArch startup state recovery and remote library banner fallback. See V0.3.3.2-TESTING.md.

## v0.3.4 — RetroAchievements beta candidate

Adds best-effort account-scoped RetroArch session checkpoints, API refresh health and Retry-After handling, unique temporary artwork files, and allowlisted support diagnostic counts. Existing event names and settings remain compatible. See V0.3.4-TESTING.md for limitations and release gates.
