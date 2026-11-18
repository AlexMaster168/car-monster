@echo off
rem Runs the gameplay PlayMode tests (physics, AI, nitro, takedown + respawn) in batchmode.
rem Results: Logs\test-results.xml. Close the project in the Unity Editor first.
setlocal
cd /d "%~dp0"

for /f "tokens=2" %%v in ('findstr /b /c:"m_EditorVersion:" "ProjectSettings\ProjectVersion.txt"') do set "UNITY_VERSION=%%v"
if not defined UNITY_EXE set "UNITY_EXE=%ProgramFiles%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
if not exist "%UNITY_EXE%" (
    echo [Car Monster] Unity %UNITY_VERSION% not found: "%UNITY_EXE%"
    exit /b 1
)

if not exist Logs mkdir Logs
echo [Car Monster] Running PlayMode tests...
start "" /wait "%UNITY_EXE%" -batchmode -projectPath "%CD%" -runTests -testPlatform PlayMode -testResults "%CD%\Logs\test-results.xml" -logFile "%CD%\Logs\tests.log"
if errorlevel 1 (
    echo [Car Monster] SOME TESTS FAILED. See Logs\test-results.xml
    exit /b 1
)
echo [Car Monster] All tests passed.
endlocal
