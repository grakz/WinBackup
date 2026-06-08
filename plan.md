# WinBackup — WinUI 3 Native App: Implementation Plan

## Vision

A single packaged Windows application (WinUI 3 / Windows App SDK) that replaces the PowerShell scripts with a fast, reliable, native experience. It lives in the system tray, runs automatically, and handles all backup logic with full test coverage.

---

## Feasibility Summary

All core requirements are achievable. Key findings:

| Requirement | Verdict | Notes |
|---|---|---|
| WinUI 3 native app | ✅ Full | C# + .NET 8, Windows App SDK 2.x |
| System tray presence | ✅ Full | Win32 `Shell_NotifyIcon` via CsWin32 P/Invoke |
| Run at startup | ✅ Full | `StartupTask` manifest extension |
| SSD volume dismount (air-gap) | ✅ Full | `DeviceIoControl` + elevated helper EXE pattern |
| Persistent SSD reminder toasts | ✅ Full | `ScheduledToastNotification` + alarm scenario |
| OneDrive file enumeration (no download) | ✅ Full | Check `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` attribute |
| OneDrive cloud-only files | ✅ Full | Hydrate-copy-verify-dehydrate, one file at a time |
| Proton Drive integration | ✅ Full | Treat Proton sync folder as local target (same as now) |
| Unit tests | ✅ Full | xUnit against separated class library |
| E2E UI tests | ✅ Full | WinAppDriver + Appium via UIA accessibility tree |
| MSIX package | ✅ Full | Single-project MSIX template |

---

## Architecture

### Process Model

Two processes are required because volume dismount needs elevation, but elevated processes cannot send toast notifications:

```
┌─────────────────────────────────────────────────┐
│  WinBackup.exe  (non-elevated, main process)    │
│                                                  │
│  ┌──────────┐  ┌──────────┐  ┌───────────────┐  │
│  │ Tray UI  │  │ Settings │  │ Status window │  │
│  └──────────┘  └──────────┘  └───────────────┘  │
│                                                  │
│  ┌──────────────────────────────────────────┐   │
│  │  BackupOrchestrator (background thread)  │   │
│  │  PeriodicTimer → Proton daily check      │   │
│  │  PeriodicTimer → SSD monthly check       │   │
│  └──────────────────────────────────────────┘   │
│                                                  │
│  Toast notifications (AppNotificationManager)    │
└───────────────┬─────────────────────────────────┘
                │ Named pipe (JSON messages)
                ▼
┌─────────────────────────────────────────────────┐
│  WinBackup.Elevated.exe  (admin helper)          │
│                                                  │
│  - Lock volume (FSCTL_LOCK_VOLUME)               │
│  - Dismount filesystem (FSCTL_DISMOUNT_VOLUME)   │
│  - Eject media (IOCTL_STORAGE_EJECT_MEDIA)       │
│  - Remove drive letter (DeleteVolumeMountPoint)  │
│  - Re-mount if backup fails                      │
└─────────────────────────────────────────────────┘
```

The elevated helper is launched via `ShellExecuteEx` with the `runas` verb only when needed (once per backup session), communicates via a named pipe, then exits.

### Solution Structure

```
WinBackup.sln
├── WinBackup/                        # Main WinUI 3 app project (packaged)
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml               # Tray shell window (hidden by default)
│   ├── Views/
│   │   ├── StatusPage.xaml
│   │   └── SettingsPage.xaml
│   ├── TrayIcon.cs                   # Shell_NotifyIcon P/Invoke wrapper
│   ├── NotificationManager.cs        # Toast scheduling + handling
│   └── Package.appxmanifest
│
├── WinBackup.Core/                   # Class library — all business logic (no WinUI dependency)
│   ├── Config/
│   │   ├── BackupConfig.cs           # Config model
│   │   └── ConfigService.cs          # Read/write config.json
│   ├── State/
│   │   ├── BackupState.cs            # State model + history
│   │   └── StateService.cs           # Read/write state.json
│   ├── Backup/
│   │   ├── IBackupEngine.cs
│   │   ├── SsdBackupEngine.cs        # Full + incremental SSD logic
│   │   ├── ProtonBackupEngine.cs     # Incremental Proton logic
│   │   └── BackupOrchestrator.cs     # Scheduling, retry, locking
│   ├── OneDrive/
│   │   └── OneDriveFileEnumerator.cs # Cloud-aware enumeration + hydrate/dehydrate
│   ├── Volume/
│   │   └── VolumeHelper.cs           # SSD detection by label/serial
│   └── Pipes/
│       └── ElevatedHelperProtocol.cs # Named pipe message contracts
│
├── WinBackup.Elevated/               # Elevated helper EXE (console app)
│   ├── Program.cs                    # Named pipe server, executes volume ops
│   └── VolumeOperations.cs           # DeviceIoControl P/Invoke wrappers
│
├── WinBackup.Tests.Unit/             # xUnit unit tests
│   ├── SsdBackupEngineTests.cs
│   ├── ProtonBackupEngineTests.cs
│   ├── OneDriveEnumeratorTests.cs
│   ├── ConfigServiceTests.cs
│   └── StateServiceTests.cs
│
└── WinBackup.Tests.E2E/              # WinAppDriver E2E tests
    ├── AppSession.cs                 # WinAppDriver session setup/teardown
    ├── TrayIconTests.cs
    ├── SettingsTests.cs
    └── StatusTests.cs
```

