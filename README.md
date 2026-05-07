# SmarterShutdown

A lightweight Windows agent that enforces a daily shutdown schedule on Intune-managed
workstations. IT admins configure the schedule centrally in Microsoft Intune via a custom
ADMX template; the agent reads policy from the Windows Registry and shuts down, hibernates,
or sleeps the device at the configured time — with user warnings, idle detection, postpones,
and Teams call deferral.

---

## What gets deployed

| Component | Where | Runs as | Purpose |
|---|---|---|---|
| `SmarterShutdown` (Windows Service) | `C:\Program Files\SmarterShutdown\Service\` | LocalSystem | Reads policy, schedules action, executes shutdown / hibernate / sleep |
| `SmarterShutdown.Notifier` (tray app) | `C:\Program Files\SmarterShutdown\Notifier\` | Logged-on user | Tray icon, countdown popup, Postpone button, "Suspend tonight" (admin only), reports idle / Teams state to the Service |
| ADMX template | imported once into Intune | — | Group Policy authoring surface for the 16 policy settings |
| Registry policy | `HKLM\SOFTWARE\Policies\SmarterShutdown` | written by Intune MDM | The Service polls every 5 min |

The Notifier is started for each user via `HKLM\Software\Microsoft\Windows\CurrentVersion\Run`.

---

## Prerequisites

- **Windows 10 21H2 (build 19044) or later, or Windows 11.** LTSC variants are out of scope.
- **.NET 8 Desktop Runtime (x64)** must be present on every target device. The MSI is
  framework-dependent (~850 KB) and will not bundle the runtime. Common deployment patterns:
  - Ship `windowsdesktop-runtime-8.0.x-win-x64.exe` as a separate Win32 app dependency in Intune
    (mark it as a *required dependency* of the SmarterShutdown app).
  - Or rely on the runtime being part of your existing Win32 app catalog.

---

## Manual install (single machine — for testing)

```powershell
# 1. Install (silent, full log)
msiexec /i SmarterShutdown.msi /quiet /l*v install.log

# 2. Confirm the service is up
Get-Service SmarterShutdown   # Status should be Running

# 3. Confirm the Notifier is up (tray icon should be visible too)
Get-Process SmarterShutdown.Notifier
```

The installer's custom action launches the Notifier in the install user's session, so you
get the tray icon immediately without logging out. On Intune (SYSTEM context, no interactive
user), the Notifier appears at the user's next login via the `Run` key.

To uninstall:

```powershell
msiexec /x SmarterShutdown.msi /quiet
```

### Setting policy locally for a smoke test

Without Intune, set policy values directly in the registry:

```powershell
$path = "HKLM:\SOFTWARE\Policies\SmarterShutdown"
New-Item -Path $path -Force | Out-Null

Set-ItemProperty -Path $path -Name Enabled        -Type DWord  -Value 1
Set-ItemProperty -Path $path -Name ShutdownTime   -Type String -Value ((Get-Date).AddMinutes(3).ToString("HH:mm"))
Set-ItemProperty -Path $path -Name ActiveDays     -Type DWord  -Value 127  # all days
Set-ItemProperty -Path $path -Name ShutdownAction -Type DWord  -Value 1    # 1 = Hibernate (safer for testing than 0 = Shutdown)
Set-ItemProperty -Path $path -Name WarningMinutes -Type DWord  -Value 2
Set-ItemProperty -Path $path -Name AllowPostpone  -Type DWord  -Value 1

# The Service caches policy for 5 min — restart it so changes take effect immediately
Restart-Service SmarterShutdown
```

Watch for the warning popup ~1 min after this runs.

---

## Intune deployment

### 1. Build the `.intunewin`

```powershell
IntuneWinAppUtil.exe -c <folder containing SmarterShutdown.msi> `
                    -s SmarterShutdown.msi `
                    -o <output folder>
