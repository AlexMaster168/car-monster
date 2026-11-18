@echo off
rem Opens the Car Monster project in the matching Unity Editor version.
rem The version is read from ProjectSettings\ProjectVersion.txt, the editor is looked up in Unity Hub.
rem Custom editor path: set UNITY_EXE=D:\Unity\6000.0.77f1\Editor\Unity.exe
setlocal
cd /d "%~dp0"

for /f "tokens=2" %%v in ('findstr /b /c:"m_EditorVersion:" "ProjectSettings\ProjectVersion.txt"') do set "UNITY_VERSION=%%v"
if not defined UNITY_EXE set "UNITY_EXE=%ProgramFiles%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"

if not exist "%UNITY_EXE%" (
    echo [Car Monster] Unity %UNITY_VERSION% not found: "%UNITY_EXE%"
    echo Install it via Unity Hub or set the UNITY_EXE environment variable.
    pause
    exit /b 1
)

echo [Car Monster] Opening project in Unity %UNITY_VERSION%...
start "" "%UNITY_EXE%" -projectPath "%CD%"
endlocal
