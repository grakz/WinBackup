# ============================================================================
#  backup-tray.ps1  --  System-tray GUI for the backup system.
#
#  Lives in the notification area. Left/right-click the icon for a menu:
#    * Status...        : window showing last SSD + Proton backup results/times
#    * Edit settings... : window to add/remove source folders, set SSD label/
#                         serial, Proton sync folder, schedule times
#    * Run SSD backup now
#    * Run Proton backup now
#    * Open log folder
#    * Exit
#
#  Launched at logon by a scheduled task (see installer). Runs in your user
#  session so the tray icon and toasts work.
# ============================================================================

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. "$PSScriptRoot\lib-backup.ps1"
Initialize-BackupRoot

$scriptDir = $PSScriptRoot

# --- Helpers to format state for display -----------------------------------
function Format-LastBackup {
    param($records, $resultFlag)
    $recs = @($records | Sort-Object { [DateTime]$_.time } -Descending)
    if ($recs.Count -eq 0) { return "Never run" }
    $last = $recs[0]
    $when = (Get-Date ([DateTime]$last.time) -Format "yyyy-MM-dd HH:mm")
    $type = if ($last.PSObject.Properties.Name -contains 'type') { " ($($last.type))" } else { "" }
    $flag = switch ($resultFlag) {
        "ok"      { "OK" }
        "errors"  { "ERRORS" }
        "skipped" { "skipped" }
        default   { "" }
    }
    return "$when$type  -  $flag"
}

# --- Status window ---------------------------------------------------------
function Show-StatusWindow {
    $state = Get-BackupState
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "Backup status"
    $form.Size = New-Object System.Drawing.Size(460, 280)
    $form.StartPosition = "CenterScreen"
    $form.FormBorderStyle = "FixedDialog"
    $form.MaximizeBox = $false; $form.MinimizeBox = $false

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Location = New-Object System.Drawing.Point(20, 20)
    $lbl.Size = New-Object System.Drawing.Size(420, 200)
    $lbl.Font = New-Object System.Drawing.Font("Segoe UI", 10)
    $ssdLine    = Format-LastBackup $state.ssdBackups    $state.lastSsdResult
    $protonLine = Format-LastBackup $state.protonBackups $state.lastProtonResult
    $ssdCount   = @($state.ssdBackups).Count
    $protCount  = @($state.protonBackups).Count
    $lbl.Text = @"
Local SSD backup (monthly, air-gapped)
   Last: $ssdLine
   Total recorded: $ssdCount

Proton Drive backup (daily, incremental)
   Last: $protonLine
   Total recorded: $protCount

Config: $Global:ConfigPath
Logs:   $(Join-Path $Global:BackupRoot 'logs')
"@
    $form.Controls.Add($lbl)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = "Close"; $ok.Location = New-Object System.Drawing.Point(350, 210)
    $ok.Add_Click({ $form.Close() })
    $form.Controls.Add($ok)
    $form.ShowDialog() | Out-Null
}

