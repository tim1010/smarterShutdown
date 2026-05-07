# Smarter Shutdown

A lightweight Windows agent that enforces a daily shutdown schedule on Intune-managed workstations.
IT admins configure the schedule in Microsoft Intune using a custom ADMX template. The agent reads
policy from the Windows Registry (written by Intune via MDM) and shuts down, hibernates, or sleeps
the device at the configured time — with user warnings, idle detection, and Teams call deferral.

Full project specification: `docs/SmarterShutdown-Spec.docx`

---

## Solution Structure

```
SmarterShutdown.sln
├── src/
│   ├── SmarterShutdown.Core         Shared models, interfaces, engines (no UI, no Win32 P/Invoke UI)
│   │   ├── Models/                  PolicySettings.cs — typed model for all 16 registry values
│   │   ├── Policy/                  PolicyReader.cs — reads HKLM\SOFTWARE\Policies\SmarterShutdown\
│   │   ├── Scheduler/               SchedulerEngine.cs — calculates next DateTime from policy
│   │   ├── Shutdown/                ShutdownExecutor.cs — shutdown / hibernate / sleep via Win32
│   │   ├── Idle/                    IdleDetector.cs — GetLastInputInfo() P/Invoke wrapper
│   │   ├── Teams/                   TeamsDetector.cs — IAudioSessionManager2 COM wrapper
│   │   └── IPC/                     PipeMessage.cs, MessageType.cs — named pipe message contract
│   │
│   ├── SmarterShutdown.Service      Windows Service (Worker Service, runs as SYSTEM)
│   │   ├── Worker.cs                Main hosted service — orchestrates scheduler + IPC server
│   │   └── Program.cs               Host builder, DI registration, Event Log setup
│   │
│   ├── SmarterShutdown.Notifier     Per-user tray application (WPF, auto-started at login)
│   │   ├── Tray/                    TrayIconManager.cs — NotifyIcon, tooltip, context menu
│   │   └── Views/                   CountdownWindow.xaml — shutdown warning dialog with postpone
│   │
│   └── SmarterShutdown.Installer    WiX v4 MSI installer
│
├── tests/
│   └── SmarterShutdown.Tests        xUnit + Moq — unit tests for Core logic
│
├── admx/
│   ├── SmarterShutdown.admx         ADMX policy template (import into Intune once per tenant)
│   └── en-US/
│       └── SmarterShutdown.adml     English language strings for the ADMX
│
└── docs/
    └── SmarterShutdown-Spec.docx    Full project specification
```

---

## Technology Stack

| Layer | Technology |
|---|---|
| Language | C# / .NET 8 |
| Service host | Microsoft.Extensions.Hosting (Worker Service) |
| Registry | Microsoft.Win32.Registry (built-in) |
| Idle detection | P/Invoke → `GetLastInputInfo()` (user32.dll) |
| Teams detection | P/Invoke → `IAudioSessionManager2` COM (audioses.dll) |
| Shutdown/hibernate/sleep | P/Invoke → `InitiateSystemShutdownEx` / `SetSuspendState` |
| Tray UI | WPF + `System.Windows.Forms.NotifyIcon` |
| IPC | Named Pipes (`System.IO.Pipes`) |
| Installer | WiX Toolset v4 (.msi → .intunewin) |
| Testing | xUnit + Moq |
| Logging | Windows Event Log + Serilog |

---

## Registry

All policy values live under one key, written by Intune via MDM:

```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\SmarterShutdown\
```

The Service reads this key on startup and re-polls every 5 minutes without restarting.

### Full policy settings reference

| Registry Value | Type | Description |
|---|---|---|
| `Enabled` | DWORD | Master switch. 1 = enabled. |
| `ShutdownTime` | SZ | 24-hour time string, e.g. `"22:00"` |
| `ActiveDays` | DWORD | Bitmask: Sun=1 Mon=2 Tue=4 Wed=8 Thu=16 Fri=32 Sat=64 |
| `ShutdownAction` | DWORD | 0=Shutdown (default), 1=Hibernate, 2=Sleep |
| `ForceShutdown` | DWORD | 1 = force-close open apps at action time |
| `SkipIfOnBattery` | DWORD | 1 = skip if device is on battery |
| `WarningMinutes` | DWORD | Minutes before action to show warning to active users |
| `WarningMessage` | SZ | Custom message text shown in the countdown popup |
| `AllowPostpone` | DWORD | 1 = user may postpone |
| `PostponeMinutes` | DWORD | Minutes each postpone delays the action |
| `MaxPostpones` | DWORD | Max postpones per cycle (0 = unlimited) |
| `IdleThresholdMinutes` | DWORD | Minutes of inactivity before machine is considered idle (default: 30) |
| `IdleWarningMinutes` | DWORD | Shortened warning period used when machine is idle (default: 2) |
| `TeamsDefer` | DWORD | 1 = defer if active Teams call detected |
| `TeamsDeferMaxMinutes` | DWORD | Max minutes to defer for Teams before proceeding (default: 60) |
| `EventLogLevel` | DWORD | 0=Off 1=Errors 2=Warnings 3=Info (default) |

---

## Named Pipe IPC

Pipe name: `\\.\pipe\SmarterShutdown`

