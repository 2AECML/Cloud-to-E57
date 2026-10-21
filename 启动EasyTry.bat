@echo off
setlocal

set "APP_DIR=%~dp0EasyTry\bin\x64\Release\net8.0-windows-tasklist"
set "APP_EXE=%APP_DIR%\EasyTry.exe"

if not exist "%APP_EXE%" goto missing

pushd "%APP_DIR%"
start "" "EasyTry.exe"
set "START_RESULT=%ERRORLEVEL%"
popd

if not "%START_RESULT%"=="0" goto failed
exit /b 0

:missing
echo [EasyTry] Executable not found:
echo "%APP_EXE%"
echo.
echo Please build the x64 Release version first.
pause
exit /b 1

:failed
echo [EasyTry] Failed to start. Error code: %START_RESULT%
pause
exit /b %START_RESULT%
