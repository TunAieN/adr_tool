@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ==============================================================
echo V2.1.61 - Windows build ONLY (does not run Appium or Android)
echo ==============================================================
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET SDK was not found in PATH.
  pause
  exit /b 1
)
dotnet --version
dotnet build ".\AndroidMultiDeviceV1\AndroidMultiDeviceV1.csproj" --configuration Release
if errorlevel 1 (
  echo BUILD FAILED. Copy all compiler errors to ChatGPT.
  pause
  exit /b 1
)
echo BUILD SUCCEEDED.
pause
exit /b 0
