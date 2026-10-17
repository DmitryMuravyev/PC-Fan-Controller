#Requires -RunAsAdministrator

param(
    [string]$ServiceName = "FanControllerService",
    [string]$InstallDir  = "$env:ProgramFiles\FanControllerService"
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$msg) Write-Host "[*] $msg" -ForegroundColor Cyan  }
function Write-Ok   { param([string]$msg) Write-Host "[+] $msg" -ForegroundColor Green }
function Write-Err  { param([string]$msg) Write-Host "[!] $msg" -ForegroundColor Red   }

# ---- Stop service -----------------------------------------------------------

Write-Step "Looking for service '$ServiceName'..."
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($svc) {
    if ($svc.Status -ne "Stopped") {
        Write-Step "Stopping service..."
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus("Stopped", (New-TimeSpan -Seconds 30))
        Write-Ok "Service stopped."
    }

    Write-Step "Deleting service..."
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Ok "Service deleted."
} else {
    Write-Host "[-] Service '$ServiceName' not found – skipping." -ForegroundColor Yellow
}

# ---- Remove files -----------------------------------------------------------

if (Test-Path $InstallDir) {
    Write-Step "Removing install directory: $InstallDir"
    Remove-Item -Path $InstallDir -Recurse -Force
    Write-Ok "Files removed."
} else {
    Write-Host "[-] Directory '$InstallDir' not found – skipping." -ForegroundColor Yellow
}

Write-Ok "Uninstall complete."
