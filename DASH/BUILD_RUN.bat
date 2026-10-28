@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
echo ================================================================
echo   ANDROID MULTI DEVICE V1 - BUILD + RUN
 echo ================================================================
where dotnet >nul 2>&1
if errorlevel 1 (
 echo ERROR: .NET 8 SDK not found in PATH.
 echo Install .NET 8 SDK, then open a new CMD.
 pause
 exit /b 1
)
echo [1/3] .NET SDK version:
dotnet --version
if errorlevel 1 goto :ERROR
echo [2/3] Building isolated C# Windows Forms project...
dotnet build "%~dp0AndroidMultiDeviceV1\AndroidMultiDeviceV1.csproj" -c Release -o "%~dp0dist" -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
if errorlevel 1 goto :ERROR
if not exist "%~dp0dist\AndroidMultiDeviceV1.exe" goto :ERROR
echo [3/3] Opening Multi-Device Manager...
start "" "%~dp0dist\AndroidMultiDeviceV1.exe"
exit /b 0
:ERROR
echo.
echo BUILD FAILED. Copy full error text here for diagnosis.
pause
exit /b 1
