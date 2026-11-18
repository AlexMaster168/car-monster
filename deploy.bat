@echo off
rem Builds a Windows x64 player into Builds\Windows and zips it to Builds\CarMonster-Windows.zip.
rem Close the project in the Unity Editor first (batchmode cannot open a project that is already open).
rem   deploy.bat          - build + zip
rem   deploy.bat nozip    - build only
setlocal
cd /d "%~dp0"

for /f "tokens=2" %%v in ('findstr /b /c:"m_EditorVersion:" "ProjectSettings\ProjectVersion.txt"') do set "UNITY_VERSION=%%v"
if not defined UNITY_EXE set "UNITY_EXE=%ProgramFiles%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"

if not exist "%UNITY_EXE%" (
    echo [Car Monster] Unity %UNITY_VERSION% not found: "%UNITY_EXE%"
    exit /b 1
)

if not exist Logs mkdir Logs
echo [Car Monster] Building Windows x64 with Unity %UNITY_VERSION%... log: Logs\build.log
start "" /wait "%UNITY_EXE%" -batchmode -quit -projectPath "%CD%" -executeMethod CarMonster.EditorTools.CarMonsterBatch.BuildWindows -logFile "%CD%\Logs\build.log"
if errorlevel 1 (
    echo [Car Monster] BUILD FAILED. See Logs\build.log
    exit /b 1
)
if not exist "Builds\Windows\CarMonster.exe" (
    echo [Car Monster] CarMonster.exe was not produced. See Logs\build.log
    exit /b 1
)

if /i "%~1"=="nozip" goto done
echo [Car Monster] Packing Builds\CarMonster-Windows.zip...
powershell -NoProfile -Command "Compress-Archive -Path 'Builds\Windows\*' -DestinationPath 'Builds\CarMonster-Windows.zip' -Force"
if errorlevel 1 (
    echo [Car Monster] Failed to create the zip archive.
    exit /b 1
)

:done
echo [Car Monster] Done: Builds\Windows\CarMonster.exe
endlocal
