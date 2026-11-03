<#
.SYNOPSIS
    Runs the automated unit and integration test suites for ControlDesk.

.DESCRIPTION
    Executes xUnit test suites across Core, Security, Network, Transfer,
    and Integration test projects. Provides failure tracking and detailed summaries.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release).

.PARAMETER Project
    Specific test project to run (e.g., "Network", "Security", "Integration").
    If omitted, runs all 5 test projects.

.PARAMETER Filter
    xUnit test case filter expression (e.g., "FullyQualifiedName~Loopback").

.PARAMETER NoBuild
    Skip building before running tests.

.PARAMETER Verbosity
    Test runner verbosity: quiet, minimal, normal, detailed (default: minimal).

.EXAMPLE
    .\scripts\test.ps1
    .\scripts\test.ps1 -Project Integration
    .\scripts\test.ps1 -Filter "FullyQualifiedName~Ed25519"
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Project = "",

    [string]$Filter = "",

    [switch]$NoBuild,

    [ValidateSet("quiet", "minimal", "normal", "detailed")]
    [string]$Verbosity = "minimal"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptRoot

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ControlDesk Automated Test Runner" -ForegroundColor Cyan
Write-Host " Configuration: $Configuration" -ForegroundColor Gray
Write-Host " Filter:        $(if ($Filter) { $Filter } else { '<None>' })" -ForegroundColor Gray
Write-Host " Target Suite:  $(if ($Project) { $Project } else { 'All Suites' })" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

$allTestProjects = @(
    "tests/ControlDesk.Core.Tests/ControlDesk.Core.Tests.csproj",
    "tests/ControlDesk.Security.Tests/ControlDesk.Security.Tests.csproj",
    "tests/ControlDesk.Network.Tests/ControlDesk.Network.Tests.csproj",
    "tests/ControlDesk.Transfer.Tests/ControlDesk.Transfer.Tests.csproj",
    "tests/ControlDesk.Integration.Tests/ControlDesk.Integration.Tests.csproj"
)

# Filter projects if user specified one
$projectsToRun = if ($Project) {
    $matched = $allTestProjects | Where-Object { $_ -like "*$Project*" }
    if (-not $matched) {
        throw "No test project found matching: $Project. Available: Core, Security, Network, Transfer, Integration"
    }
    $matched
} else {
    $allTestProjects
}

Push-Location $repoRoot
try {
    $dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "dotnet CLI is not installed or not found in system PATH."
    }

    $failedSuites = @()
    $passedSuites = @()
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    foreach ($proj in $projectsToRun) {
        $projName = [System.IO.Path]::GetFileNameWithoutExtension($proj)
        Write-Host "`n------------------------------------------------------------" -ForegroundColor DarkGray
        Write-Host " [RUNNING] $projName" -ForegroundColor Yellow
        Write-Host "------------------------------------------------------------" -ForegroundColor DarkGray

        $argsList = @("test", $proj, "-c", $Configuration, "--nologo", "-v", $Verbosity)
        if ($NoBuild) {
            $argsList += "--no-build"
        }
        if ($Filter) {
            $argsList += "--filter"
            $argsList += $Filter
        }

        & dotnet $argsList

        if ($LASTEXITCODE -eq 0) {
            Write-Host "[PASSED] $projName" -ForegroundColor Green
            $passedSuites += $projName
        } else {
            Write-Host "[FAILED] $projName (Exit Code: $LASTEXITCODE)" -ForegroundColor Red
            $failedSuites += $projName
        }
    }

    $stopwatch.Stop()
    $elapsedSec = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)

    Write-Host "`n============================================================" -ForegroundColor Cyan
    Write-Host " TEST RUN SUMMARY ($elapsedSec seconds)" -ForegroundColor Cyan
    Write-Host " Passed Suites: $($passedSuites.Count)" -ForegroundColor Green
    foreach ($p in $passedSuites) {
        Write-Host "   + $p" -ForegroundColor Green
    }

    if ($failedSuites.Count -gt 0) {
        Write-Host " Failed Suites: $($failedSuites.Count)" -ForegroundColor Red
        foreach ($f in $failedSuites) {
            Write-Host "   - $f" -ForegroundColor Red
        }
        Write-Host "============================================================" -ForegroundColor Red
        exit 1
    } else {
        Write-Host " All test suites completed successfully!" -ForegroundColor Green
        Write-Host "============================================================" -ForegroundColor Green
        exit 0
    }
}
finally {
    Pop-Location
}