```

### 2. Create the Win32 app in Intune

- **Install command:** `msiexec /i SmarterShutdown.msi /quiet /norestart`
- **Uninstall command:** `msiexec /x {UPGRADE-CODE-FROM-MSI} /quiet /norestart` (the
  UpgradeCode is `d4c87fd9-5c55-4f01-8d32-a16d0b6e3a72`; or just use the ProductCode discovered by Intune)
- **Detection rule:** MSI product code based on `SmarterShutdown.msi` (Intune detects this automatically when you upload the file)
- **Install behavior:** System
- **Device restart behavior:** No specific action
- **Required dependency:** the .NET 8 Desktop Runtime Win32 app (see Prerequisites)

### 3. Import the ADMX template

1. Devices → Configuration → Import ADMX → upload `admx/SmarterShutdown.admx`, then
   `admx/en-US/SmarterShutdown.adml`
2. Devices → Configuration → Create profile → Templates → **Imported Administrative templates**
3. Find the **SmarterShutdown → Shutdown Scheduling** category and configure the settings
4. Assign the profile to the same device groups as the Win32 app

The ADMX namespace (`SmarterShutdown.Policies.SmarterShutdown`) and registry path
(`HKLM\SOFTWARE\Policies\SmarterShutdown`) are **final** — changing them after deployment
breaks every existing Configuration Profile.

---

## Policy reference

All values live under `HKLM\SOFTWARE\Policies\SmarterShutdown`.

| Value | Type | Default | Description |
|---|---|---|---|
| `Enabled` | DWORD | `0` | Master switch. Set to `1` to enable scheduling. |
| `ShutdownTime` | SZ | `22:00` | 24-hour `HH:mm` string. |
| `ActiveDays` | DWORD | `0` | Bitmask: Sun=1, Mon=2, Tue=4, Wed=8, Thu=16, Fri=32, Sat=64. Sum the bits. (e.g. weekdays = 62, every day = 127). `0` disables scheduling and logs Event 1007. |
| `ShutdownAction` | DWORD | `0` | `0`=Shutdown, `1`=Hibernate, `2`=Sleep. |
| `ForceShutdown` | DWORD | `0` | `1` force-closes apps with unsaved work at action time. |
| `SkipIfOnBattery` | DWORD | `0` | `1` skips the action if the device is on battery (logs Event 1006). |
| `WarningMinutes` | DWORD | `15` | Lead time before action that the warning popup appears. |
| `WarningMessage` | SZ | `""` | Custom message shown in the popup. Empty = built-in default ("Please save your work."). |
| `AllowPostpone` | DWORD | `1` | `1` shows a Postpone button in the popup. |
| `PostponeMinutes` | DWORD | `15` | Minutes each postpone delays the action. |
| `MaxPostpones` | DWORD | `3` | Maximum postpones per cycle. `0` = unlimited. |
| `IdleThresholdMinutes` | DWORD | `30` | After this many minutes of no input, the device is considered idle. |
| `IdleWarningMinutes` | DWORD | `2` | Shorter warning lead time used when the device is idle. |
| `TeamsDefer` | DWORD | `1` | `1` defers the action while a Teams call is active. |
| `TeamsDeferMaxMinutes` | DWORD | `60` | Maximum total minutes to defer for a Teams call before proceeding anyway (logs Event 1005). |
| `EventLogLevel` | DWORD | `3` | `0`=Off, `1`=Errors, `2`=Warnings, `3`=Info. |

Policy changes are picked up by the Service every 5 minutes. To force an immediate re-read:
`Restart-Service SmarterShutdown`.

---

## Monitoring — Event Log

All events are written to **Application** log under source `SmarterShutdown`.

| ID | Level | Meaning |
|---|---|---|
| 1000 | Information | Service started; next action scheduled at `{DateTime}` |
| 1001 | Information | Action executed (`Shutdown` / `Hibernate` / `Sleep`) |
| 1002 | Information | Action postponed by user; rescheduled to `{DateTime}` |
| 1003 | Information | Action deferred — Teams call in progress |
| 1004 | Information | Policy refreshed; next action at `{DateTime}` |
| 1005 | Warning | Teams deferral limit reached; proceeding with action |
| 1006 | Warning | Action skipped — device on battery |
| 1007 | Warning | No active days configured — scheduling disabled |
| 1008 | Error | Registry read failed: `{message}` |
| 1009 | Error | Shutdown execution failed: `{message}` |
| 1010 | Warning | Action suspended by local admin `{username}` |
| 1011 | Information | Warning broadcast (`Active` / `Idle`, `lead={N}min`) sent to `{N}` connected client(s); `idle={N}min` |

Useful one-liner for live triage:

```powershell
Get-WinEvent -LogName Application -ProviderName SmarterShutdown -MaxEvents 20 |
    Format-Table TimeCreated, Id, LevelDisplayName, Message -AutoSize -Wrap
