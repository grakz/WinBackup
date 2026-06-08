# WinBackup — Build Goals

This file is used by the agentic build loop. Work through each item in order.
**Rules:** Do not mark an item complete until its verification criterion passes. Do not skip ahead. When a phase is fully checked, commit before starting the next phase.

---

## Phase 1 — Scaffolding & Core

### 1.1 Solution structure
- [ ] `WinBackup.sln` exists with 5 projects: `WinBackup`, `WinBackup.Core`, `WinBackup.Elevated`, `WinBackup.Tests.Unit`, `WinBackup.Tests.E2E`
- [ ] All projects build with `dotnet build WinBackup.sln` (0 errors, 0 warnings)
- [ ] `WinBackup` references `WinBackup.Core`; `WinBackup.Elevated` references `WinBackup.Core`; both test projects reference `WinBackup.Core`
- [ ] `WinBackup.Core` has no reference to any WinUI or Windows App SDK UI assembly (verified by inspecting `.csproj`)

### 1.2 Config model & service
- [ ] `BackupConfig` model exists with fields: `SourceFolders`, `Ssd.VolumeLabel`, `Ssd.DiskSerial`, `Ssd.BackupSubdir`, `Ssd.DismountAfterBackup`, `Ssd.ConnectWaitMinutes`, `Proton.SyncFolder`, `Proton.LookbackBackups`, `Schedule.SsdDayOfMonth`, `Schedule.SsdTime`, `Schedule.ProtonTime`, `LogDir`
- [ ] `ConfigService.Load()` reads from a path, returns defaults when file absent
- [ ] `ConfigService.Save()` writes and `Load()` round-trips without data loss
- [ ] Unit tests pass: `ConfigServiceTests` — load missing file, load valid file, save-then-load round-trip, malformed JSON returns default without throwing

### 1.3 State model & service
- [ ] `BackupState` model exists with a list of `BackupRecord` entries (each has: `Target` (Ssd/Proton), `StartedAt`, `CompletedAt`, `ResultCode`, `FilescopiedCount`, `ErrorMessage`)
- [ ] `StateService.Load()` / `StateService.Save()` round-trips correctly
- [ ] `StateService.AddRecord()` appends a record and persists
- [ ] `StateService.GetLastSuccessful(target)` returns the most recent record with `ResultCode == Success` for that target
- [ ] Unit tests pass: `StateServiceTests` — empty state, add records, get-last-successful with no records, get-last-successful with mixed results, round-trip serialization

### 1.4 Incremental cutoff logic
- [ ] `CutoffCalculator.GetProtonCutoff(state, lookbackBackups)` returns the timestamp of the Nth-most-recent successful SSD backup
- [ ] Unit tests pass: 0 successful SSD backups → returns `null` (full copy implied); 1 SSD backup with `lookback=2` → returns that backup's timestamp; 5 SSD backups with `lookback=2` → returns 2nd-most-recent timestamp; gap of several months handled correctly

### 1.5 Tray icon & application shell
- [ ] App launches without a visible window; main window is hidden on startup
- [ ] A tray icon appears in the notification area on launch
- [ ] Right-click tray menu shows items: "Status", "Settings", "Run SSD backup now", "Run Proton backup now", "Open logs", "Exit"
- [ ] "Exit" cleanly removes the tray icon and terminates the process
- [ ] App registers a startup task via `Windows.ApplicationModel.StartupTask` (entry in `Package.appxmanifest`)
- [ ] App does not appear in the taskbar while running in tray-only mode

### 1.6 Settings UI
- [ ] Settings window opens from tray menu
- [ ] All config fields are represented: source folder list (add/remove), SSD volume label, SSD disk serial, SSD backup subdir, dismount toggle, connect-wait minutes, Proton sync folder, lookback backups, SSD schedule day + time, Proton schedule time, log directory
- [ ] "Save" writes config via `ConfigService.Save()` and closes the window
- [ ] "Cancel" discards changes
- [ ] Reopening Settings shows the previously saved values
- [ ] Source folder paths are validated to exist on Save; invalid paths show an inline error, not a crash

---

## Phase 2 — Backup Engines

### 2.1 FileCopyService
- [ ] `FileCopyService.CopyAsync(source, dest, progress, ct)` streams source to dest, reports `IProgress<FileCopyProgress>` (bytes copied, total bytes, filename)
- [ ] Retry logic: retries up to 3 times with 2-second delay on `IOException` (locked file); gives up and rethrows after max retries
- [ ] Post-copy SHA-256 verification: computes hash of source and dest after copy; throws `HashMismatchException` if they differ
- [ ] Unit tests pass: happy path copy, retry on first-attempt IOException then succeeds, hash mismatch throws, cancellation via `CancellationToken` stops mid-copy cleanly

