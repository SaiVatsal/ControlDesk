@echo off
setlocal EnableDelayedExpansion
title ControlDesk - Setup Manager
color 0B

echo ===============================================================================
echo                ControlDesk - Windows Remote Desktop Setup
echo                     Cross-Border Edition (India ^<--^> Dubai)
echo ===============================================================================
echo.

set "DESKTOP_DIR=%USERPROFILE%\Desktop"
set "APP_DIR=%DESKTOP_DIR%\ControlDesk"
set "REPO_DIR=%~dp0"
if "%REPO_DIR:~-1%"=="\" set "REPO_DIR=%REPO_DIR:~0,-1%"

if not exist "%APP_DIR%" mkdir "%APP_DIR%" >nul 2>&1

echo [*] Target Directory: %APP_DIR%
echo.

:: Check .NET SDK
echo [1/4] Checking .NET environment...
dotnet --version >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do echo   [OK] .NET SDK is available: %%v
) else (
    echo   [!] .NET SDK not detected in PATH.
    echo   [*] Installing .NET 10 SDK via winget...
    winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
    if %ERRORLEVEL% NEQ 0 (
        winget install --id Microsoft.DotNet.SDK.9 --exact --accept-package-agreements --accept-source-agreements
    )
    set "PATH=%PATH%;C:\Program Files\dotnet"
)

:: Build and Publish
echo.
echo [2/4] Publishing ControlDesk Single-File Executable...
if exist "%REPO_DIR%\src\ControlDesk.App\ControlDesk.App.csproj" (
    dotnet publish "%REPO_DIR%\src\ControlDesk.App\ControlDesk.App.csproj" ^
        -c Release ^
        -r win-x64 ^
        --self-contained true ^
        -p:PublishSingleFile=true ^
        -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:EnableCompressionInSingleFile=true ^
        -p:PublishTrimmed=false ^
        -o "%APP_DIR%" ^
        --nologo -v minimal

    if exist "%APP_DIR%\ControlDesk.App.exe" (
        if not exist "%APP_DIR%\ControlDesk.exe" (
            copy /y "%APP_DIR%\ControlDesk.App.exe" "%APP_DIR%\ControlDesk.exe" >nul 2>&1
        )
    )
)

:: Firewall Rule
echo.
echo [3/4] Configuring Outbound Firewall Rules...
netsh advfirewall firewall add rule name="ControlDesk Outbound WSS and WebRTC" dir=out action=allow program="%APP_DIR%\ControlDesk.exe" enable=yes >nul 2>&1

:: Create Shortcut on Desktop
echo.
echo [4/4] Creating Desktop Shortcut...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%DESKTOP_DIR%\ControlDesk.lnk'); if (Test-Path '%APP_DIR%\ControlDesk.exe') { $s.TargetPath = '%APP_DIR%\ControlDesk.exe'; } else { $s.TargetPath = '%APP_DIR%\Run.bat'; } $s.WorkingDirectory = '%APP_DIR%'; $s.Description = 'ControlDesk - Secure Windows Remote Desktop'; $s.Save();" >nul 2>&1

echo.
echo ===============================================================================
echo   Setup Complete!
echo   Application: %APP_DIR%\ControlDesk.exe
echo   Shortcut   : %DESKTOP_DIR%\ControlDesk.lnk
echo ===============================================================================
echo.
set /p "LAUNCH_NOW=Launch ControlDesk now? [Y/N, default=Y]: "
if /i "!LAUNCH_NOW!"=="" set "LAUNCH_NOW=Y"
if /i "!LAUNCH_NOW!"=="Y" (
    if exist "%APP_DIR%\ControlDesk.exe" start "" "%APP_DIR%\ControlDesk.exe"
)
pause
