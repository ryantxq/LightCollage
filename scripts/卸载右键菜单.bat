@echo off
setlocal
rem LightCollage - uninstall shell context menu for the CURRENT USER (HKCU only).
rem All Chinese messages are printed by uninstall.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" echo [WARNING] Uninstall finished with warnings. Please read the messages above.
pause
exit /b %RC%
