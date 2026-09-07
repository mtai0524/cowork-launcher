# Cowork

A Windows desktop app (C# / WPF) for **managing the programs you have to run every day**: declare
them once, then launch them by hand or let Cowork run them on a schedule — and edit their config
files inside the app, without hunting for paths in Notepad.

```
┌─────────────────────────────────────────────────────────────────────┐
│  Cowork   ▶ Run   ■ Stop   │  Run all   Stop all   Check schedule   │
├──────────────────┬──────────────────────────────────────────────────┤
│ Apps             │ Overview │ Env vars │ Config files │ Schedule…    │
│                  │                                                  │
│ ● DB backup      │  Name        [ DB backup             ]           │
│   Running        │  Program     [ C:\tools\backup.bat   ] [Browse…] │
│   Next: 02:15    │  Arguments   [ --full --verbose      ]           │
│                  │                                                  │
│ ● Morning report │  ☑ No overlapping runs   ☑ Capture output        │
│   Last run…      │                                                  │
└──────────────────┴──────────────────────────────────────────────────┘
```

## What it does

| Area | Features |
|---|---|
| **App management** | Add / edit / delete / duplicate, group, reorder, enable or disable individually, search |
| **Running** | Run one app by hand or "Run all", restart in one click, block overlapping runs, stop on timeout; the result is judged against each app's own list of **success exit codes** (robocopy returning 1 still means the job is done) |
| **Dependencies** | "Only run B after A finishes successfully", or "after A is up" for services; "Run all" orders itself accordingly, and a failed dependency skips what comes after instead of running into thin air |
| **Graceful stop** | Closes the main window for GUI apps, sends **Ctrl+C** to console apps (node, python, .bat), waits out the app's own grace period before killing the whole process tree |
| **Keep alive** | If an app exits or crashes on its own, Cowork restarts it a few seconds later; a per-hour cap keeps it from looping forever on an app that is truly broken |
| **Hang detection** | Probes a TCP port or URL on an interval, and watches output (silent too long, or printing a pattern like `FATAL`); on a hang Cowork stops the app and lets keep-alive / retry handle restarting it |
| **Retry on failure** | A job that ends in failure or times out runs again after N seconds, up to M times; the tray only notifies once the attempts are used up. Pressing Stop cancels the retry |
| **Program discovery** | Scans a folder for `.exe`/`.bat`/`.cmd`/`.ps1` and ranks them by confidence; `.ps1` files are wrapped in `powershell.exe` automatically |
| **Per-app setup** | Command-line arguments, working directory, private environment variables, window style, admin rights |
| **Config file discovery** | Scans the app's folder and works out which files are configuration — including **extensionless** ones like `~/.config/app/config` — scoring each High/Medium/Low and skipping `node_modules`, `bin`, and lock files |
| **Config editing** | Opens JSON / INI / .env / XML / App.config inside Cowork — as a key-value table or as raw source — with automatic backups, and a warning before overwriting a file another tool has changed |
| **Diffs** | Compares what is about to be written against what is on disk, or a `.cowork.bak` against the current file — with a restore button |
| **Schedules** | Fixed times of day, repeating intervals, run-on-startup; filtered by day of week; catch-up runs for missed slots |
| **Machine events** | Run when the machine wakes from sleep, when the screen unlocks, or when the network comes back — independent of the schedule, with a delay and a quiet period to stop double firing |
| **Monitoring** | Live output log, run history with exit codes and durations, logging to file, tray notifications when an app fails |
| **Per-run output** | Every run gets its own log file; clicking a row in the History tab replays exactly that run's output |
| **Outbound alerts** | When an app fails at midnight, get it via webhook (Slack/Discord/Teams), Telegram, or email — sent to every configured channel in parallel, with a quiet period against spam |
| **Background operation** | Minimize to the system tray, start with Windows |
| **Remote management** | Several machines connect out to one web hub: see their status and hit Run / Stop / Restart from a browser, and issue agent tokens from the web — see [docs/06](docs/06-quan-ly-tu-xa.md) |

## Getting started

```bash
dotnet build                                  # build the whole solution
dotnet test                                   # run every test
dotnet run --project src/Cowork.App           # open the app
dotnet run --project src/Cowork.Hub           # remote management hub (set a password in appsettings.json first)
```

Requirements: Windows and the .NET 8 SDK (`dotnet --list-sdks` must show 8.x or newer).

## Layout

```
Cowork.slnx
├─ src/Cowork.Core/     Models, services, config readers and writers — no WPF dependency
│   ├─ Models/          ManagedApp, ScheduleRule, ConfigFileRef, AppRunRecord…
│   ├─ Configuration/   JsonConfigEditor, IniConfigEditor, XmlConfigEditor, ConfigFileScanner, ProgramScanner…
│   ├─ Services/        ProcessManager, DailyScheduler, KeepAliveSupervisor, RetrySupervisor, HealthMonitor, RunQueue, SystemTriggerSupervisor, LogPruner, JsonWorkspaceStore…
│   └─ Validation/      AppValidator
├─ src/Cowork.App/      WPF, MVVM (CommunityToolkit.Mvvm)
│   ├─ ViewModels/      MainViewModel, AppViewModel, ConfigFileViewModel, ScanConfigViewModel, ScanProgramViewModel…
│   ├─ Converters/      Binding converters
│   ├─ Localization/    Multi-language labels for XAML
│   └─ Themes/          Control styles and four colour palettes
├─ src/Cowork.Remote/   Data contracts, machine registry, SignalR client — shared by agent and hub, no WPF or ASP.NET
├─ src/Cowork.Hub/      Remote management hub: ASP.NET Core + Blazor Server, runs on Linux too
├─ tests/Cowork.Tests/  xUnit — schedules, config editors, storage, real process launches, in-process hub
└─ docs/                Detailed documentation (in Vietnamese)
```

Keeping `Cowork.Core` free of WPF is deliberate: every decision — when a run is due, what value to
write into a config file — is testable without standing up a window.

## User data

Lives in `%APPDATA%\Cowork`:

| File | Contents |
|---|---|
| `workspace.json` | The app list and settings. Back this file up and you have backed up everything. |
| `history.json` | Run history |
| `backups\workspace-YYYYMMDD.json` | A snapshot of the workspace taken at the start of each day; the 10 most recent are kept |
| `logs\cowork-YYYYMMDD.log` | Cowork's own activity log |
| `logs\run-YYYYMMDD-<run id>.log` | The full output of **a single run**; files older than the retention set in Settings are deleted automatically |

## Documentation

The documents themselves are written in Vietnamese.

| Document | Contents |
|---|---|
| [docs/01-tong-quan.md](docs/01-tong-quan.md) | The problem, the scope, the concepts |
| [docs/02-kien-truc.md](docs/02-kien-truc.md) | Architecture, data flow, design decisions |
| [docs/03-mo-hinh-du-lieu.md](docs/03-mo-hinh-du-lieu.md) | The model and the `workspace.json` schema |
| [docs/04-huong-dan-su-dung.md](docs/04-huong-dan-su-dung.md) | A walkthrough for each situation |
| [docs/05-lo-trinh.md](docs/05-lo-trinh.md) | Current limits and where this is going |
| [docs/06-quan-ly-tu-xa.md](docs/06-quan-ly-tu-xa.md) | Remote management: the hub, agent tokens, deployment |
