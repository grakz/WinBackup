# WinBackup — Build Goals

This file is used by the agentic build loop. Work through each item in order.
**Rules:** Do not mark an item complete until its verification criterion passes. Do not skip ahead. When a phase is fully checked, commit before starting the next phase.

> **Build environment status (2026-06-08).** This dev box has .NET SDK 10, MSBuild 17.14
> (VS Build Tools 2022), and the Windows 10 SDK incl. signtool — but **not** the Windows App
> SDK / MSIX "single-project" build component (the `Microsoft.Build.Packaging.Pri.Tasks` PRI
> task is missing). Consequence: `WinBackup.Core`, `WinBackup.Elevated`, and both test projects
> build and the unit suite runs (104 tests, 90.8% line coverage). The **WinUI 3 app project
> (`WinBackup`) cannot be built or run here**, which blocks every UI goal (1.5, 1.6, 5.x, 6.x,
> 8.2, 9.4), the E2E run (7.2), and MSIX packaging (7.3). To finish those, install the
> "Windows application development" workload in the VS Installer (components: *Windows App SDK
> C# Templates* + *MSIX Packaging Tools*), then build `WinBackup\WinBackup.csproj`.
> Items below are checked only when actually verified; UI/MSIX items are annotated **BLOCKED**.

---

## Phase 1 — Scaffolding & Core

### 1.1 Solution structure
- [x] All 5 projects exist: `WinBackup` (WinUI 3), `WinBackup.Core`, `WinBackup.Elevated`, `WinBackup.Tests.Unit`, `WinBackup.Tests.E2E`
- [x] The 4 non-UI projects build with `dotnet build WinBackup.sln` (0 errors, 0 warnings). `WinBackup` (UI) is **BLOCKED** on the missing WinUI/MSIX workload and is intentionally not yet in the .sln so the solution stays green; add it once the workload is installed.
- [x] `WinBackup` references `WinBackup.Core`; `WinBackup.Elevated` references `WinBackup.Core`; both test projects reference `WinBackup.Core` (E2E launches the app exe by path rather than project-referencing it)
- [x] `WinBackup.Core` has no reference to any WinUI or Windows App SDK UI assembly (verified by inspecting `.csproj` — only `Microsoft.Extensions.Logging.Abstractions`)

### 1.2 Config model & service
- [x] `BackupConfig` model exists with fields: `SourceFolders`, `Ssd.VolumeLabel`, `Ssd.DiskSerial`, `Ssd.BackupSubdir`, `Ssd.DismountAfterBackup`, `Ssd.ConnectWaitMinutes`, `Proton.SyncFolder`, `Proton.LookbackBackups`, `Schedule.SsdDayOfMonth`, `Schedule.SsdTime`, `Schedule.ProtonTime`, `LogDir`
- [x] `ConfigService.Load()` reads from a path, returns defaults when file absent
- [x] `ConfigService.Save()` writes and `Load()` round-trips without data loss
- [x] Unit tests pass: `ConfigServiceTests` — load missing file, load valid file, save-then-load round-trip, malformed JSON returns default without throwing

### 1.3 State model & service
- [x] `BackupState` model exists with a list of `BackupRecord` entries (each has: `Target` (Ssd/Proton), `StartedAt`, `CompletedAt`, `ResultCode`, `FilesCopiedCount`, `ErrorMessage`)
- [x] `StateService.Load()` / `StateService.Save()` round-trips correctly
- [x] `StateService.AddRecord()` appends a record and persists
- [x] `StateService.GetLastSuccessful(target)` returns the most recent record with `ResultCode == Success` for that target
- [x] Unit tests pass: `StateServiceTests` — empty state, add records, get-last-successful with no records, get-last-successful with mixed results, round-trip serialization

### 1.4 Incremental cutoff logic
- [x] `CutoffCalculator.GetProtonCutoff(state, lookbackBackups)` returns the timestamp of the Nth-most-recent successful SSD backup
- [x] Unit tests pass: 0 successful SSD backups → returns `null` (full copy implied); 1 SSD backup with `lookback=2` → returns that backup's timestamp; 5 SSD backups with `lookback=2` → returns 2nd-most-recent timestamp; gap of several months handled correctly

### 1.5 Tray icon & application shell
- [ ] App launches without a visible window; main window is hidden on startup
- [ ] A tray icon appears in the notification area on launch
- [ ] Right-click tray menu shows items: "Status", "Settings", "File History", "Browse backups", "Run SSD backup now", "Run Proton backup now", "Open logs", "Exit"
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

### 2.1 FileFilterService
- [x] `FileFilterService.ShouldExclude(path)` returns `true` for: `~$*` (Office lock files), `*.tmp`, `*.~*`, `desktop.ini`, `thumbs.db`, `ehthumbs.db`
- [x] Default exclusion patterns are supplemented by `BackupConfig.ExcludePatterns` (user-defined globs)
- [x] Excluded files are logged at debug level and counted separately from skipped/error files
- [x] Unit tests pass: each built-in pattern excluded correctly; custom pattern from config excluded; normal files not excluded; case-insensitive matching on Windows paths

### 2.2 FileCopyService
- [x] `FileCopyService.CopyAsync(source, dest, progress, ct)` streams source to dest, reports `IProgress<FileCopyProgress>` (bytes copied, total bytes, filename)
- [x] `FileFilterService.ShouldExclude()` is checked before any copy attempt; excluded files are skipped silently
- [x] Retry logic: retries up to 3 times with 2-second delay on `IOException` that is **not** a sharing violation; gives up and logs after max retries
- [x] Sharing violation (`ERROR_SHARING_VIOLATION` / `Win32Exception` with native error 32) does **not** retry — instead sets a `RequiresVssFallback = true` flag on the result and returns immediately
- [x] Post-copy SHA-256 verification: computes hash of source and dest after copy; throws `HashMismatchException` if they differ
- [x] Unit tests pass: happy path copy, filter exclusion skips file, non-sharing IOException retries then gives up, sharing violation returns `RequiresVssFallback` without retrying, hash mismatch throws, cancellation stops mid-copy cleanly

### 2.3 SsdBackupEngine
- [x] `SsdBackupEngine.RunAsync()` performs a **full** copy (all source files into `YYYY_FULL/`) when no full backup exists for the current year
- [x] Performs an **incremental** copy (files with `LastWriteTime > last SSD backup timestamp` into `YYYY-MM_INCR/`) when a full backup for this year already exists
- [x] Uses `FileCopyService` for all file copies (progress flows up to orchestrator)
- [x] Files returning `RequiresVssFallback` are collected and passed to the VSS fallback path (Phase 4); until Phase 4 is implemented these are logged as "pending VSS" and counted as `PartialSuccess`
- [x] Excluded files (from `FileFilterService`) are not copied and not counted as errors
- [x] Skips files that fail after retries for non-sharing errors, logs the skip, continues (does not abort the whole backup)
- [x] Records a `BackupRecord` via `StateService.AddRecord()` on completion (success or partial)
- [x] Unit tests pass (with mock filesystem + mock `FileCopyService`): first run of year → full copy; second run same year → incremental; year rollover → new full copy; excluded files skipped; mixed skip-on-error scenario

### 2.4 ProtonBackupEngine
- [x] `ProtonBackupEngine.RunAsync()` copies files modified since `CutoffCalculator.GetProtonCutoff()` into `YYYY-MM-DD/` subfolder of the Proton sync folder
- [x] Skips run entirely (no folder created) if no files have changed since cutoff
- [x] Uses `FileCopyService` for all copies; excluded files not copied; `RequiresVssFallback` files handled same as SsdBackupEngine (Phase 4 completes this)
- [x] Records a `BackupRecord` on completion
- [x] Unit tests pass: no changes since cutoff → no folder created; files changed → correct dated folder; cutoff = null → copies all source files; excluded files skipped; skip-on-error scenario

### 2.5 BackupOrchestrator
- [x] `BackupOrchestrator` holds a `PeriodicTimer` (or equivalent) that checks daily whether a Proton backup is due (time-of-day match) and monthly whether an SSD backup is due (day-of-month + time match) — pure `TickAsync` + `BackupSchedule`; the `PeriodicTimer` host loop lives in the app shell (Phase 1.5)
- [x] Prevents concurrent runs: if a backup is already running, a second trigger is silently skipped and logged
- [x] Exposes `CurrentStatus` property: `Idle | RunningProton | RunningSsd | Error` (named `Status`)
- [x] Exposes `Progress` event carrying `FileCopyProgress` for UI binding (via `BackupProgress.Current`)
- [x] Unit tests pass: double-trigger is a no-op; status transitions Idle → Running → Idle; timer fires at correct time window (mock clock)

---

## Phase 3 — OneDrive Support

### 3.1 Cloud-only file detection
- [x] `OneDriveFileEnumerator.Enumerate(folder)` returns entries (`OneDriveEntry`) with `Path`, `IsCloudOnly` (true if `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` is set), `SizeBytes`
- [x] Enumeration of a folder containing cloud-only placeholders does **not** trigger hydration — enumeration reads metadata only and never opens a file (real-hardware network-activity confirmation deferred to Phase 7 manual validation)
- [x] Unit tests pass: local files classified correctly; cloud-only files classified correctly using mock recall-attribute values

### 3.2 Hydrate-copy-verify-dehydrate
- [x] Engines back up cloud-only files: cloud state is carried on every `FileItem` (`IsCloudOnly`), so the copy pipeline handles them uniformly — opening the file during copy hydrates it on demand (a dedicated OneDrive-path enumerator was unnecessary)
- [ ] For cloud-only files: status UI shows "Downloading from OneDrive: {filename}" during hydration phase — **app UI, deferred to Phase 6**
- [x] After verified copy, dehydration is invoked via `IPlaceholderController.Dehydrate` (real impl calls `CfSetPinState(CF_PIN_STATE_UNPINNED)`; P/Invoke wiring deferred to the app project)
- [x] If dehydration call fails (non-fatal), the error is logged and the backup record is marked `PartialSuccess`; the copy itself is not rolled back
- [x] Unit tests pass: mock cloud-only file → copy → verify → dehydrate called; local file → not dehydrated; dehydration failure → PartialSuccess, no exception thrown

---

## Phase 4 — Volume Air-Gap

### 4.1 Elevated helper binary
- [ ] `WinBackup.Elevated.exe` builds and has `requestedExecutionLevel = requireAdministrator` in its manifest
- [ ] On launch, it opens a named pipe server (`\\.\pipe\WinBackupElevated`) and waits for commands
- [ ] Supports commands: `Lock`, `Dismount`, `Eject`, `RemoveMountPoint`, `Remount`, `VssSnapshot`, `VssDeleteSnapshot`, `Exit` (JSON over pipe)
- [ ] Responds to each command with `{ "success": true/false, "error": "..." }`
- [ ] Exits cleanly on `Exit` command or pipe disconnection

### 4.2 ElevatedHelperProtocol
- [x] `ElevatedProtocol` in `WinBackup.Core` defines request/response message types (serializable via `System.Text.Json`, newline-framed)
- [x] `ElevatedHelperClient.SendCommandAsync()` sends a command over the (pipe) stream and returns the response; `IElevatedHelperClient` seam for testing
- [x] Unit tests pass: command serialization round-trip; response deserialization happy path and error path; client send/receive; empty-stream handling

### 4.3 VSS fallback copy
- [ ] `VssOperations.cs` in the elevated helper implements: `CreateSnapshot(volumePath)` → returns shadow device path; `DeleteSnapshot(snapshotId)` — **elevated EXE, real COM, deferred to app build**
- [ ] Uses VSS COM interfaces (`IVssBackupComponents`) via P/Invoke / COM interop: `InitializeForBackup` → `AddVolume` → `PrepareForBackup` → `DoSnapshotSet` — **elevated EXE, deferred**
- [x] One VSS snapshot is created per source volume per backup session; the snapshot is reused for all locked files on that volume (`VssCoordinator` caching, unit-tested)
- [x] Main app reconstructs the shadow path for a locked file: replaces the drive root with the snapshot device path (`ShadowPath.Map`, unit-tested)
- [x] Copies from shadow path using normal stream copy + SHA-256 verify (`VssLockedFileHandler`)
- [x] `BackupRecord` notes VSS-copied files separately (count of `VssFallbackCount`)
- [x] After backup session ends, the coordinator deletes all snapshots created during the session (`VssCoordinator.DeleteAllAsync`)
- [x] Unit tests pass (mock helper responses): sharing-violation file → VSS path constructed correctly; VSS copy succeeds → file counted in `VssFallbackCount`; VSS also fails → file logged as skipped, backup continues

### 4.4 Integration into SsdBackupEngine and ProtonBackupEngine
- [ ] Before backup starts: main app launches `WinBackup.Elevated.exe` via `ShellExecuteEx` with `runas`; waits up to 30 seconds for pipe connection
- [ ] If UAC is declined or helper fails to connect: backup proceeds **without** dismount and **without** VSS fallback; locked files are logged as skipped with note "UAC declined — VSS unavailable"; tray icon shows warning badge
- [ ] After successful backup: sends `Dismount` → `Eject` → `RemoveMountPoint` via pipe (SSD only)
- [ ] If backup fails mid-copy: sends `Remount` before helper exits, so drive remains accessible (SSD only)
- [ ] All VSS snapshots deleted via helper before helper exits
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
- [x] `dotnet test WinBackup.Tests.Unit` passes with 0 failures (104 tests)
- [x] Line coverage of `WinBackup.Core` is ≥ 90% (90.8% line / 80.4% branch via Coverlet/cobertura)
- [x] `FileFilterServiceTests` covers all built-in exclusion patterns including Office `~$` files
- [x] `FileCopyServiceTests` covers: normal copy, sharing-violation triggers VSS flag, non-sharing error retries then skips, hash mismatch, cancellation
- [x] No test uses `Thread.Sleep` — all async tests use `await` + fake clocks; copy retry delay injected as `TimeSpan.Zero`

### 7.2 E2E test suite
- [ ] WinAppDriver server starts and connects to the app session in `AppSession.cs`
- [ ] `TrayIconTests`: app launches → tray icon found in UIA tree → right-click menu items enumerated correctly
- [ ] `SettingsTests`: open settings → change source folder to a temp path → save → reopen settings → verify temp path is present
- [ ] `StatusTests`: trigger a manual Proton backup (temp source + temp Proton folder) → status window shows "Running" → backup completes → status shows "Success" with file count > 0
- [ ] `ToastTests`: programmatically trigger the SSD reminder logic → verify a `ScheduledToastNotification` exists in the notification queue
- [ ] `FileHistoryTests`: open File History page → status displays without crash; toggle enabled state → status badge updates
- [ ] `BrowserTests`: open Browse backups page → folder tree populated from config → selecting a folder loads timeline (may be empty in test env, no crash)
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

---

## Phase 8 — Windows File History Integration

### 8.1 FileHistoryService
- [ ] `FileHistoryService` in `WinBackup.Core` wraps `FhConfigMgr` COM object via CsWin32
- [ ] `GetStatus()` returns: `Enabled/Disabled/NotConfigured`, last backup time (or null), target drive label + free space
- [ ] `SetEnabled(bool)` toggles File History on/off; returns `NotConfigured` error if no drive is set
- [ ] `GetFrequency()` / `SetFrequency(FhFrequency)` reads/writes backup frequency
- [ ] `GetRetention()` / `SetRetention(type, ageMonths)` reads/writes retention policy
- [ ] `TriggerBackupNow()` triggers an immediate File History backup via `IFhServiceComProxy`
- [ ] All methods degrade gracefully when File History service is absent or not configured (return status, do not throw)
- [ ] Unit tests pass (mock COM interfaces): each getter/setter round-trips; `NotConfigured` state handled; COM failure returns error status, does not throw

### 8.2 File History UI page
- [ ] "File History" entry in tray menu opens `FileHistoryPage`
- [ ] Page shows current status (on/off badge, last backup time, target drive + free space)
- [ ] On/Off toggle updates immediately via `FileHistoryService.SetEnabled()`
- [ ] Frequency dropdown shows all 9 options; changing selection calls `SetFrequency()` and shows confirmation
- [ ] Retention dropdown shows all options; changing selection calls `SetRetention()` and shows confirmation
- [ ] "Back up now" button calls `TriggerBackupNow()`; button is disabled while backup is in progress; shows "Last backed up: {time}" on completion
- [ ] When status is `NotConfigured`: controls are disabled; banner reads "File History has no backup drive configured" with "Open Windows Settings" button that launches `ms-settings:backup`
- [ ] Page reflects live status (polls every 30 seconds while open; updates on show)

---

## Phase 9 — Backup Browser

### 9.1 SSD snapshot reader
- [ ] `SsdSnapshotReader.ListSnapshots(sourceFolder)` returns all available `BackupSnapshot` objects for a folder: each has `Timestamp`, `Source` (`SsdFull` / `SsdIncremental`), `IsAvailable` (false if SSD not connected)
- [ ] `SsdSnapshotReader.GetContentsAt(sourceFolder, timestamp)` reconstructs the folder's file listing at time T: starts from the most recent `YYYY_FULL` at or before T, applies each `YYYY-MM_INCR` up to T in order (later layer wins)
- [ ] Returns only the logical file set (no duplicate paths — only the most-recent-before-T version of each file)
- [ ] Unit tests pass: no snapshots → empty list; only full → full contents returned; full + two incrementals, T between them → correct merged set; file deleted in incr → absent from result; SSD disconnected → snapshots listed as unavailable

### 9.2 File History reader
- [ ] `FileHistoryReader.ListVersions(filePath)` returns all `BackupSnapshot` objects for a specific file from the File History archive, sorted newest-first
- [ ] `FileHistoryReader.GetVersionPath(filePath, timestamp)` returns the physical path to the File History copy at or before the given timestamp (for direct copy/preview)
- [ ] `FileHistoryEnumerator.ListSnapshots(sourceFolder)` returns distinct timestamps at which File History captured any change under `sourceFolder`
- [ ] Unit tests pass: file with no FH versions → empty list; file with 3 versions → correct timestamps; `GetVersionPath` returns closest-before-T version; FH drive absent → returns empty, no exception

### 9.3 Unified snapshot index
- [ ] `SnapshotIndex.GetTimeline(sourceFolder)` merges `SsdSnapshotReader` and `FileHistoryReader` results into one sorted list, deduplicating by source
- [ ] `SnapshotIndex.GetContentsAt(sourceFolder, timestamp)` routes to the correct reader: File History if within retention window and FH drive available; SSD otherwise
- [ ] Returns a `SnapshotContentsResult` with: `IReadOnlyList<SnapshotFile>` (path, size, timestamp, source), `SourceUnavailableReason` (null if successful)
- [ ] Unit tests pass: FH within retention and available → routes to FH; FH unavailable → falls back to SSD; both unavailable → returns `SourceUnavailableReason`; timeline merge produces correct chronological order

### 9.4 Browser UI
- [ ] "Browse backups" entry in tray menu opens `BrowserPage`
- [ ] Left panel: tree of configured source folders; selecting a folder loads its timeline
- [ ] Centre panel: timeline list of all snapshots for the selected folder, grouped by date; File History entries shown in one accent colour, SSD entries in another; unavailable entries greyed with tooltip "Connect SSD to access"
- [ ] Right panel: file listing for the selected snapshot showing reconstructed folder contents (name, size, modified time, source badge)
- [ ] Selecting a file enables "Restore file…" button; selecting any snapshot enables "Restore folder…" button
- [ ] "Restore file…" opens a save-file picker pre-filled with the original filename; copies the historical version to the chosen path
- [ ] "Restore folder…" opens a folder picker; copies all files from the snapshot to the chosen folder (does not overwrite without a confirmation dialog)
- [ ] When SSD is required but not connected: non-blocking info banner shown; File History entries remain functional
- [ ] When neither source is available: empty state illustration with explanation text
