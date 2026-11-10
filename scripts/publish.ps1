<#
.SYNOPSIS
    Builds and publishes production-ready single-file binaries for GameGuard.

.DESCRIPTION
    Compiles and publishes:
    - GameGuard.Service (Headless Windows Background Service daemon)
    - GameGuard.UI (WPF Desktop Application and Management Console)
    Targeting win-x64, optimized for distribution with optional self-contained runtime.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Release.

.PARAMETER SelfContained
    Whether to bundle the .NET 10 LTS runtime into the executable. Default is $true.

.PARAMETER OutputDir
    Root directory for published output artifacts. Default is "dist".
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [bool]$SelfContained = $true,

    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

# Ensure .NET 10 SDK is in PATH
if ($env:USERPROFILE) {
    $dotnetUserProfile = Join-Path $env:USERPROFILE ".dotnet"
    if (Test-Path $dotnetUserProfile) {
        $env:PATH = "$dotnetUserProfile;$env:PATH"
    }
}

$rootPath = Resolve-Path (Join-Path $PSScriptRoot "..")
$distPath = Join-Path $rootPath $OutputDir
$serviceOutPath = Join-Path $distPath "service"
$uiOutPath = Join-Path $distPath "app"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   GameGuard Release Packager & Publisher" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Configuration  : $Configuration"
Write-Host "Target Runtime : win-x64"
Write-Host "Self-Contained : $SelfContained"
Write-Host "Destination    : $distPath"
Write-Host ""

# 1. Run full test suite before publishing
Write-Host "[1/4] Running automated test verification suite..." -ForegroundColor Yellow
$testArgs = @("test", (Join-Path $rootPath "GameGuard.slnx"), "-c", $Configuration, "--nologo")
& dotnet @testArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Test suite execution failed. Publishing aborted."
    exit $LASTEXITCODE
}
Write-Host "      All unit and integration tests passed." -ForegroundColor Green

# 2. Publish Service
Write-Host ""
Write-Host "[2/4] Publishing GameGuard.Service (Windows Background Service)..." -ForegroundColor Yellow
$serviceProj = Join-Path $rootPath "src\GameGuard.Service\GameGuard.Service.csproj"
$serviceArgs = @(
    "publish", $serviceProj,
    "-c", $Configuration,
    "-r", "win-x64",
    "-p:PublishSingleFile=true",
    "--self-contained", $SelfContained.ToString().ToLower(),
    "-o", $serviceOutPath,
    "--nologo"
)
& dotnet @serviceArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish GameGuard.Service."
    exit $LASTEXITCODE
}
Write-Host "      GameGuard.Service published successfully." -ForegroundColor Green

# 3. Publish UI
Write-Host ""
Write-Host "[3/4] Publishing GameGuard.UI (Desktop UI & System Tray)..." -ForegroundColor Yellow
$uiProj = Join-Path $rootPath "src\GameGuard.UI\GameGuard.UI.csproj"
$uiArgs = @(
    "publish", $uiProj,
    "-c", $Configuration,
    "-r", "win-x64",
    "-p:PublishSingleFile=true",
    "--self-contained", $SelfContained.ToString().ToLower(),
    "-o", $uiOutPath,
    "--nologo"
)
& dotnet @uiArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish GameGuard.UI."
    exit $LASTEXITCODE
}
Write-Host "      GameGuard.UI published successfully." -ForegroundColor Green

# 4. Packaging Summary
Write-Host ""
Write-Host "[4/4] Release Package Manifest:" -ForegroundColor Cyan

$serviceExe = Join-Path $serviceOutPath "GameGuard.Service.exe"
$uiExe = Join-Path $uiOutPath "GameGuard.UI.exe"

$artifacts = @(
    [PSCustomObject]@{
        Component = "GameGuard Service Daemon"
        Executable = "GameGuard.Service.exe"
        SizeMB = [Math]::Round(((Get-Item $serviceExe).Length / 1MB), 2)
        Location = $serviceOutPath
    },
    [PSCustomObject]@{
        Component = "GameGuard Desktop UI"
        Executable = "GameGuard.UI.exe"
        SizeMB = [Math]::Round(((Get-Item $uiExe).Length / 1MB), 2)
        Location = $uiOutPath
    }
)

$artifacts | Format-Table -AutoSize
Write-Host "Build and packaging completed successfully!" -ForegroundColor Green
