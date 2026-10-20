@echo off
setlocal
cd /d "%~dp0"
for %%S in (*.sln) do (
  start "" "%%~fS"
  exit /b 0
)
echo Solution file not found.
pause
exit /b 1