The **Service** is the pipe server (runs as SYSTEM). The **Notifier** is the pipe client (runs in user session). The pipe ACL grants Authenticated Users connect access and denies Network.

Because SYSTEM cannot enumerate user audio sessions, the **Notifier** is responsible for running `TeamsDetector` and reporting results to the Service over the pipe.

### Message types (`MessageType` enum)

| Message | Direction | Meaning |
|---|---|---|
| `ShutdownPending` | Service → Notifier | Countdown started for active user |
| `ShutdownPendingIdle` | Service → Notifier | Countdown started, machine is idle |
| `PostponeRequest` | Notifier → Service | User clicked Postpone |
| `PostponeAck` | Service → Notifier | Postpone accepted, includes new DateTime |
| `SuspendRequest` | Notifier → Service | Local admin clicked Suspend Tonight |
| `SuspendAck` | Service → Notifier | Suspension confirmed |
| `DeferredTeams` | Service → Notifier | Shutdown deferred due to Teams call |
| `TeamsCallStatus` | Notifier → Service | Notifier reports current Teams call state |
| `PolicyRefreshed` | Service → Notifier | Policy re-read, update tray tooltip |

---

## Key Behaviours

### Shutdown flow (active user)
1. At T-minus-`WarningMinutes`: Service checks Teams deferral → if deferred, wait and recheck every 2 min up to `TeamsDeferMaxMinutes`
2. Service checks idle state via `IdleDetector`
3. Service sends `ShutdownPending` (active) or `ShutdownPendingIdle` (idle) over pipe
4. Notifier shows countdown popup; idle path uses `IdleWarningMinutes` and auto-proceeds
5. User may click Postpone (if `AllowPostpone=1`, up to `MaxPostpones` times)
6. At T=0: Service calls `ShutdownExecutor` with the configured `ShutdownAction`

### Admin suspend
- Right-click tray icon → **Suspend tonight's shutdown**
- Only visible when the Notifier process is running as a local administrator
- Writes Event ID 1010 to the Windows Application event log (source: `SmarterShutdown`)

### Policy refresh
- Service re-reads the registry key every 5 minutes
- If `Enabled` switches to 0, any pending countdown is cancelled and the pipe sends `PolicyRefreshed`
- If the registry key is deleted (policy removed from Intune), the Service disables gracefully

---

## Event Log

Source name: `SmarterShutdown`  
Log: Windows Application event log

| Event ID | Level | Meaning |
|---|---|---|
| 1000 | Info | Service started, next action scheduled at {DateTime} |
| 1001 | Info | Shutdown action executed ({Shutdown\|Hibernate\|Sleep}) |
| 1002 | Info | Action postponed by user, rescheduled to {DateTime} |
| 1003 | Info | Action deferred — Teams call in progress |
| 1004 | Info | Policy refreshed — next action at {DateTime} |
| 1005 | Warning | Teams deferral limit reached, proceeding with action |
| 1006 | Warning | Action skipped — device on battery |
| 1007 | Warning | No active days configured — scheduling disabled |
| 1008 | Error | Registry read failed: {message} |
| 1009 | Error | Shutdown execution failed: {message} |
| 1010 | Warning | Action suspended by local admin {username} |

---

## Build

```bash
# Restore and build everything
dotnet build SmarterShutdown.sln

# Run tests
dotnet test tests/SmarterShutdown.Tests

# Publish Service (self-contained, Windows x64)
dotnet publish src/SmarterShutdown.Service -c Release -r win-x64 --self-contained false

# Publish Notifier
dotnet publish src/SmarterShutdown.Notifier -c Release -r win-x64 --self-contained false

# Build MSI (requires WiX v4 toolset)
dotnet build src/SmarterShutdown.Installer -c Release

# Package for Intune (requires Win32 Content Prep Tool)
IntuneWinAppUtil.exe -c <output_folder> -s SmarterShutdown.msi -o <output_folder>
```

---

## Development Notes

- **Target framework:** `net8.0-windows` (required for WPF and Windows-specific APIs)
- **Service account:** LocalSystem — do not change; required for registry and shutdown APIs
- **Notifier startup:** Installed via `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` (applies to all users); the MSI sets this key
- **TeamsDetector runs in Notifier, not Service** — SYSTEM cannot access user-session audio sessions; the Notifier performs the COM enumeration and reports over the pipe
- **ADMX namespace is final:** `SmarterShutdown` — changing it after any Intune deployment breaks existing Configuration Profiles
- **Code signing:** Dev builds are unsigned. Production builds must be EV-signed before fleet deployment (SmartScreen will block unsigned installs)
- **Minimum OS:** Windows 10 21H2 (19044) and Windows 11; LTSC variants out of scope for v1

---

## ADMX Deployment

1. In Intune: **Devices → Configuration → Import ADMX**
2. Upload `admx/SmarterShutdown.admx`, then `admx/en-US/SmarterShutdown.adml`
3. Create a **Configuration Profile** → **Templates → Imported Administrative templates**
4. Find category **SmarterShutdown → Shutdown Scheduling** and configure settings
5. Assign profile to device groups

The namespace (`SmarterShutdown`) and registry path (`SOFTWARE\Policies\SmarterShutdown`) are fixed — do not change them after a profile has been deployed.
