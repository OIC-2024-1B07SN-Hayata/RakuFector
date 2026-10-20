@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Repair NuGet Restore and Build
set "SOLUTION="
for %%S in (*.sln) do set "SOLUTION=%%~nxS"
set "LOG=%~dp0restore_and_build_log.txt"
echo Integrated app - restore and build
if not defined SOLUTION (echo ERROR: Solution not found. & goto failed)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET SDK not found.
  echo Install Visual Studio 2022 with .NET desktop development and .NET 8 SDK.
  goto failed
)
echo Checking .NET SDK...
dotnet --list-sdks
for %%P in (ChordDetection.Core ChordDetection.Infrastructure CommonAudio DrumEditor) do (
  if exist "src\%%P\obj" rmdir /s /q "src\%%P\obj"
)
for /d %%P in (src\*) do (
  if exist "%%P\*.csproj" (
    if exist "%%P\obj" rmdir /s /q "%%P\obj"
  )
)
echo [1/2] Restoring NuGet packages...
echo Restore and build diagnostic log > "%LOG%"
dotnet restore "%SOLUTION%" -p:Platform=x64 --force -v:minimal >> "%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: NuGet restore failed.
  type "%LOG%"
  goto failed
)
echo [2/2] Building the solution...
dotnet build "%SOLUTION%" --no-restore -c Debug -p:Platform=x64 -v:minimal >> "%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: Build failed. See restore_and_build_log.txt.
  type "%LOG%"
  goto failed
)
echo.
echo SUCCESS: Restore and build completed.
echo The EXE is under src\[main project]\bin\x64\Debug\net8.0-windows.
echo Build log: %LOG%
pause
exit /b 0
:failed
echo.
echo Build is not yet confirmed successful.
pause
exit /b 1