```

---

## Troubleshooting

### The warning popup never appears
First, check Application log for Event 1011 around the expected warning time:

| 1011 says | Meaning |
|---|---|
| `(Active, lead=15min) sent to 1 connected client(s)` | Popup *was* sent and received by a Notifier. Likely hidden behind a lock screen or off-screen — see notes below. |
| `(Idle, lead=2min) sent to 1 connected client(s)` | Idle path fired. Popup appeared with only 2-min lead and no Postpone button — easy to miss if the user wasn't watching. |
| `... sent to 0 connected client(s)` | Service tried to broadcast but no Notifier was connected. The user session never started the tray app (or it crashed). |

If clients = 0:
- Confirm the Notifier process exists: `Get-Process SmarterShutdown.Notifier`
- The `Run` key fires at user login. Fresh installs need the user to log out and back in,
  or start manually: `Start-Process "C:\Program Files\SmarterShutdown\Notifier\SmarterShutdown.Notifier.exe"`

If clients ≥ 1 but popup wasn't visible:
- The popup renders to the user's desktop. If the session was locked (typical after 10-15
  min idle) or display asleep, the popup is there but covered.
- For idle path, this is by design — `IdleWarningMinutes=2` and `PostponeAllowed=false`
  assume nobody is watching. Bump `IdleThresholdMinutes` higher if you don't want the idle
  path to fire during normal AFK breaks.

Other things to confirm:
- The Service is running and reads policy: check Application log for Event 1000 and a recent 1004.
- `_nextAction` is what you expect — Event 1000 / 1004 print it. If it's the day-after rather
  than today, check `ActiveDays` and `ShutdownTime`.

### Shutdown doesn't fire at the scheduled time
1. **Battery skip**: if `SkipIfOnBattery=1` and the device is on battery, the action is
   skipped and Event 1006 is logged.
2. **Teams deferral**: if `TeamsDefer=1` and the Notifier reports an active call, the
   Service defers and logs Event 1003. After `TeamsDeferMaxMinutes` it logs Event 1005
   and fires anyway.
3. **Stale policy**: the Service caches policy for 5 min. After registry edits during
   testing, run `Restart-Service SmarterShutdown`.
4. **Privilege failure**: shutdown requires `SeShutdownPrivilege`. The Service enables
   it explicitly, but if Event 1009 reports `ERROR_NOT_ALL_ASSIGNED (1300)`, the service
   token is missing the privilege — check that the service account is `LocalSystem`
   (`Get-CimInstance Win32_Service -Filter "Name='SmarterShutdown'" | Select-Object StartName`).

### "Suspend tonight's shutdown" menu item missing
The item only appears when the Notifier process is running with administrator rights in
its token. UAC-elevated admin → visible. Standard user → hidden. Standard user in admin
group but not elevated → hidden (this is intentional).

### Service won't start after install
Check Event Log: a config / DI error logs as a generic .NET error during host startup.
Most common cause is the .NET 8 Desktop Runtime being missing. Run
`dotnet --list-runtimes` — you need `Microsoft.WindowsDesktop.App 8.0.x`.

---

## Limitations / known issues

- **Teams call detection** is currently stubbed. The pipe wiring, Service-side deferral
  logic, and Event Log integration are all in place, but `Win32TeamsDetector.IsInCall()`
  always returns `false` until the COM enumeration of `IAudioSessionManager2` capture
  sessions is implemented and validated against real Teams calls. Until then,
  `TeamsDefer=1` is a no-op.
- **Tray icon is a placeholder** (`SystemIcons.Information`). A branded `.ico` should be
  shipped before fleet deployment.
- **MSI is unsigned** in dev builds. Production builds must be EV-signed before
  deployment — Microsoft Defender SmartScreen will block unsigned installers in many
  enterprise configurations.

---

## Building from source

See `CLAUDE.md` for the full architecture and tech-stack reference. Quick build:

```powershell
dotnet test SmarterShutdown.sln                                                  # unit + integration tests
dotnet build src\SmarterShutdown.Installer -c Release                            # produces SmarterShutdown.msi
```

The installer's `BeforeBuild` target publishes the Service and Notifier as framework-dependent
x64 binaries before WiX harvests them, so a single `dotnet build` against the wixproj
produces the MSI.
