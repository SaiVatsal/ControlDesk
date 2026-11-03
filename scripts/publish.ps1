<#
.SYNOPSIS
    Publishes ControlDesk as a self-contained, single-file Windows desktop application.

.DESCRIPTION
    Compiles and publishes:
    1. ControlDesk.App as a single-file, self-contained win-x64 executable.
    2. (Optional) ControlDesk.Server as a lightweight cross-platform server binary.

.PARAMETER Configuration
    Build configuration: Release or Debug (default: Release).

.PARAMETER Runtime
    Target runtime identifier for client: win-x64 or win-arm64 (default: win-x64).

.PARAMETER OutputDirectory
    Base output directory for published artifacts (default: artifacts/Release).

.PARAMETER IncludeServer
    Also publish the ASP.NET Core Signaling Server.

.PARAMETER ServerRuntime
    Target runtime identifier for server: linux-x64, win-x64, etc. (default: linux-x64).

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -Configuration Release -IncludeServer
    .\scripts\publish.ps1 -OutputDirectory C:\Releases\ControlDesk
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [string]$OutputDirectory = "artifacts/Release",

    [switch]$IncludeServer,

    [string]$ServerRuntime = "linux-x64"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptRoot

# Resolve absolute output directory
$absOutputDir = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repoRoot $OutputDirectory
}

$clientOut = Join-Path $absOutputDir "client"
$serverOut = Join-Path $absOutputDir "server"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ControlDesk Production Publisher" -ForegroundColor Cyan
Write-Host " Configuration:    $Configuration" -ForegroundColor Gray
Write-Host " Client Runtime:   $Runtime" -ForegroundColor Gray
Write-Host " Output Directory: $absOutputDir" -ForegroundColor Gray
Write-Host " Include Server:   $IncludeServer" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

Push-Location $repoRoot
try {
    $dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "dotnet CLI is not installed or not found in system PATH."
    }

    # Ensure output directories exist
    if (-not (Test-Path $clientOut)) {
        New-Item -ItemType Directory -Path $clientOut -Force | Out-Null
    }

    # Publish ControlDesk.App
    Write-Host "`n[TASK] Publishing ControlDesk.App (Single-File, Self-Contained $Runtime)..." -ForegroundColor Yellow
    $clientProj = "src/ControlDesk.App/ControlDesk.App.csproj"

    & dotnet publish $clientProj `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=embedded `
        -o $clientOut `
        --nologo -v minimal

    if ($LASTEXITCODE -ne 0) {
        throw "Client publish failed with exit code $LASTEXITCODE"
    }

    $exePath = Join-Path $clientOut "ControlDesk.exe"
    if (Test-Path $exePath) {
        $exeSizeMb = [Math]::Round((Get-Item $exePath).Length / 1MB, 2)
        Write-Host "[SUCCESS] Generated: $exePath ($exeSizeMb MB)" -ForegroundColor Green
    } else {
        throw "Expected executable not found at: $exePath"
    }

    # Publish ControlDesk.Server if requested
    if ($IncludeServer) {
        if (-not (Test-Path $serverOut)) {
            New-Item -ItemType Directory -Path $serverOut -Force | Out-Null
        }

        Write-Host "`n[TASK] Publishing ControlDesk.Server ($ServerRuntime)..." -ForegroundColor Yellow
        $serverProj = "src/ControlDesk.Server/ControlDesk.Server.csproj"

        & dotnet publish $serverProj `
            -c $Configuration `
            -r $ServerRuntime `
            --self-contained false `
            -p:UseAppHost=false `
            -o $serverOut `
            --nologo -v minimal

        if ($LASTEXITCODE -ne 0) {
            throw "Server publish failed with exit code $LASTEXITCODE"
        }

        Write-Host "[SUCCESS] Server binaries published to: $serverOut" -ForegroundColor Green
    }

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] Publish completed successfully." -ForegroundColor Green
    Write-Host " Executable: $exePath" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
}
finally {
    Pop-Location
}
