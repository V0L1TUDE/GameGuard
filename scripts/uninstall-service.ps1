<#
.SYNOPSIS
    Stops and unregisters the GameGuard Background Windows Service.

.DESCRIPTION
    Requires administrative elevation. Performs:
    1. Verifies administrative privileges.
    2. Executes GameGuard.Service.exe --stop to cleanly stop the daemon.
    3. Executes GameGuard.Service.exe --uninstall to remove service registration from SCM.
#>

[CmdletBinding()]
param(
    [string]$ServiceBinDir = "$PSScriptRoot\..\dist\service"
)

$ErrorActionPreference = "Stop"

# 1. Elevation check
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Administrative privileges required. Please re-run this script in an elevated PowerShell session (Run as Administrator)."
    exit 1
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   GameGuard Windows Service Uninstaller" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$serviceDir = Resolve-Path $ServiceBinDir
$serviceExe = Join-Path $serviceDir "GameGuard.Service.exe"

if (-not (Test-Path $serviceExe)) {
    Write-Error "GameGuard.Service.exe not found at: $serviceExe."
    exit 1
}

# 2. Stop service
Write-Host "Stopping GameGuard Background Service..." -ForegroundColor Yellow
try {
    & $serviceExe --stop
}
catch {
    Write-Warning "Service was not running or could not be stopped cleanly: $_"
}

# 3. Uninstall service
Write-Host "Unregistering GameGuard Windows Service..." -ForegroundColor Yellow
& $serviceExe --uninstall
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to uninstall GameGuard Windows Service."
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "GameGuard Windows Service successfully removed from Windows Service Control Manager." -ForegroundColor Green
Write-Host "Database and history in %ProgramData%\GameGuard were preserved." -ForegroundColor Gray