---

## Key Technical Decisions

### OneDrive Files On Demand

The app uses a single strategy: **Hydrate-Copy-Verify-Dehydrate**, one file at a time.

No Azure account, no app registration, no sign-in flow, no external dependencies. Works entirely offline (local filesystem only).

Sequence per cloud-only file (identified by `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`):
1. Open the file for read — Windows triggers OneDrive to hydrate (download) it into the local placeholder.
2. Stream to backup destination using managed `Stream`-based copy with real-time per-file progress in the UI.
3. Compute SHA-256 of source and destination; abort and log error if they differ.
4. Call `CfSetPinState(CF_PIN_STATE_UNPINNED)` to re-dehydrate the file, restoring it to cloud-only status.

**Trade-off accepted:** Backing up large cloud-only files is slow (limited by OneDrive download speed). This is fine for a personal backup tool running overnight. Disk space required = size of the single largest file being processed, not the whole OneDrive.

### SSD Volume Air-Gap

Sequence handled by the elevated helper:
1. Main app requests backup via named pipe.
2. Elevated helper opens volume handle: `CreateFile("\\.\X:", GENERIC_READ | GENERIC_WRITE, ...)`.
3. `DeviceIoControl` → `FSCTL_LOCK_VOLUME`.
4. Main app performs file copy (runs in main process, no elevation needed for reads/writes).
5. When complete, main app signals helper: proceed with eject.
6. Helper: `FSCTL_DISMOUNT_VOLUME` → `IOCTL_STORAGE_EJECT_MEDIA` → `DeleteVolumeMountPoint`.
7. Helper exits. Drive letter is gone; filesystem is dismounted.

If copy fails, helper re-mounts (`SetVolumeMountPoint`) before exiting so the drive remains accessible.

### SSD Monthly Reminder

Uses `Windows.UI.Notifications.ScheduledToastNotification` (accessible from packaged apps without a WinUI host):
- On the configured monthly date, schedule a `scenario="alarm"` toast.
- Toast has buttons: **"Connect now"** (launches app to foreground) and **"Remind me tomorrow"** (schedules a new toast for the next day).
- If user dismisses without action, the app re-schedules for 3 days later up to 4 times, then logs as "overdue" and changes the tray icon to a warning state.
- Scheduled toasts fire even when the app is not running.

### Proton Drive

