<#
.SYNOPSIS
    Creates complete distribution packages for ControlDesk: Client installer and VPS deployment bundle.

.DESCRIPTION
    Assembles two distinct distribution artifacts:
    1. Client Package (ControlDesk-Client-win-x64.zip): Self-contained .exe + app.manifest + LICENSE + README
    2. VPS Package (ControlDesk-VPS-Deployment.zip): docker-compose.yml, Dockerfiles, configs, deployment guide

.PARAMETER Configuration
    Build configuration to package (default: Release).

.PARAMETER OutputDirectory
    Base directory containing published artifacts (default: artifacts/Release).

.PARAMETER Version
    Version string for package naming (e.g., "1.0.0"). If omitted, reads from project file.

.PARAMETER SkipClient
    Skip creating the client package.

.PARAMETER SkipVps
    Skip creating the VPS deployment package.

.EXAMPLE
    .\scripts\package.ps1 -Version "1.0.0"
    .\scripts\package.ps1 -OutputDirectory C:\Builds\artifacts\Release -Version "1.0.0"
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$OutputDirectory = "artifacts/Release",

    [string]$Version = "",

    [switch]$SkipClient,

    [switch]$SkipVps
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptRoot

# Resolve absolute paths
$absOutputDir = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repoRoot $OutputDirectory
}

$clientSource = Join-Path $absOutputDir "client"
$serverSource = Join-Path $absOutputDir "server"
$packagesDir = Join-Path $absOutputDir "packages"

