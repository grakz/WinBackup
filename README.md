# Backup System — Local SSD (air-gapped) + Proton Drive, with tray GUI

A self-contained Windows backup system, built entirely in native PowerShell
(no installs, no runtime, no compilation). All backups are stored as **plain,
directly-usable files** on both targets.

| Path | Cadence | Prompted | Air-gapped | How it reaches the cloud |
|------|---------|----------|------------|--------------------------|
| Local SSD | Monthly | Yes (toast asks you to connect) | Yes (volume dismounted after) | n/a — local |
| Proton Drive | Daily | No | No | writes deltas into the Proton sync folder; the Proton app uploads them |

A **system-tray app** ties it together: view status, edit settings, run a
backup on demand, open logs.

---

## Why this design

- **No rclone.** Because the Proton Drive desktop app is installed and syncing,
  the daily job simply writes plain files into a Proton-synced folder and lets
  the official app upload them. This avoids rclone's beta Proton backend, its
  30-day token expiry, and any auth fragility.
- **No double-storage.** The Proton job is **incremental-only** — it copies just
  the files changed since a cutoff into dated delta folders. Your full dataset
  exists once (the originals); only recent deltas live in the sync folder.
- **Resilient cutoff.** The Proton cutoff is the *Nth-most-recent* successful SSD
  backup (default N=2, configurable). So if you miss a monthly SSD backup, the
  daily Proton deltas still reach back far enough to cover the gap.
- **Air-gap = dismount.** Protection against an encrypting worm comes from the
  SSD volume being dismounted (`mountvol X: /p`) except during the backup window.
  While dismounted it has no drive letter and is invisible to anything scanning
  the filesystem. Physical unplug is your manual final step after the toast.
- **Scheduled tasks, not a service.** A SYSTEM service can't show toasts or drive
  the interactive connect/disconnect flow (Session 0 isolation). The SSD + tray
  tasks run in your logged-on session; the Proton task runs unattended via S4U.

---

## Files

| File | Role |
|------|------|
| `install.ps1` | Run once (elevated). Sets everything up. |
| `uninstall.ps1` | Removes tasks (and optionally files). Leaves your backups intact. |
| `lib-backup.ps1` | Shared functions (config, state, logging, toast, SSD detect). |
| `backup-ssd.ps1` | The monthly air-gapped SSD backup. |
| `backup-proton.ps1` | The daily Proton incremental backup. |
| `backup-tray.ps1` | The system-tray GUI. |
| `apply-schedule.ps1` | Re-applies schedule times after you change them in the GUI. |
| `config.default.json` | Template config (real config lives in ProgramData). |

Installed locations: scripts → `C:\Program Files\BackupSystem`; config, state and
logs → `C:\ProgramData\BackupSystem`.

---

## Install

1. Make sure the **Proton Drive desktop app is installed, logged in, and syncing**.
   Note the folder it syncs (default `%USERPROFILE%\Proton Drive\My files`).
2. Give your backup SSD a **volume label** (e.g. `BACKUP_SSD`) if it doesn't have one.
3. Open **PowerShell as Administrator** in this folder:
   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install.ps1
   ```
   The installer auto-detects the Proton folder, lists your volumes so you can
   pick the SSD (it records the label + serial), registers the tasks, and starts
   the tray app.
4. The shield icon appears in your tray. **Left-click** = status; **right-click**
   = settings, run-now, logs, exit.

### Test it
```powershell
Start-ScheduledTask -TaskName 'BackupSystem - Proton'
Start-ScheduledTask -TaskName 'BackupSystem - SSD'
```
Then check `C:\ProgramData\BackupSystem\logs\`.

---

## Using the tray app

- **Status** — last SSD and Proton backup time, type (full/incr), and result.
- **Edit settings** — add/remove source folders (folder picker), set SSD
  label/serial, Proton sync folder, lookback count, and the schedule times.
  Saving re-applies the schedule to the tasks automatically.
- **Run … backup now** — triggers a backup immediately in the background.
- **Open log folder** / **Exit**.

---

## Backup layout

**SSD** (`<SSD>\Backups\`):
```
2026_FULL\           complete copy, taken once per year
   Documents\  Pictures\
2026-02_INCR\        files changed since the previous SSD backup
2026-03_INCR\
```

**Proton** (`…\Proton Drive\My files\Backups\`):
```
2026-03-14\          one folder per day that had changes
   Documents\  Pictures\
2026-03-15\
```
Days with no changes write nothing.

**Restore:** plain files — just copy them back. For a point-in-time SSD restore,
take `YYYY_FULL` then apply each later `*_INCR` in date order. For Proton, the
dated delta folders give you per-day history; the newest version of any file is
in its latest dated folder.

---

## Caveats

- **Incremental is timestamp-based** (`LastWriteTime`). Predictable and gives true
  plain-file deltas, but a file whose contents change *without* its modified-time
  updating (rare) would be missed.
- **Proton on-demand sync:** if you enable "Optimize storage" in the Proton app,
  make sure the *Backups* folder stays "Always keep on this device" isn't required
  — written files upload regardless, but verify your first run actually appears in
  the Proton web app.
- **Cleanup is manual** — old `*_FULL` / `*_INCR` and old Proton dated folders are
  not auto-pruned, so you never silently lose history. Prune when space is low.
- **Validate before trusting:** these scripts were authored but not executed in a
  Windows environment. Run each once interactively and confirm the toast, the SSD
  detection, the dismount, and the Proton folder upload all behave before relying
  on the schedule.
