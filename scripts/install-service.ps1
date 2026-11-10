<#
.SYNOPSIS
    Installs and configures the GameGuard Background Windows Service.

.DESCRIPTION
    Requires administrative elevation. Performs:
    1. Verifies administrative privileges.
    2. Ensures %ProgramData%\GameGuard storage directory exists with proper ACLs.
    3. Executes GameGuard.Service.exe --install to register the Windows Service.
    4. Starts the Windows Service via GameGuard.Service.exe --start.
    5. Checks and displays service status.
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
Write-Host "   GameGuard Windows Service Installation" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$serviceDir = Resolve-Path $ServiceBinDir
$serviceExe = Join-Path $serviceDir "GameGuard.Service.exe"

if (-not (Test-Path $serviceExe)) {
    Write-Error "GameGuard.Service.exe not found at: $serviceExe. Please run scripts/publish.ps1 first."
    exit 1
}

# 2. Setup %ProgramData%\GameGuard storage
$programData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$gameGuardDataDir = Join-Path $programData "GameGuard"

if (-not (Test-Path $gameGuardDataDir)) {
    Write-Host "Creating shared database directory: $gameGuardDataDir..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $gameGuardDataDir -Force | Out-Null
}

Write-Host "Configuring access control on $gameGuardDataDir..." -ForegroundColor Yellow
$acl = Get-Acl $gameGuardDataDir
$ruleUsers = New-Object Security.AccessControl.FileSystemAccessRule(
    "BUILTIN\Users",
    "Modify, Synchronize",
    "ContainerInherit, ObjectInherit",
    "None",
    "Allow"
)
$ruleAdmins = New-Object Security.AccessControl.FileSystemAccessRule(
    "BUILTIN\Administrators",
    "FullControl",
    "ContainerInherit, ObjectInherit",
    "None",
    "Allow"
)
$acl.SetAccessRule($ruleUsers)
$acl.SetAccessRule($ruleAdmins)
Set-Acl -Path $gameGuardDataDir -AclObject $acl
Write-Host "Shared storage initialized with secure multi-user permissions." -ForegroundColor Green

# 3. Register service
Write-Host ""
Write-Host "Registering GameGuard Background Service..." -ForegroundColor Yellow
& $serviceExe --install
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to install GameGuard Windows Service."
    exit $LASTEXITCODE
}

# 4. Start service
Write-Host ""
Write-Host "Starting GameGuard Background Service..." -ForegroundColor Yellow
& $serviceExe --start
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Could not start service immediately. Attempting status check..."
}

# 5. Display status
Write-Host ""
Write-Host "Current Service Status:" -ForegroundColor Cyan
& $serviceExe --status

Write-Host ""
Write-Host "Installation completed successfully!" -ForegroundColor Green
Write-Host "The GameGuard desktop UI will now connect to this daemon automatically via Named Pipes." -ForegroundColor Gray