### 2.2 SsdBackupEngine
- [ ] `SsdBackupEngine.RunAsync()` performs a **full** copy (all source files into `YYYY_FULL/`) when no full backup exists for the current year
- [ ] Performs an **incremental** copy (files with `LastWriteTime > last SSD backup timestamp` into `YYYY-MM_INCR/`) when a full backup for this year already exists
- [ ] Uses `FileCopyService` for all file copies (progress flows up to orchestrator)
- [ ] Skips files that fail after retries, logs the skip, and continues (does not abort the whole backup)
- [ ] Records a `BackupRecord` via `StateService.AddRecord()` on completion (success or partial)
- [ ] Unit tests pass (with mock filesystem + mock `FileCopyService`): first run of year → full copy; second run same year → incremental; year rollover → new full copy; mixed skip-on-error scenario

### 2.3 ProtonBackupEngine
- [ ] `ProtonBackupEngine.RunAsync()` copies files modified since `CutoffCalculator.GetProtonCutoff()` into `YYYY-MM-DD/` subfolder of the Proton sync folder
- [ ] Skips run entirely (no folder created) if no files have changed since cutoff
- [ ] Uses `FileCopyService` for all copies
- [ ] Records a `BackupRecord` on completion
- [ ] Unit tests pass: no changes since cutoff → no folder created; files changed → correct dated folder; cutoff = null → copies all source files; skip-on-error scenario

### 2.4 BackupOrchestrator
- [ ] `BackupOrchestrator` holds a `PeriodicTimer` (or equivalent) that checks daily whether a Proton backup is due (time-of-day match) and monthly whether an SSD backup is due (day-of-month + time match)
- [ ] Prevents concurrent runs: if a backup is already running, a second trigger is silently skipped and logged
- [ ] Exposes `CurrentStatus` property: `Idle | RunningProton | RunningSsd | Error`
- [ ] Exposes `Progress` event carrying `FileCopyProgress` for UI binding
- [ ] Unit tests pass: double-trigger is a no-op; status transitions Idle → Running → Idle; timer fires at correct time window (mock clock)

---

## Phase 3 — OneDrive Support

### 3.1 Cloud-only file detection
- [ ] `OneDriveFileEnumerator.Enumerate(folder)` returns `IEnumerable<FileEntry>` where each entry has `Path`, `IsCloudOnly` (true if `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` is set), `SizeBytes`
- [ ] Enumeration of a folder containing cloud-only placeholders does **not** trigger hydration (verified: no network activity, placeholder attributes unchanged after enumeration)
- [ ] Unit tests pass: local files classified correctly; cloud-only files classified correctly using mock `WIN32_FIND_DATA` attribute values

### 3.2 Hydrate-copy-verify-dehydrate
- [ ] `SsdBackupEngine` and `ProtonBackupEngine` use `OneDriveFileEnumerator` when a source folder is inside a known OneDrive path
- [ ] For cloud-only files: status UI shows "Downloading from OneDrive: {filename}" during hydration phase
- [ ] After verified copy, `CfSetPinState(CF_PIN_STATE_UNPINNED)` is called to re-dehydrate the file
- [ ] If dehydration call fails (non-fatal), the error is logged and the backup record is marked `PartialSuccess`; the copy itself is not rolled back
- [ ] Unit tests pass: mock cloud-only file → hydrate called → copy called → verify called → dehydrate called in order; dehydration failure → record marked PartialSuccess, no exception thrown

---

## Phase 4 — Volume Air-Gap

### 4.1 Elevated helper binary
- [ ] `WinBackup.Elevated.exe` builds and has `requestedExecutionLevel = requireAdministrator` in its manifest
- [ ] On launch, it opens a named pipe server (`\\.\pipe\WinBackupElevated`) and waits for commands
- [ ] Supports commands: `Lock`, `Dismount`, `Eject`, `RemoveMountPoint`, `Remount`, `Exit` (JSON over pipe)
- [ ] Responds to each command with `{ "success": true/false, "error": "..." }`
- [ ] Exits cleanly on `Exit` command or pipe disconnection

### 4.2 ElevatedHelperProtocol
- [ ] `ElevatedHelperProtocol` in `WinBackup.Core` defines request/response message types (serializable via `System.Text.Json`)
- [ ] `ElevatedHelperClient.SendCommandAsync()` connects to the named pipe, sends a command, and returns the response
- [ ] Unit tests pass: command serialization round-trip; response deserialization happy path and error path

### 4.3 Integration into SsdBackupEngine
- [ ] Before backup starts: main app launches `WinBackup.Elevated.exe` via `ShellExecuteEx` with `runas`; waits up to 30 seconds for pipe connection
- [ ] If UAC is declined or helper fails to connect: backup proceeds **without** dismount, tray icon shows warning badge, log entry written ("Air-gap skipped: UAC declined")
- [ ] After successful backup: sends `Dismount` → `Eject` → `RemoveMountPoint` via pipe
- [ ] If backup fails mid-copy: sends `Remount` before helper exits, so drive remains accessible
- [ ] Helper exits after each backup session (not kept alive between backups)

---

## Phase 5 — Notifications & Reminders

