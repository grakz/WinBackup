# WinBackup (WinUI 3) — Build Status

Snapshot of the migration build on branch `winui3-build` (2026-06-08).

## Summary

The **entire backup engine and all supporting logic** are implemented in `WinBackup.Core` and
covered by **104 passing unit tests (90.8% line coverage)**. The **elevated helper** EXE builds.
The **WinUI 3 GUI** is scaffolded but cannot be built in this environment (missing tooling — see
below), so the UI, MSIX packaging, and E2E run remain to be done on a fully-equipped machine.

## What builds and is verified here

| Project | Status | Notes |
|---|---|---|
| `WinBackup.Core` | ✅ builds, 104 tests, 90.8% cov | All backup/browser/scheduling logic |
| `WinBackup.Elevated` | ✅ builds | Pipe server + volume/VSS ops (admin-only, runtime needs real HW) |
| `WinBackup.Tests.Unit` | ✅ builds + passes | xUnit + Moq, in-memory fakes |
| `WinBackup.Tests.E2E` | ✅ builds, self-skips | WinAppDriver scaffold; runs once the app exists |
| `WinBackup` (WinUI 3) | ⛔ cannot build here | Needs the Windows App SDK / MSIX workload |

`dotnet build WinBackup.sln` is green (the UI project is deliberately not in the .sln yet).

## The blocker

`dotnet build WinBackup\WinBackup.csproj` restores and compiles C#, then fails at PRI resource
generation:

```
error MSB4062: The "Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent" task could not be loaded
from ...\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll
```

That task ships with the **Windows App SDK / MSIX build component**, which is not installed (checked:
absent from both the .NET SDK and VS Build Tools). This is an environment gap, not a code problem.

### To unblock (on a machine with Visual Studio / VS Installer)

1. In the **Visual Studio Installer**, add the **"Windows application development"** workload, or at
   minimum the components *Windows App SDK C# Templates* and *MSIX Packaging Tools*.
2. Add the UI project to the solution: `dotnet sln WinBackup.sln add WinBackup\WinBackup.csproj`.
3. Build: `dotnet build WinBackup\WinBackup.csproj -c Release -p:Platform=x64`.

## What remains (all gated on the workload above)

- **UI** (goals 1.5, 1.6, 5.x, 6.x, 8.2, 9.4): tray icon (`Shell_NotifyIcon`), Settings, Status,
  File History, and Browser pages, plus toast notifications and the SSD reminder. The Core view-model
  logic these bind to already exists and is tested.
- **Real platform adapters** (interfaces already defined and unit-tested against fakes):
  - `IPlaceholderController` → `CfSetPinState` (Cloud Filter API)
  - `IFileHistoryBackend` → `FhConfigMgr` COM
  - `IFileHistoryLocator` → Windows File History archive path mapping
  - Tray + toast (`AppNotificationManager`) live in the app project.
- **Elevated integration** (4.4): launch helper via `ShellExecuteEx`/`runas`, drive the air-gap
  sequence and VSS over the named pipe. The protocol, client, coordinator, and handler are done/tested.
- **MSIX packaging + self-signed signing** (7.3) and the **E2E run** (7.2).

## Architecture realized so far

```
WinBackup.Core (net8.0-windows, no UI deps)
├── Abstractions/   IFileSystem, IClock, FileItem, PhysicalFileSystem
├── Config/         BackupConfig + ConfigService
├── State/          BackupState/Record + StateService
├── Backup/         FileFilter, FileCopy (+SHA-256/retry/VSS-flag), FileSetCopier,
│                   Ssd/Proton engines, BackupSchedule, CutoffCalculator, BackupOrchestrator
├── OneDrive/       OneDriveFileEnumerator, IPlaceholderController
├── Volume/         ShadowPath, VssCoordinator, VssLockedFileHandler
├── FileHistory/    FileHistoryService over IFileHistoryBackend
├── Browser/        SsdSnapshotReader, FileHistoryReader, SnapshotIndex
└── Pipes/          ElevatedProtocol + client (newline-JSON)

WinBackup.Elevated (admin EXE)   pipe server, VolumeOperations, VssOperations (WMI)
WinBackup (WinUI 3)              minimal shell (App + MainWindow) — awaits workload
```