# Determine version
if (-not $Version) {
    $clientProj = "$repoRoot/src/ControlDesk.App/ControlDesk.App.csproj"
    if (Test-Path $clientProj) {
        $xml = [xml](Get-Content $clientProj)
        $Version = $xml.Project.PropertyGroup.Version ?? "1.0.0"
    } else {
        $Version = "1.0.0"
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$clientZipName = "ControlDesk-Client-win-x64-v$Version-$timestamp.zip"
$vpsZipName = "ControlDesk-VPS-Deployment-v$Version-$timestamp.zip"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ControlDesk Package Assembler" -ForegroundColor Cyan
Write-Host " Version:         $Version" -ForegroundColor Gray
Write-Host " Source Dir:      $absOutputDir" -ForegroundColor Gray
Write-Host " Output Dir:      $packagesDir" -ForegroundColor Gray
Write-Host " Skip Client:     $SkipClient" -ForegroundColor Gray
Write-Host " Skip VPS:        $SkipVps" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

Push-Location $repoRoot
try {
    # Ensure packages directory exists
    if (-not (Test-Path $packagesDir)) {
        New-Item -ItemType Directory -Path $packagesDir -Force | Out-Null
    }

    # ===== CLIENT PACKAGE =====
    if (-not $SkipClient) {
        Write-Host "`n[TASK] Creating Client Distribution Package..." -ForegroundColor Yellow

        if (-not (Test-Path $clientSource)) {
            throw "Client publish directory not found: $clientSource. Run publish.ps1 first."
        }

        $exePath = Join-Path $clientSource "ControlDesk.exe"
        if (-not (Test-Path $exePath)) {
            throw "ControlDesk.exe not found in $clientSource"
        }

        # Create staging directory
        $stagingClient = Join-Path $packagesDir "ClientStaging"
        if (Test-Path $stagingClient) {
            Remove-Item -Recurse -Force $stagingClient -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $stagingClient | Out-Null

        # Copy executable
        Copy-Item $exePath -Destination (Join-Path $stagingClient "ControlDesk.exe")

        # Copy app.manifest if exists
        $manifestPath = "$repoRoot/src/ControlDesk.App/app.manifest"
        if (Test-Path $manifestPath) {
            Copy-Item $manifestPath -Destination (Join-Path $stagingClient "app.manifest")
        }

        # Create LICENSE
        $licenseContent = @"
MIT License

Copyright (c) $([DateTime]::Now.Year) ControlDesk Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"@
        Set-Content -Path (Join-Path $stagingClient "LICENSE.txt") -Value $licenseContent -Encoding UTF8

        # Create INSTALL.txt with quick start
        $installContent = @"
ControlDesk v$Version - Quick Start
===================================

SYSTEM REQUIREMENTS:
- Windows 10 21H2+ or Windows 11 (x64)
- .NET 10.0 Runtime NOT required (self-contained)
- GPU with hardware H.264 encoding (Intel QuickSync, NVIDIA NVENC, AMD AMF) recommended

INSTALLATION:
1. Extract this archive to a folder of your choice (e.g., C:\Program Files\ControlDesk)
2. Run ControlDesk.exe - no installation required
3. On first launch, accept Windows Firewall prompt for inbound connections

QUICK CONNECT (India Controller <-> Dubai Host):
1. Both machines: Launch ControlDesk, note your Device ID (XXX-XXX-XXX)
2. Controller (India): Enter Host's Device ID in "Quick Connect" and click Connect
3. Host (Dubai): Accept incoming connection prompt
4. Session established - enjoy remote desktop!

UNATTENDED ACCESS SETUP (Dubai Host):
1. Settings > Remote Access > Enable Unattended Access
2. Add Controller's Device ID as "Trusted Device"
3. Controller can now connect without manual approval

NETWORK CONFIGURATION:
- Default Signaling: wss://signaling.controldesk.example.com/ws
- Default TURN: turn.controldesk.example.com
- Configure custom servers in Settings > Network

TROUBLESHOOTING:
- No connection: Verify both machines can reach signaling server
- High latency: Check if TURN relay is being used (see Diagnostics overlay)
- Black screen: Ensure host GPU drivers are up to date
- Input not working: Run as Administrator on both machines

LOGS & DATA:
- Settings/Logs: %APPDATA%\ControlDesk\
- Audit Log: %APPDATA%\ControlDesk\audit.db

SUPPORT:
- Documentation: https://github.com/your-org/ControlDesk/docs
- Issues: https://github.com/your-org/ControlDesk/issues

ControlDesk — Secure Remote Desktop Across Borders
"@
        Set-Content -Path (Join-Path $stagingClient "INSTALL.txt") -Value $installContent -Encoding UTF8

        # Create the ZIP
        $clientZipPath = Join-Path $packagesDir $clientZipName
        if (Test-Path $clientZipPath) {
            Remove-Item $clientZipPath -Force
        }

        Compress-Archive -Path (Join-Path $stagingClient "*") -DestinationPath $clientZipPath -Force
        $zipSizeMb = [Math]::Round((Get-Item $clientZipPath).Length / 1MB, 2)
        Write-Host "[SUCCESS] Client package: $clientZipPath ($zipSizeMb MB)" -ForegroundColor Green

        # Cleanup staging
        Remove-Item -Recurse -Force $stagingClient -ErrorAction SilentlyContinue
    }

    # ===== VPS DEPLOYMENT PACKAGE =====
    if (-not $SkipVps) {
        Write-Host "`n[TASK] Creating VPS Deployment Package..." -ForegroundColor Yellow

        $deploymentSource = "$repoRoot/deployment"
        if (-not (Test-Path $deploymentSource)) {
            throw "Deployment directory not found: $deploymentSource"
        }

        # Create staging directory
        $stagingVps = Join-Path $packagesDir "VpsStaging"
        if (Test-Path $stagingVps) {
            Remove-Item -Recurse -Force $stagingVps -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $stagingVps | Out-Null

        # Copy entire deployment folder structure
        Copy-Item -Path (Join-Path $deploymentSource "*") -Destination $stagingVps -Recurse -Force

        # Create .env.template
        $envTemplate = @"
# ControlDesk VPS Deployment Configuration
# Copy this file to .env and fill in your values

# Domain Configuration (REQUIRED - must have valid A records)
SIGNALING_DOMAIN=signaling.controldesk.example.com
TURN_DOMAIN=turn.controldesk.example.com

# Let's Encrypt Email (REQUIRED for automatic TLS)
LETSENCRYPT_EMAIL=admin@example.com

# TURN Secret (REQUIRED - generate with: openssl rand -base64 32)
# MUST match in both signaling server and coturn config
TURN_SECRET=your-super-secret-turn-key-change-this

# External VPS Public IP (OPTIONAL - coturn can auto-detect)
# EXTERNAL_IP=203.0.113.42
"@
        Set-Content -Path (Join-Path $stagingVps ".env.template") -Value $envTemplate -Encoding UTF8

        # Create README-VPS.md
        $vpsReadme = @"
# ControlDesk VPS Deployment Package v$Version

This package contains everything needed to deploy the ControlDesk Signaling Server
and coturn TURN/STUN relay on a Linux VPS for cross-border WebRTC connectivity
(India <-> Dubai).

## Quick Deploy

\```bash
# 1. Extract this archive on your VPS
unzip ControlDesk-VPS-Deployment-v$Version-*.zip
cd ControlDesk-VPS-Deployment

# 2. Copy and configure environment
cp .env.template .env
nano .env   # Fill in your domain, email, and TURN secret

# 3. Deploy
docker compose up -d

# 4. Verify
curl https://signaling.yourdomain.com/health
\```

## Contents

- docker-compose.yml          - Orchestrates signaling, coturn, Caddy
- signaling/Dockerfile        - Multi-stage build for ASP.NET Core signaling server
- coturn/turnserver.conf      - High-performance TURN/STUN configuration
- caddy/Caddyfile             - Reverse proxy with automatic HTTPS (Let's Encrypt)
- deployment-guide.md         - Complete step-by-step deployment instructions

## Architecture

\```
Internet
    │
    ├── Controller (India) ──────┐
    │                              │ WebRTC ICE (STUN + TURN)
    └── Host (Dubai) ────────────┘
                                │
                    ┌───────────┴───────────┐
                    ▼                       ▼
            ┌───────────────┐       ┌───────────────┐
            │    Caddy      │       │    coturn     │
            │   :80/:443    │       │ :3478/:5349   │
            └───────┬───────┘       └───────┬───────┘
                    │                       │
                    ▼                       ▼
            ┌───────────────────────────────────┐
            │      Signaling Server (:5000)     │
            │  (ASP.NET Core WebSocket API)     │
            └───────────────────────────────────┘
\```

## Client Configuration

After deployment, configure ControlDesk clients with:

- Signaling Server: `wss://signaling.yourdomain.com/ws`
- TURN Domain: `turn.yourdomain.com`

## Security Notes

- The TURN_SECRET must be a 32-byte base64 string (generate with `openssl rand -base64 32`)
- Keep .env file secure - it contains your TURN secret
- Caddy automatically provisions TLS certificates via Let's Encrypt
- Rotate TURN secret quarterly (see deployment-guide.md)

## Requirements

- Linux VPS (Ubuntu 22.04/24.04, Debian 12)
- 2 vCPU, 4 GB RAM minimum
- Public static IP
- Domain name with A records pointing to VPS IP
- Ports: 80, 443 (TCP), 3478, 5349 (TCP/UDP), 49152-65535 (UDP)

## Support

See deployment-guide.md for detailed instructions, troubleshooting, and maintenance procedures.

ControlDesk — Secure Remote Desktop Across Borders
"@
        Set-Content -Path (Join-Path $stagingVps "README-VPS.md") -Value $vpsReadme -Encoding UTF8

        # Create the ZIP
        $vpsZipPath = Join-Path $packagesDir $vpsZipName
        if (Test-Path $vpsZipPath) {
            Remove-Item $vpsZipPath -Force
        }

        Compress-Archive -Path (Join-Path $stagingVps "*") -DestinationPath $vpsZipPath -Force
        $zipSizeMb = [Math]::Round((Get-Item $vpsZipPath).Length / 1MB, 2)
        Write-Host "[SUCCESS] VPS package: $vpsZipPath ($zipSizeMb MB)" -ForegroundColor Green

        # Cleanup staging
        Remove-Item -Recurse -Force $stagingVps -ErrorAction SilentlyContinue
    }

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] Package assembly completed." -ForegroundColor Green
    Write-Host " Packages directory: $packagesDir" -ForegroundColor Green
    if (-not $SkipClient) {
        Write-Host "   Client: $clientZipName" -ForegroundColor Green
    }
    if (-not $SkipVps) {
        Write-Host "   VPS:    $vpsZipName" -ForegroundColor Green
    }
    Write-Host "============================================================" -ForegroundColor Green
}
finally {
    Pop-Location
}