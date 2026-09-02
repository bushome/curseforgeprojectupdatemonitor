# CurseForge Update Monitor

Polls the CurseForge API for a list of project (mod) IDs on an interval you set in
`config.json`. When any project's latest file changes, it runs your bat file once
(waits for it to finish, then keeps polling). Your bat file is responsible for
everything else — RCON broadcast via `mcrcon.exe`, server restarts, etc.

This application was originally made so I could monitor mod updates I have running on 
my Ark Ascended server cluster but it can be configured to be used for just about anything 
since it calls a bat file on update found. What you define in the bat file decides it's end
use on how you want to utilize update events from CF. The crash monitor part is just cheap
insurance since servers don't always want to restart the first time after a mod update....
for whatever reason.

## Build

Requires the .NET 10 SDK (https://dotnet.microsoft.com/download) on the machine you
build on.

This is a **framework-dependent** build — the output exe is small (a few MB
rather than 60-100MB), but every machine that *runs* it needs the .NET 10
**Runtime** installed — specifically the console app runtime, not the SDK and
not the ASP.NET Core Runtime (that one's for web apps):
https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime
Install it once on each host before running the exe there.

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The exe will be at:
```
bin\Release\net10.0\win-x64\publish\CurseForgeUpdateMonitor.exe
```

Copy that exe to wherever you want it to live on the server, alongside a
`config.json` (see below). `library.json` and `monitor.log` will be created next
to the exe automatically.

> Prefer a bigger exe with no runtime install required on the server? Drop
> `--self-contained false` back to `--self-contained true` and add
> `-p:IncludeNativeLibrariesForSelfExtract=true` — that's the self-contained
> version this project started as.

## Setup

1. Get a CurseForge Core API key:
   - Go to https://console.curseforge.com/ and sign in with your CurseForge/Overwolf
     account (Google sign-in is common here).
   - You may be prompted to create/select an organization first — that's normal,
     just click through it.
   - Go to **Settings > API Keys**, and generate/copy the key listed under
     **"CurseForge Core API"** — that's the one this app needs (`apiKey` in
     `config.json`).
   - If that section isn't available on your account, CurseForge also has a
     formal application process for API access — see
     https://support.curseforge.com/en/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key
     for the application form; approved keys are emailed after review.
2. Copy `config.example.json` to `config.json` next to the exe and fill in:
   - `apiKey` — your CurseForge API key
   - `pollIntervalSeconds` — how often to check (e.g. `300` = every 5 minutes)
   - `batFilePath` — path to your existing broadcast/restart bat file. Can be a
     full path, or a relative one like `..\shutdown.bat` — relative paths are
     resolved against the exe's own folder (not whatever folder happens to be
     "current" when it's launched), so a plain filename works if the bat file
     sits right next to the exe, and `..\` works if it's one folder up.
   - `projectIds` — a single comma-delimited line of the CurseForge mod IDs to
     watch (the number on a mod's CurseForge page, e.g. under "About Project"),
     e.g. `"955333,985370,942249"`. Spaces around commas are fine.
   - `batFileTimeoutSeconds` — optional; only applies to this broadcast/restart
     bat file, not to crash-monitor restarts (see below). After launching it, the
     app waits for it to finish before resuming mod-update polling — this value
     caps how long it'll wait. `0` (the default) means wait indefinitely, no
     matter how long the bat file takes. Set a positive number (seconds) only if
     you want a safety valve in case the bat file ever hangs — if it doesn't
     finish within that time, the app logs a warning and resumes polling anyway,
     leaving the bat file running uncontrolled in the background.
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

**It automatically pauses while your mod-update bat file is running.** Since
that bat file brings servers down and back up on its own, the crash monitor
would otherwise see a server's process disappear mid-restart and try to
"fix" it with its own `run.cmd` call — stepping on the bat file's own restart
sequence. Instead, the crash monitor checks a shared flag each cycle: if the
bat file is currently running, it skips that check entirely (logging it once,
not every cycle) and resumes automatically the moment the bat file exits.

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
- Add one entry per server. `processPath` must point at that specific server's
  exe (from its `run.cmd`) — since every map runs the same `AsaApiLoader.exe`
  binary name from a different folder, matching on the full path is what tells
  Aberration's process apart from every other map's. `runCmdPath` and
  `processPath` can both be relative to the monitor exe's own folder (same rule
  as `batFilePath` above) if that's more convenient than typing full paths. You
  just need to rename them runab.cmd, runast.cmd, runcenter.cmd etc if you want to
  keep everything contained in the same working directory.
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
