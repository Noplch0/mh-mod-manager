@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"
set EXITCODE=%ERRORLEVEL%
if %EXITCODE%==0 if exist "%~dp0dist\mh-mod-manager" rmdir /s /q "%~dp0dist\mh-mod-manager"
echo.
if not %EXITCODE%==0 (
  echo Publish failed, exit code %EXITCODE%.
) else (
  echo Publish finished.
)
pause
exit /b %EXITCODE%
