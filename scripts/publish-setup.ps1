[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptRoot

$setupProj = Join-Path $repoRoot "src/ControlDesk.Setup/ControlDesk.Setup.csproj"
$setupOut = Join-Path $repoRoot "artifacts/Release/setup"

if (-not (Test-Path $setupOut)) {
    New-Item -ItemType Directory -Path $setupOut -Force | Out-Null
}

& dotnet publish $setupProj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -o $setupOut `
    --nologo -v minimal

if ($LASTEXITCODE -ne 0) {
    throw "Setup publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $setupOut "ControlDesk-Setup.exe"
if (Test-Path $exePath) {
    $exeSizeMb = [Math]::Round((Get-Item $exePath).Length / 1MB, 2)
    Write-Host "[SUCCESS] Generated: $exePath ($exeSizeMb MB)" -ForegroundColor Green
} else {
    throw "Expected setup executable not found at: $exePath"
}
