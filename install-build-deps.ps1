<#
.SYNOPSIS
    Installs the minimal set of build dependencies needed to build WinBackup
    (the WinUI 3 app) on a clean Windows 11 machine.

.DESCRIPTION
    This deliberately installs the *minimum* tooling -- NOT "Visual Studio with
    everything". Everything here is derived from what the projects actually
    declare:

      * All projects target  net8.0-windows10.0.19041.0   -> .NET 8 SDK
      * WinBackup.csproj sets UseWinUI=true                -> the WinUI 3 PRI /
        MSIX build tasks (Microsoft.Build.Packaging.Pri.Tasks.dll). These ship
        ONLY with the Visual Studio "Windows App SDK C# Templates" component;
        their absence is the documented build blocker (see BUILD_STATUS.md).
      * Microsoft.Windows.SDK.BuildTools 10.0.26100.x      -> Windows 11 SDK 26100
      * Everything else (Microsoft.WindowsAppSDK, xunit, Moq, Appium,
        System.Management, ...) is a NuGet PackageReference and is restored
        automatically by `dotnet restore` -- no machine-wide install required.
        WindowsAppSDKSelfContained=true means the App SDK runtime is bundled at
        publish time, so no separate runtime install is needed to build or run.

    The script is idempotent: it skips anything already present and, if VS Build
    Tools already exists, it *modifies* the existing install to add the missing
    WinUI component rather than reinstalling.

.NOTES
    Run from an ELEVATED PowerShell session. Requires winget (ships with
    Windows 11). A reboot may be requested by the Visual Studio installer.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    # Skip the Visual Studio Build Tools step (e.g. if you build the UI elsewhere
    # and only want Core/tests, which build with just the .NET SDK).
    [switch] $SkipVSBuildTools,

    # The Windows 11 SDK component to install. 26100 matches the version pinned
    # by Microsoft.Windows.SDK.BuildTools in WinBackup.csproj.
    [string] $Windows11SdkComponent = 'Microsoft.VisualStudio.Component.Windows11SDK.26100'
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string]$Message) Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Skip { param([string]$Message) Write-Host "    [skip] $Message" -ForegroundColor DarkGray }
function Write-Ok   { param([string]$Message) Write-Host "    [ok]   $Message" -ForegroundColor Green }

# --- preconditions ---------------------------------------------------------
Write-Step 'Checking prerequisites'

$winget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $winget) {
    throw "winget was not found. Install 'App Installer' from the Microsoft Store, then re-run this script."
}
Write-Ok "winget: $($winget.Source)"

# --- 1) .NET 8 SDK ---------------------------------------------------------
# Every project targets net8.0-windows10.0.19041.0. A .NET 8+ SDK can build it.
Write-Step '.NET 8 SDK'

function Test-Dotnet8 {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $false }
    # Any installed SDK with major version >= 8 can target net8.0.
    $sdks = & dotnet --list-sdks 2>$null
    foreach ($line in $sdks) {
        if ($line -match '^(\d+)\.') { if ([int]$Matches[1] -ge 8) { return $true } }
    }
    return $false
}

if (Test-Dotnet8) {
    Write-Skip 'A .NET 8+ SDK is already installed.'
} else {
    Write-Host '    Installing Microsoft.DotNet.SDK.8 via winget...'
    winget install --id Microsoft.DotNet.SDK.8 --exact --source winget `
        --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) { throw "winget failed to install the .NET 8 SDK (exit $LASTEXITCODE)." }
    Write-Ok '.NET 8 SDK installed.'
}

# --- 2) Visual Studio Build Tools 2022 (WinUI 3 / MSIX / PRI tasks) --------
if ($SkipVSBuildTools) {
    Write-Step 'Visual Studio Build Tools (SKIPPED via -SkipVSBuildTools)'
} else {
    Write-Step 'Visual Studio Build Tools 2022 with WinUI 3 components'

    # Minimal component set -- just enough to compile + PRI/MSIX-package a WinUI 3
    # C# app from the command line:
    #   ManagedDesktopBuildTools      -> MSBuild, Roslyn, NuGet, .NET SDK targets
    #   ComponentGroup.WindowsAppSDK.Cs -> Windows App SDK C# Templates +
    #                                      single-project MSIX + the missing PRI tasks
    #   Windows11SDK.26100            -> Windows SDK headers/libs/winmd
    $components = @(
        'Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools'
        'Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs'
        $Windows11SdkComponent
    )
    $addArgs = ($components | ForEach-Object { "--add $_" }) -join ' '

    # Locate an existing Build Tools install so we *modify* instead of duplicating.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $existingPath = $null
    if (Test-Path $vswhere) {
        $existingPath = & $vswhere -products 'Microsoft.VisualStudio.Product.BuildTools' `
            -format value -property installationPath -nologo 2>$null | Select-Object -First 1
    }

    if ($existingPath) {
        Write-Host "    Build Tools already present at: $existingPath"
        Write-Host '    Adding the WinUI 3 / MSIX components to the existing install...'
        $setup = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\setup.exe'
        $modifyArgs = "modify --installPath `"$existingPath`" $addArgs " +
                      '--includeRecommended --quiet --norestart --wait --force'
        $p = Start-Process -FilePath $setup -ArgumentList $modifyArgs -Wait -PassThru
        $code = $p.ExitCode
    } else {
        Write-Host '    Installing Microsoft.VisualStudio.2022.BuildTools via winget...'
        $override = "--quiet --wait --norestart --includeRecommended $addArgs"
        winget install --id Microsoft.VisualStudio.2022.BuildTools --exact --source winget `
            --accept-package-agreements --accept-source-agreements `
            --override $override
        $code = $LASTEXITCODE
    }

    # 3010 = success, reboot required. Treat as success.
    if ($code -eq 3010) {
        Write-Ok 'Build Tools components installed -- a REBOOT is required to finish.'
        $script:RebootRequired = $true
    } elseif ($code -ne 0) {
        throw "Visual Studio installer failed (exit $code). See %TEMP%\dd_*.log for details."
    } else {
        Write-Ok 'Visual Studio Build Tools 2022 (WinUI 3 components) installed.'
    }
}

# --- 3) Restore NuGet packages (App SDK, xunit, Moq, Appium, ...) ----------
# This pulls Microsoft.WindowsAppSDK and every other PackageReference. No
# machine-wide install of the App SDK is needed -- it's a self-contained NuGet.
Write-Step 'Restoring NuGet packages (solution + WinUI app project)'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sln = Join-Path $repoRoot 'WinBackup.sln'
if (Test-Path $sln) {
    Push-Location $repoRoot
    try {
        & dotnet restore $sln
        if ($LASTEXITCODE -ne 0) { Write-Warning 'dotnet restore reported errors (re-run after a reboot if components were just installed).' }
        else { Write-Ok 'NuGet packages restored.' }
    } finally { Pop-Location }
} else {
    Write-Skip "WinBackup.sln not found next to this script; skipping restore."
}

# --- done ------------------------------------------------------------------
Write-Step 'Done'
Write-Host @'
Minimal build environment installed. To build:

    dotnet build WinBackup.sln                         # Core + Elevated + tests
    dotnet build WinBackup\WinBackup.csproj -c Release -p:Platform=x64   # WinUI 3 app

If the Visual Studio installer asked for a reboot, restart Windows before
building the WinUI 3 app project.
'@ -ForegroundColor Gray

if ($script:RebootRequired) {
    Write-Warning 'A reboot is required to complete the Visual Studio Build Tools installation.'
}
