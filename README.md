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
| **Per-run output** | Every run gets its own self-describing log file — a header with the command, arguments, working directory, trigger and PID, then output tagged by source (`out` / `ERR` / `cowork` for lifecycle events like a stop request or a grace-period kill), then a footer with the exit code, outcome and duration. Environment variable *names* are recorded, never their values. Filter the History tab to one app, or read any run's log from the web hub |
| **Outbound alerts** | When an app fails at midnight, get it via webhook (Slack/Discord/Teams), Telegram, or email — sent to every configured channel in parallel, with a quiet period against spam |
| **Daily news** | A built-in RSS reader: pick your topics — AI, agents, technology, programming, startups, security, repos — and Cowork pulls the public feeds of about thirty outlets into one dated briefing. Mostly foreign press, with a share of the Vietnamese press held back for it — spread down the page rather than clumped at the top, so any screenful keeps the ratio; a quiet day on one side is filled from the other rather than leaving the page short. Duplicates across outlets collapse into one story, no single feed can take over the page, and your own feeds can be added by URL. The **repos** topic reads GitHub rather than the press: the trending boards for discovering something new, and the release feeds of `anthropics/claude-code`, the Model Context Protocol SDKs and a dozen other AI/agent repos for keeping up with what you already use |
| **News on the web** | The hub carries the same briefing and fetches the feeds itself, so it has today’s news even while every machine is off; topics are per-browser |
| **Background operation** | Minimize to the system tray, start with Windows |
| **Remote management** | Several machines connect out to one web hub: see their status, hit Run / Stop / Restart from a browser, take a screenshot of a running app's window, and issue agent tokens from the web — see [docs/06](docs/06-quan-ly-tu-xa.md) |

## Download

Every build lives on the **[releases page](https://github.com/mtai0524/cowork-launcher/releases/latest)**.
Both packages carry their own runtime, so there is no .NET to install first.

| File | For | Size |
|---|---|---|
| `Cowork-<version>-win-x64.msi` | The desktop app, Windows 10 1809 or newer, x64 | ~54 MB |
| `cowork-hub_<version>_amd64.deb` | The hub, Debian / Ubuntu, amd64 | ~34 MB |
| `SHA256SUMS` | Checksums for both, to verify what you downloaded | — |

There is no desktop build for Linux and none for arm64: the app is WPF, which only runs on Windows
x86/x64. Linux gets the hub — the piece that takes connections from your Windows machines and shows
them on a web page.

## Installing

**Windows** — the desktop app:

```powershell
winget install mtai0524.Cowork
```

Per-user, so no UAC prompt. Or, from a downloaded `.msi`:

```powershell
msiexec /i Cowork-1.0.0-win-x64.msi          # with a wizard
msiexec /i Cowork-1.0.0-win-x64.msi /qn      # silently
```

It lands in `%LOCALAPPDATA%\Programs\Cowork`, adds a Start Menu entry, and puts itself on your `PATH`
so `cowork` opens the app from any terminal. Your data lives in `%APPDATA%\Cowork` and uninstalling
leaves it alone.

**Linux** — the hub only:

```bash
curl -LO https://github.com/mtai0524/cowork-launcher/releases/latest/download/cowork-hub_1.0.0_amd64.deb
sudo apt install ./cowork-hub_1.0.0_amd64.deb
sudo nano /etc/cowork-hub/cowork-hub.env    # set the web password
sudo systemctl enable --now cowork-hub
```

Use `apt install ./file.deb` rather than `dpkg -i` — the package needs ICU and OpenSSL, and `apt`
fetches them for you. The hub deliberately does not start on install: it refuses to run while the
web password is still the placeholder, so starting it early would only produce a dead service in
the log.

Full details — what each package writes where, upgrading, uninstalling, building the packages
yourself: [docs/07](docs/07-cai-dat.md).

## Building from source

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
│   ├─ News/            FeedParser, NewsDigest, NewsService, NewsCatalog — RSS in, one dated briefing out
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
├─ packaging/           MSI (WiX) for Windows, .deb for the Linux hub, winget manifests
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
| `news-cache.json` | The stories pulled by the last news refresh, so the panel has something to show the moment you open the app. Safe to delete — it is refetched |
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
| [docs/07-cai-dat.md](docs/07-cai-dat.md) | Installing on Windows and Linux, building the packages, cutting a release |
