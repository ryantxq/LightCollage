@echo off
setlocal
rem LightCollage - install "Pin Tu" shell context menu for the CURRENT USER only (HKCU, no admin).
rem All Chinese messages are printed by install.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" echo [FAILED] Install did not complete. Please read the messages above.
pause
exit /b %RC%
