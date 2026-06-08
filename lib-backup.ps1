# ============================================================================
#  lib-backup.ps1  --  Shared functions for the backup system.
#  Dot-sourced by the SSD script, the Proton script, and the GUI.
# ============================================================================

# Root where config, state and logs live.
$Global:BackupRoot   = "C:\ProgramData\BackupSystem"
$Global:ConfigPath   = Join-Path $Global:BackupRoot "config.json"
$Global:StatePath    = Join-Path $Global:BackupRoot "state.json"

function Initialize-BackupRoot {
    if (-not (Test-Path $Global:BackupRoot)) {
        New-Item -ItemType Directory -Path $Global:BackupRoot -Force | Out-Null
    }
    $logDir = Join-Path $Global:BackupRoot "logs"
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
}

# --- Config ----------------------------------------------------------------
function Get-BackupConfig {
    if (-not (Test-Path $Global:ConfigPath)) {
        throw "Config not found at $Global:ConfigPath. Run the installer first."
    }
    return (Get-Content $Global:ConfigPath -Raw | ConvertFrom-Json)
}

function Save-BackupConfig {
    param([Parameter(Mandatory)] $Config)
    Initialize-BackupRoot
    $Config | ConvertTo-Json -Depth 10 | Set-Content -Path $Global:ConfigPath -Encoding UTF8
}

# --- State (records the history of successful backups) ---------------------
#  state.json schema:
#  {
#    "ssdBackups":    [ { "time": "ISO8601", "type": "full|incr" }, ... ],
#    "protonBackups": [ { "time": "ISO8601" }, ... ],
#    "lastSsdResult":    "ok|errors|skipped|never",
#    "lastProtonResult": "ok|errors|never"
#  }
function Get-BackupState {
    if (Test-Path $Global:StatePath) {
        try { return (Get-Content $Global:StatePath -Raw | ConvertFrom-Json) } catch {}
    }
    # Default empty state.
    return [PSCustomObject]@{
        ssdBackups       = @()
        protonBackups    = @()
        lastSsdResult    = "never"
        lastProtonResult = "never"
    }
}

function Save-BackupState {
    param([Parameter(Mandatory)] $State)
    Initialize-BackupRoot
    $State | ConvertTo-Json -Depth 10 | Set-Content -Path $Global:StatePath -Encoding UTF8
}

# Append a successful backup record and trim history to a reasonable length.
function Add-BackupRecord {
    param(
        [ValidateSet("ssd","proton")] [string]$Kind,
        [string]$Type = "incr"
    )
    $state = Get-BackupState
    $rec = [PSCustomObject]@{ time = (Get-Date -Format "o"); type = $Type }
    if ($Kind -eq "ssd") {
        $state.ssdBackups = @($state.ssdBackups + $rec | Select-Object -Last 60)
    } else {
        $state.protonBackups = @($state.protonBackups + $rec | Select-Object -Last 120)
    }
    Save-BackupState $state
}

# --- Logging ---------------------------------------------------------------
function Write-BackupLog {
    param([string]$Message, [string]$LogFile)
    $line = "{0}  {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -Path $LogFile -Value $line
    Write-Host $line
}

# --- Toast notification ----------------------------------------------------
function Show-Toast {
    param([string]$Title, [string]$Message)
    try {
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
        $appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
        $xml = @"
<toast>
  <visual>
    <binding template="ToastText02">
      <text id="1">$([System.Security.SecurityElement]::Escape($Title))</text>
      <text id="2">$([System.Security.SecurityElement]::Escape($Message))</text>
    </binding>
  </visual>
</toast>
"@
        $doc = New-Object Windows.Data.Xml.Dom.XmlDocument
        $doc.LoadXml($xml)
        $toast = [Windows.UI.Notifications.ToastNotification]::new($doc)
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show($toast)
    } catch {
        Write-Host "TOAST (fallback): $Title - $Message"
    }
}

# --- SSD detection by label + serial (not drive letter) --------------------
function Find-BackupSsd {
    param([Parameter(Mandatory)] $Config)
    try {
        $vols = Get-Volume | Where-Object {
            $_.FileSystemLabel -eq $Config.ssd.volumeLabel -and $_.DriveLetter
        }
        foreach ($v in $vols) {
            $disk = Get-Partition -DriveLetter $v.DriveLetter -ErrorAction SilentlyContinue |
                    Get-Disk -ErrorAction SilentlyContinue
            if ($disk -and ($disk.SerialNumber.Trim() -eq $Config.ssd.diskSerial.Trim())) {
                return [string]$v.DriveLetter
            }
        }
    } catch {}
    return $null
}

# --- Compute the Proton incremental cutoff ---------------------------------
# Returns the timestamp of the Nth-most-recent successful SSD backup
# (N = config.proton.lookbackBackups). Falls back gracefully:
#   * if fewer than N SSD backups exist, use the oldest available
#   * if no SSD backups exist at all, use a 60-day window
function Get-ProtonCutoff {
    param([Parameter(Mandatory)] $Config)
    $state = Get-BackupState
    $ssd = @($state.ssdBackups | Sort-Object { [DateTime]$_.time } -Descending)
    $n = [int]$Config.proton.lookbackBackups
    if ($ssd.Count -eq 0) {
        return (Get-Date).AddDays(-60)
    }
    $idx = [Math]::Min($n - 1, $ssd.Count - 1)
    return [DateTime]$ssd[$idx].time
}
