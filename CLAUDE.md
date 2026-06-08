# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WinBackup is a native Windows PowerShell backup system providing dual-target backups: monthly air-gapped SSD + daily incremental Proton Drive cloud. No external dependencies — pure PowerShell 5.0+ and Windows built-ins (robocopy, mountvol, schtasks).

## Running and Testing

No build step. Scripts run directly via PowerShell.

**Install (requires elevated shell):**
```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install.ps1
```

**Test scheduled tasks manually:**
```powershell
Start-ScheduledTask -TaskName 'BackupSystem - Proton'
Start-ScheduledTask -TaskName 'BackupSystem - SSD'
```

**Run scripts directly (from install dir `C:\Program Files\BackupSystem\`):**
```powershell
powershell -File backup-ssd.ps1
powershell -File backup-proton.ps1
powershell -File backup-tray.ps1
```

## Architecture

### Script Roles

| Script | Purpose | Session type |
|--------|---------|-------------|
| `lib-backup.ps1` | Shared library — dot-sourced by all others | N/A |
| `backup-ssd.ps1` | Monthly full+incremental copy to SSD, then dismounts volume | User session (interactive) |
| `backup-proton.ps1` | Daily incremental copy to Proton sync folder | S4U (unattended service account) |
| `backup-tray.ps1` | WinForms system tray GUI | User session (at logon) |
| `install.ps1` | Copies scripts, creates config, registers 3 scheduled tasks | Elevated, one-time |
| `apply-schedule.ps1` | Updates task trigger times after config changes | Called by tray settings UI |

### Shared Library (`lib-backup.ps1`)

All backup scripts dot-source this file. It provides:
- `Get-BackupConfig` / `Save-BackupConfig` — reads/writes `C:\ProgramData\BackupSystem\config.json`
- `Get-BackupState` / `Save-BackupState` / `Add-BackupRecord` — reads/writes `state.json`; state tracks backup history used to compute incremental cutoffs
- SSD detection by volume label + disk serial (not drive letter — handles reassignment)
- Toast notification helper
- Proton cutoff calculation: uses the Nth-most-recent successful SSD backup timestamp (N = `lookbackBackups` in config) so missed monthly SSD runs don't cause data loss in Proton

### File Locations (post-install)

- Scripts: `C:\Program Files\BackupSystem\`
- Config + state + logs: `C:\ProgramData\BackupSystem\`

### Backup Strategy

**SSD (monthly):**
- First backup of a calendar year → full copy into `YYYY_FULL/` via robocopy
- Subsequent backups → incremental into `YYYY-MM_INCR/` (files modified since last SSD backup)
- Volume is dismounted after backup (`mountvol X: /p`) for air-gap

**Proton (daily):**
- Incremental only; writes changed files to dated `YYYY-MM-DD/` folders in Proton sync directory
- Proton desktop app uploads automatically — no rclone/API tokens needed
- Skips if no files changed (no empty folders created)
- Cutoff date computed from state history, not last-run date (resilient to gaps)

**Restore:** Copy `YYYY_FULL` contents first, then apply `*_INCR` folders in chronological order — all plain files, no proprietary format.

### Key Design Constraints

- SSD and tray tasks run in the **user session** (needed for toast notifications and GUI); Proton task runs as S4U (no user session required)
- SSD dismount requires the task to run with highest privileges
- Incremental detection uses `LastWriteTime` — files with changed content but unchanged timestamps would be missed
- `config.default.json` is a template; the live config is generated during `install.ps1` and lives in ProgramData