# --- Settings window -------------------------------------------------------
function Show-SettingsWindow {
    $config = Get-BackupConfig

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "Backup settings"
    $form.Size = New-Object System.Drawing.Size(620, 560)
    $form.StartPosition = "CenterScreen"
    $form.FormBorderStyle = "FixedDialog"
    $form.MaximizeBox = $false

    $y = 15
    function Add-Label($text, $top) {
        $l = New-Object System.Windows.Forms.Label
        $l.Text = $text; $l.Location = New-Object System.Drawing.Point(15, $top)
        $l.Size = New-Object System.Drawing.Size(580, 18)
        $l.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
        $form.Controls.Add($l)
    }

    # ---- Source folders ----
    Add-Label "Folders to back up:" $y
    $y += 22
    $list = New-Object System.Windows.Forms.ListBox
    $list.Location = New-Object System.Drawing.Point(15, $y)
    $list.Size = New-Object System.Drawing.Size(470, 120)
    foreach ($s in $config.sourceFolders) { [void]$list.Items.Add($s) }
    $form.Controls.Add($list)

    $btnAdd = New-Object System.Windows.Forms.Button
    $btnAdd.Text = "Add..."; $btnAdd.Location = New-Object System.Drawing.Point(495, $y)
    $btnAdd.Size = New-Object System.Drawing.Size(95, 28)
    $btnAdd.Add_Click({
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        if ($dlg.ShowDialog() -eq "OK") { [void]$list.Items.Add($dlg.SelectedPath) }
    })
    $form.Controls.Add($btnAdd)

    $btnRem = New-Object System.Windows.Forms.Button
    $btnRem.Text = "Remove"; $btnRem.Location = New-Object System.Drawing.Point(495, ($y + 34))
    $btnRem.Size = New-Object System.Drawing.Size(95, 28)
    $btnRem.Add_Click({ if ($list.SelectedIndex -ge 0) { $list.Items.RemoveAt($list.SelectedIndex) } })
    $form.Controls.Add($btnRem)

    $y += 130

    # ---- Generic text field helper ----
    $fields = @{}
    function Add-Field($key, $label, $value, [ref]$topRef) {
        $t = $topRef.Value
        $l = New-Object System.Windows.Forms.Label
        $l.Text = $label; $l.Location = New-Object System.Drawing.Point(15, ($t + 3))
        $l.Size = New-Object System.Drawing.Size(180, 20)
        $form.Controls.Add($l)
        $tb = New-Object System.Windows.Forms.TextBox
        $tb.Location = New-Object System.Drawing.Point(200, $t)
        $tb.Size = New-Object System.Drawing.Size(390, 22)
        $tb.Text = [string]$value
        $form.Controls.Add($tb)
        $fields[$key] = $tb
        $topRef.Value = $t + 30
    }

    $topRef = [ref]$y
    Add-Field "ssdLabel"   "SSD volume label:"        $config.ssd.volumeLabel   $topRef
    Add-Field "ssdSerial"  "SSD disk serial:"         $config.ssd.diskSerial    $topRef
    Add-Field "ssdSubdir"  "SSD backup subfolder:"    $config.ssd.backupSubdir  $topRef
    Add-Field "protonPath" "Proton sync folder:"      $config.proton.syncFolder $topRef
    Add-Field "lookback"   "Proton lookback (# SSD):" $config.proton.lookbackBackups $topRef
    Add-Field "ssdDay"     "SSD day of month (1-28):" $config.schedule.ssdDayOfMonth $topRef
    Add-Field "ssdTime"    "SSD time (HH:MM):"        $config.schedule.ssdTime  $topRef
    Add-Field "protonTime" "Proton time (HH:MM):"     $config.schedule.protonTime $topRef

    $y = $topRef.Value + 10

    # ---- Save / Cancel ----
    $btnSave = New-Object System.Windows.Forms.Button
    $btnSave.Text = "Save"; $btnSave.Location = New-Object System.Drawing.Point(415, $y)
    $btnSave.Size = New-Object System.Drawing.Size(80, 30)
    $btnSave.Add_Click({
        $config.sourceFolders = @($list.Items)
        $config.ssd.volumeLabel    = $fields["ssdLabel"].Text
        $config.ssd.diskSerial     = $fields["ssdSerial"].Text
        $config.ssd.backupSubdir   = $fields["ssdSubdir"].Text
        $config.proton.syncFolder  = $fields["protonPath"].Text
        $config.proton.lookbackBackups = [int]$fields["lookback"].Text
        $config.schedule.ssdDayOfMonth = [int]$fields["ssdDay"].Text
        $config.schedule.ssdTime    = $fields["ssdTime"].Text
        $config.schedule.protonTime = $fields["protonTime"].Text
        Save-BackupConfig $config
        # Re-apply schedule times to the existing tasks if the helper is present.
        $applier = Join-Path $scriptDir "apply-schedule.ps1"
        if (Test-Path $applier) {
            try { & powershell -NoProfile -ExecutionPolicy Bypass -File $applier } catch {}
        }
        [System.Windows.Forms.MessageBox]::Show("Settings saved.","Backup") | Out-Null
        $form.Close()
    })
    $form.Controls.Add($btnSave)

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = "Cancel"; $btnCancel.Location = New-Object System.Drawing.Point(505, $y)
    $btnCancel.Size = New-Object System.Drawing.Size(85, 30)
    $btnCancel.Add_Click({ $form.Close() })
    $form.Controls.Add($btnCancel)

    $form.ShowDialog() | Out-Null
}

# --- Run a backup script in a new hidden process ---------------------------
function Start-BackupScript {
    param([string]$ScriptName, [string]$Friendly)
    $path = Join-Path $scriptDir $ScriptName
    Start-Process powershell -ArgumentList @(
        "-NoProfile","-WindowStyle","Hidden","-ExecutionPolicy","Bypass","-File","`"$path`""
    )
    Show-Toast "Backup started" "$Friendly is running. You'll be notified when it finishes."
}

# --- Build the tray icon + menu --------------------------------------------
$icon = New-Object System.Windows.Forms.NotifyIcon
# Use a built-in system icon so we don't ship an .ico file.
$icon.Icon = [System.Drawing.SystemIcons]::Shield
$icon.Text = "Backup System"
$icon.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip

$miStatus = $menu.Items.Add("Status...")
$miStatus.Add_Click({ Show-StatusWindow })

$miSettings = $menu.Items.Add("Edit settings...")
$miSettings.Add_Click({ Show-SettingsWindow })

[void]$menu.Items.Add("-")  # separator

$miRunSsd = $menu.Items.Add("Run SSD backup now")
$miRunSsd.Add_Click({ Start-BackupScript "backup-ssd.ps1" "Local SSD backup" })

$miRunProton = $menu.Items.Add("Run Proton backup now")
$miRunProton.Add_Click({ Start-BackupScript "backup-proton.ps1" "Proton Drive backup" })

[void]$menu.Items.Add("-")

$miLogs = $menu.Items.Add("Open log folder")
$miLogs.Add_Click({ Start-Process (Join-Path $Global:BackupRoot "logs") })

[void]$menu.Items.Add("-")

$miExit = $menu.Items.Add("Exit")
$miExit.Add_Click({
    $icon.Visible = $false
    $icon.Dispose()
    [System.Windows.Forms.Application]::Exit()
})

$icon.ContextMenuStrip = $menu
# Left-click also opens the menu (show status on double-click).
$icon.Add_MouseClick({
    param($s, $e)
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        Show-StatusWindow
    }
})

# --- Run the message loop --------------------------------------------------
$appContext = New-Object System.Windows.Forms.ApplicationContext
[System.Windows.Forms.Application]::Run($appContext)