No API required. The app writes backup delta files into the configured Proton sync folder (e.g., `%USERPROFILE%\Proton Drive\My files\Backups\YYYY-MM-DD\`). The Proton desktop app handles upload automatically. This is identical to the current PowerShell approach and is the correct design — no Proton SDK needed.

---

## Testing Strategy

### Unit Tests (WinBackup.Tests.Unit)

All logic lives in `WinBackup.Core` (no WinUI dependency), making it fully unit-testable.

Key test areas:
- **Incremental cutoff logic**: Given a state history with N SSD backups, verify the Proton cutoff date is correct for various `lookbackBackups` values, including edge cases (0 SSD backups, 1 SSD backup, missed months).
- **SSD detection**: Given mock volume enumeration results, verify correct SSD identification by label + serial, and graceful handling of label/serial mismatch.
- **File enumeration**: With a mock filesystem, verify that cloud-only files are identified correctly and that enumeration does not trigger hydration.
- **Config/State serialization**: Round-trip serialization tests for config and state JSON.
- **Backup engine logic**: Full/incremental decision logic (year boundary), folder naming, file copy ordering.
- **Free space check**: Pre-backup space validation for OneDrive Strategy A.
- **Named pipe protocol**: Message serialization/deserialization for elevated helper IPC.

Use `Microsoft.Extensions.FileProviders.Abstractions` or an `IFileSystem` interface to abstract the filesystem for unit tests.

### E2E Tests (WinBackup.Tests.E2E)

Using WinAppDriver + Appium, targeting the UIA (UI Automation) accessibility tree:

- **Tray icon presence**: After launch, verify tray icon is visible in notification area.
- **Settings round-trip**: Open settings, change source folder, save, reopen — verify persisted.
- **Status display**: After a simulated backup run, verify status window shows correct last-run time and result.
- **Manual run trigger**: Click "Run Proton backup now" in tray menu, verify status changes to "Running…" then "Success".
- **Toast action**: Simulate SSD reminder toast appearing; click "Remind me tomorrow"; verify a new scheduled toast exists.

E2E tests run against a test configuration pointing to temp folders, not the real user data.

---

## Technology Stack

| Component | Choice |
|---|---|
| Language | C# 12, .NET 8 |
| UI Framework | WinUI 3 (Windows App SDK 2.1.3) |
| Packaging | Single-Project MSIX, self-signed certificate |
| Win32 interop | `Microsoft.Windows.CsWin32` (P/Invoke source generator) |
| OneDrive cloud files | `CfSetPinState` via `Microsoft.Windows.CsWin32` (no Graph API) |
| File copy | Managed `Stream`-based with retry + SHA-256 post-verification |
| Unit tests | xUnit + Moq |
| E2E tests | WinAppDriver + Appium |
| IPC (main ↔ elevated) | Named pipes with JSON messages |
| Logging | `Microsoft.Extensions.Logging` → file sink (`Serilog.Sinks.File`) |
| Config/State | `System.Text.Json` (no external serializer needed) |

---

## Implementation Phases

### Phase 1 — Scaffolding & Core
- Create solution with 5 projects (main app, core lib, elevated helper, unit tests, E2E tests)
- Implement Config and State models + services with full unit test coverage
- Implement `TrayIcon.cs` (Shell_NotifyIcon P/Invoke) and basic tray menu
- Implement startup task registration
- Implement Settings UI (source folders, SSD label/serial, Proton path, schedule times)

### Phase 2 — Backup Engines
- Implement `SsdBackupEngine` (full + incremental logic, managed `Stream`-based copy with retry-on-locked-file, real-time progress reporting)
- Implement post-transfer SHA-256 verification for every copied file (source hash vs destination hash)
- Implement `ProtonBackupEngine` (incremental delta to sync folder, same copy + verify approach)
- Implement `BackupOrchestrator` (scheduling, concurrency guard, logging)
- Full unit test coverage for all backup logic

### Phase 3 — OneDrive Support
- Implement `OneDriveFileEnumerator` (enumerate with cloud attribute detection, no download triggered)
- Implement hydrate-copy-verify-dehydrate sequence (`CfSetPinState` via CsWin32 P/Invoke)
- Show per-file hydration progress in status UI ("Downloading from OneDrive: filename.ext…")
- Unit tests: mock filesystem with mix of local and cloud-only files, verify dehydration called after copy

### Phase 4 — Volume Air-Gap
- Implement `WinBackup.Elevated` helper (named pipe server, DeviceIoControl wrappers)
- Implement `ElevatedHelperProtocol` IPC contract in `WinBackup.Core`
- Integrate elevated helper launch into `SsdBackupEngine`
- Handle helper-launch UAC prompt gracefully (user can decline; backup proceeds without dismount with a warning)

### Phase 5 — Notifications & Reminders
- Implement `NotificationManager` (toast registration in manifest, `AppNotificationManager` setup)
- Implement SSD monthly reminder scheduling (`ScheduledToastNotification`)
- Implement reminder escalation logic (re-schedule on dismiss, tray icon warning state)
- Implement "Remind me tomorrow" and "Connect now" action buttons

### Phase 6 — Status UI & Polish
- Implement Status window (last backup times, type, result, log viewer)
- Implement log file viewer (open folder button)
- Settings validation (path existence, SSD label/serial format)
- Error state handling (backup failure → tray icon change + toast)

### Phase 7 — Testing & Hardening
- Complete unit test suite (target: >90% coverage of `WinBackup.Core`)
- Implement E2E test suite (WinAppDriver setup + all scenarios above)
- Manual end-to-end validation on real hardware (OneDrive, Proton, physical SSD)
- MSIX signing setup: generate self-signed certificate, sign the MSIX, install cert to "Trusted People" store on each target machine (one PowerShell command per machine)

---

## Decisions

All design decisions are locked:

1. **OneDrive integration**: Hydrate-copy-verify-dehydrate only. No Azure AD app registration, no Microsoft Graph API, no sign-in flow. Works for personal OneDrive Family accounts with no Azure account required. Slow for large cloud-only files but acceptable for overnight runs on a private machine with sufficient free disk space.

2. **MSIX distribution**: Plain MSIX download (double-click to install). Self-signed certificate generated once by the developer. On each personal machine, run one PowerShell command to trust the cert (`Import-Certificate` to the "Trusted People" store), then install the MSIX normally. No Store, no renewal fees.

3. **Copy engine**: Managed `Stream`-based copy throughout (SSD full, SSD incremental, Proton). Provides real-time per-file progress in the UI. Retry-on-locked-file implemented in `FileCopyService` (configurable retry count + delay). Post-transfer SHA-256 hash verification on every file — source hash vs destination hash — before considering the file successfully backed up.

4. **Install scope**: Per-user MSIX. Config and state stored in `%APPDATA%\WinBackup\`. No admin required at install time. Follows the same model as the current PowerShell scripts.
