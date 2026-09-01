# CurseForge Update Monitor

Polls the CurseForge API for a list of project (mod) IDs on an interval you set in
`config.json`. When any project's latest file changes, it runs your bat file once
(waits for it to finish, then keeps polling). Your bat file is responsible for
everything else — RCON broadcast via `mcrcon.exe`, server restarts, etc.

## Build

Requires the .NET 8 SDK (https://dotnet.microsoft.com/download) on the machine you
build on — the *output* is a self-contained exe that needs nothing installed on
the machine that runs it.

```
dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The exe will be at:
```
bin\Release\net8.0\win-x64\publish\CurseForgeUpdateMonitor.exe
```

Copy that exe to wherever you want it to live on the server, alongside a
`config.json` (see below). `library.json` and `monitor.log` will be created next
to the exe automatically.

## Setup

1. Get a CurseForge Core API key: https://console.curseforge.com/
2. Copy `config.example.json` to `config.json` next to the exe and fill in:
   - `apiKey` — your CurseForge API key
   - `pollIntervalSeconds` — how often to check (e.g. `300` = every 5 minutes)
   - `batFilePath` — full path to your existing broadcast/restart bat file
   - `projectIds` — a single comma-delimited line of the CurseForge mod IDs to
     watch (the number on a mod's CurseForge page, e.g. under "About Project"),
     e.g. `"955333,985370,942249"`. Spaces around commas are fine.
   - `batFileTimeoutSeconds` — optional; `0` means wait as long as it takes
3. Run the exe. On the very first run it records the current file ID for every
   project as a baseline (no bat file triggered) — updates are only detected
   from the second successful check onward.
4. To run it as a background service rather than a console window, wrap it in
   NSSM (https://nssm.cc/) or a scheduled task set to run at startup / on a
   trigger with "repeat every N minutes" if you'd rather not have it loop
   internally.

## How update detection works

Each poll cycle makes a single batched call to CurseForge's `POST /v1/mods`
endpoint with all your project IDs at once (friendlier to rate limits than one
call per project). For each project, it takes the file with the newest
`fileDate` from `latestFiles` and compares its file ID against what's stored in
`library.json`. A different ID = an update.

## Crash monitor (optional)

A separate, independently-scheduled feature — toggle it on in `config.json` under
`crashMonitor`. It has nothing to do with mod updates; it just watches that each
configured server's process is still running, and restarts it via that server's
own `run.cmd` if not.

```json
"crashMonitor": {
  "enabled": true,
  "checkIntervalSeconds": 30,
  "restartCooldownSeconds": 120,
  "servers": [
    {
      "name": "Aberration",
      "processPath": "D:\\servers\\AberrationASM\\ShooterGame\\Binaries\\Win64\\AsaApiLoader.exe",
      "runCmdPath": "D:\\servers\\AberrationASM\\run.cmd"
    }
  ]
}
```

- `enabled` — master on/off switch.
- `checkIntervalSeconds` — how often to check, independent of `pollIntervalSeconds`
  (defaults to 30s — much faster than you'd want for CurseForge polling).
- `restartCooldownSeconds` — minimum time between restart attempts for the same
  server, so a server that's stuck for some other reason (bad config, disk full,
  etc.) doesn't get hammered with restart attempts every 30 seconds.
- Add one entry per server. `processPath` must be the full path to that specific
  server's exe (from its `run.cmd`) — since every map runs the same `AsaApiLoader.exe`
  binary name from a different folder, matching on the full path is what tells
  Aberration's process apart from every other map's.
- On a detected crash it just re-runs `runCmdPath` as-is (same as double-clicking
  it) and logs it — no broadcast bat, no other side effects. Your `run.cmd`'s own
  `start` command handles backgrounding the actual server process, exactly like a
  normal manual restart.
- If `crashMonitor.enabled` is `false` (or omitted), none of this runs at all —
  the app behaves exactly like the mod-update-only version.

## library.json

```json
{
  "projects": {
    "12345": {
      "projectId": 12345,
      "name": "Some Mod",
      "lastFileId": 987654,
      "lastFileName": "SomeMod-1.2.3.zip",
      "lastFileDate": "2026-08-30T12:00:00Z",
      "lastChecked": "2026-09-01T10:05:00Z"
    }
  }
}
```

This schema is a first draft — if it needs to line up with the format your
other tooling already uses, share that example and it's a quick change to
`LibraryEntry` in `Models.cs`.

## Logging

Everything is written to both the console and `monitor.log` (append-only, next
to the exe) — poll results, detected updates, API errors, and bat file exit
codes.
