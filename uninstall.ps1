# ============================================================================
#  uninstall.ps1  --  Removes the scheduled tasks and (optionally) files.
#  Run elevated. Does NOT touch your backups on the SSD or in Proton.
# ============================================================================
$ErrorActionPreference = "SilentlyContinue"

Write-Host "Stopping tray app..."
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -like "*backup-tray*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

foreach ($t in @("BackupSystem - Proton","BackupSystem - SSD","BackupSystem - Tray")) {
    Unregister-ScheduledTask -TaskName $t -Confirm:$false
    schtasks /Delete /TN $t /F 2>$null | Out-Null
    Write-Host "Removed task: $t"
}

$ans = Read-Host "Also delete program files and config/logs? (y/N)"
if ($ans -eq "y") {
    Remove-Item "C:\Program Files\BackupSystem" -Recurse -Force
    Remove-Item "C:\ProgramData\BackupSystem"  -Recurse -Force
    Write-Host "Removed program files, config and logs."
} else {
    Write-Host "Left files in place. Tasks removed."
}
Write-Host "Done. (Your actual backups on the SSD / Proton are untouched.)"
