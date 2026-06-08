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
| OneDrive pass-through (no local disk) | ⚠️ Partial | Two strategies — see OneDrive section below |
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
│   │   ├── OneDriveFileEnumerator.cs # Cloud-aware file enumeration
│   │   └── OneDriveStreamProvider.cs # Pass-through streaming strategy
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

### OneDrive Files On Demand — Two Strategies

The app will support both, selectable in settings:

**Strategy A — Hydrate-Copy-Dehydrate** (default, simpler):
1. Enumerate OneDrive folder; for each cloud-only file (`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`), open it normally — Windows/OneDrive hydrates it on demand.
2. Stream to backup destination.
3. After copy, call `CfSetPinState(CF_PIN_STATE_UNPINNED)` to re-dehydrate the file.
4. Requires enough free disk space for the largest single file being backed up (not the whole OneDrive). The app will check free space before each file and skip + warn if insufficient.

**Strategy B — Microsoft Graph API streaming** (zero local footprint):
1. Authenticate via MSAL (`Microsoft.Identity.Client`), delegated user permission `Files.Read`.
2. Enumerate via Graph API (`/me/drive/root/children`) to discover cloud-only files.
3. Download content as a `Stream` from `/me/drive/items/{id}/content` and write directly to backup destination.
4. No local disk use. Requires an Azure AD app registration (free) and initial user sign-in.

The app will fall back from Strategy A to Strategy B file-by-file if free disk space is insufficient for a given file.

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
| UI Framework | WinUI 3 (Windows App SDK 2.x) |
| Packaging | Single-Project MSIX |
| Win32 interop | `Microsoft.Windows.CsWin32` (P/Invoke source generator) |
| OneDrive Graph | `Microsoft.Graph` + `Microsoft.Identity.Client` (MSAL) |
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
- Implement `SsdBackupEngine` (full + incremental logic, robocopy equivalent via `File.Copy` streams)
- Implement `ProtonBackupEngine` (incremental delta to sync folder)
- Implement `BackupOrchestrator` (scheduling, concurrency guard, logging)
- Full unit test coverage for all backup logic

### Phase 3 — OneDrive Support
- Implement `OneDriveFileEnumerator` (cloud attribute detection without download)
- Implement Strategy A (hydrate-copy-dehydrate with `CfSetPinState`)
- Implement free-space pre-check and per-file fallback to Strategy B
- Implement Strategy B (Microsoft Graph streaming) with MSAL auth flow in Settings

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
- MSIX signing setup (self-signed cert for dev; production cert or Store for distribution)

---

## Open Questions / Decisions Needed

1. **Graph API app registration**: Strategy B requires an Azure AD app registration. Should this be a pre-registered app ID bundled in the binary (with the user's tenant being their personal Microsoft account), or should advanced users supply their own? For personal OneDrive (MSA), a single app registration with `Files.Read` delegated permission works for all users.

2. **MSIX distribution**: Microsoft Store (auto-updates, no signing headache) vs. self-hosted `.appinstaller` file (more control) vs. plain MSIX download. Store submission requires review but enables auto-updates for free.

3. **Robocopy vs managed copy**: The PowerShell version uses `robocopy` for full SSD backups. The WinUI 3 version should use managed `Stream`-based copy (with progress reporting to UI) rather than shelling out to `robocopy`. This enables real-time progress in the status window but requires re-implementing retry-on-locked-file logic that robocopy handles internally.

4. **Multi-user machines**: The current PowerShell solution is per-user (installed to user session). The WinUI 3 app should follow the same model — per-user MSIX install, per-user config in `%APPDATA%`, no machine-wide service.
