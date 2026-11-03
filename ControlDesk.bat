@echo off
setlocal
title ControlDesk Launcher
set "APP_DIR=%USERPROFILE%\Desktop\ControlDesk"
set "REPO_DIR=G:\Coding-main\Control Desk"

if exist "%APP_DIR%\ControlDesk.exe" (
    start "" "%APP_DIR%\ControlDesk.exe" %*
    exit /b 0
)

if exist "%APP_DIR%\ControlDesk.App.exe" (
    start "" "%APP_DIR%\ControlDesk.App.exe" %*
    exit /b 0
)

if exist "%REPO_DIR%\src\ControlDesk.App\ControlDesk.App.csproj" (
    echo Launching ControlDesk via .NET...
    cd /d "%REPO_DIR%"
    dotnet run --project "%REPO_DIR%\src\ControlDesk.App\ControlDesk.App.csproj" -c Release --no-restore %*
    if %ERRORLEVEL% NEQ 0 (
        dotnet run --project "%REPO_DIR%\src\ControlDesk.App\ControlDesk.App.csproj" -c Release %*
    )
    exit /b %ERRORLEVEL%
)

echo [!] Could not locate ControlDesk executable.
echo     Run Setup-ControlDesk.bat to install the application.
pause