### 5.1 Toast infrastructure
- [ ] `Package.appxmanifest` contains `windows.toastNotificationActivation` extension and COM server registration
- [ ] `NotificationManager.Initialize()` called on app start; `AppNotificationManager.Default` registered
- [ ] A test toast can be triggered from the tray menu ("Send test notification") and appears in the Action Center
- [ ] Toast activation (clicking the toast body) brings the app window to foreground

### 5.2 Backup result toasts
- [ ] On successful backup completion: toast shows "Backup complete — X files copied to [target]"
- [ ] On backup failure: toast shows "Backup failed — [short error]" with a "View logs" action button
- [ ] "View logs" button opens the log directory in Explorer

### 5.3 SSD monthly reminder
- [ ] On app start (and after each reminder action), the next SSD reminder is scheduled via `ScheduledToastNotification` for the configured day-of-month and time
- [ ] Reminder toast uses `scenario="alarm"`, stays on screen until dismissed
- [ ] Toast has two action buttons: "Connect now" (brings app to foreground) and "Remind me tomorrow"
- [ ] "Remind me tomorrow" schedules a new `ScheduledToastNotification` for the same time the following day
- [ ] If toast is dismissed without action, app re-schedules for 3 days later; after 4 dismissals without connection, tray icon gains a warning badge and tooltip reads "SSD backup overdue"
- [ ] Warning badge clears after a successful SSD backup

---

## Phase 6 — Status UI & Polish

### 6.1 Status window
- [ ] Status window opens from tray menu "Status"
- [ ] Shows for each target (SSD, Proton): last run time, backup type (Full/Incremental/Delta), result (Success/Partial/Failed), files copied count
- [ ] Shows current operation when a backup is running: target name, current filename, progress bar (bytes / total bytes), elapsed time
- [ ] "Open logs folder" button opens `LogDir` in Explorer
- [ ] Status updates in real-time while backup is running (no manual refresh needed)

### 6.2 Settings validation
- [ ] Source folders: each path validated to exist; removed paths highlighted with warning (not blocked — path may be a removable drive)
- [ ] SSD volume label: non-empty, no path separators
- [ ] SSD disk serial: non-empty
- [ ] Proton sync folder: path validated to exist on Save
- [ ] Schedule times: valid HH:MM format, day-of-month 1–28
- [ ] Log directory: writable path (validated by attempting to create a temp file)

### 6.3 Error states
- [ ] Tray icon tooltip always reflects current state: "WinBackup — Idle", "WinBackup — Running SSD backup…", "WinBackup — Last backup failed [time]", "WinBackup — SSD backup overdue"
- [ ] Tray icon image changes: normal (shield), running (shield + spinner overlay), warning (shield + exclamation), error (shield + X)
- [ ] Any unhandled exception in backup thread is caught, logged, and surfaces as an error toast (does not crash the app)

---

## Phase 7 — Testing & Hardening

### 7.1 Unit test coverage
- [ ] `dotnet test WinBackup.Tests.Unit` passes with 0 failures
- [ ] Line coverage of `WinBackup.Core` is ≥ 90% (measured via `dotnet-coverage` or Coverlet)
- [ ] No test uses `Thread.Sleep` — all async tests use proper `await` / fake clocks

### 7.2 E2E test suite
- [ ] WinAppDriver server starts and connects to the app session in `AppSession.cs`
- [ ] `TrayIconTests`: app launches → tray icon found in UIA tree → right-click menu items enumerated correctly
- [ ] `SettingsTests`: open settings → change source folder to a temp path → save → reopen settings → verify temp path is present
- [ ] `StatusTests`: trigger a manual Proton backup (temp source + temp Proton folder) → status window shows "Running" → backup completes → status shows "Success" with file count > 0
- [ ] `ToastTests`: programmatically trigger the SSD reminder logic → verify a `ScheduledToastNotification` exists in the notification queue
- [ ] All E2E tests pass against a test config (no real user data touched)

### 7.3 MSIX packaging & signing
- [ ] `dotnet publish` produces a valid `.msix` in the output directory
- [ ] Self-signed certificate generated: `New-SelfSignedCertificate` with `Publisher` matching the MSIX manifest identity
- [ ] MSIX signed with `signtool sign`
- [ ] Signed MSIX installs cleanly on a machine that has the cert in "Trusted People" store
- [ ] `Package.appxmanifest` version is `1.0.0.0`; a second build with bumped version installs as an upgrade without uninstalling first

### 7.4 Final checklist
- [ ] `dotnet build WinBackup.sln` — 0 errors, 0 warnings
- [ ] `dotnet test WinBackup.Tests.Unit` — 0 failures
- [ ] `dotnet test WinBackup.Tests.E2E` — 0 failures
- [ ] App runs from a cold start (no previous config) and prompts user to complete setup via Settings
- [ ] App runs from an existing config with no first-run prompt
- [ ] All log files written to configured `LogDir`, rotated by date, no unbounded growth
- [ ] Uninstall via Windows Settings → Apps removes all scheduled toasts, the startup task, and app files; config/state in `%APPDATA%\WinBackup\` is left intact (user data preserved)
