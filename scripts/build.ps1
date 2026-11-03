<#
.SYNOPSIS
    Builds the ControlDesk solution or specific subprojects.

.DESCRIPTION
    Compiles ControlDesk for Windows (.NET 10.0 / net10.0-windows).
    Supports Clean, Configuration (Debug/Release), and Project targeting.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release).

.PARAMETER Clean
    If specified, performs a clean build before compiling.

.PARAMETER Target
    Optional specific project path or name to build. Defaults to ControlDesk.sln.

.EXAMPLE
    .\scripts\build.ps1 -Configuration Release
    .\scripts\build.ps1 -Clean -Configuration Debug
    .\scripts\build.ps1 -Target src\ControlDesk.App\ControlDesk.App.csproj
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Clean,

    [string]$Target = "ControlDesk.sln"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptRoot

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ControlDesk Build Engine" -ForegroundColor Cyan
Write-Host " Solution/Target: $Target" -ForegroundColor Gray
Write-Host " Configuration:   $Configuration" -ForegroundColor Gray
Write-Host " Repository Root: $repoRoot" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

Push-Location $repoRoot
try {
    # Verify dotnet SDK availability
    $dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "dotnet CLI is not installed or not found in system PATH. Please install .NET 10.0 SDK."
    }

    $sdkVersion = & dotnet --version
    Write-Host "[INFO] Detected .NET SDK: $sdkVersion" -ForegroundColor Green

    # Clean if requested
    if ($Clean) {
        Write-Host "`n[TASK] Cleaning target: $Target..." -ForegroundColor Yellow
        & dotnet clean $Target -c $Configuration --nologo -v minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Clean failed with exit code $LASTEXITCODE"
        }
        Write-Host "[SUCCESS] Clean completed." -ForegroundColor Green
    }

    # Restore dependencies
    Write-Host "`n[TASK] Restoring NuGet dependencies..." -ForegroundColor Yellow
    & dotnet restore $Target --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed with exit code $LASTEXITCODE"
    }

    # Build solution
    Write-Host "`n[TASK] Building $Target [$Configuration]..." -ForegroundColor Yellow
    & dotnet build $Target -c $Configuration --no-restore --nologo -v minimal `
        -p:TreatWarningsAsErrors=false

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] Build completed successfully." -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
}
finally {
    Pop-Location
}
