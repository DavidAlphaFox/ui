@echo off
setlocal
set PATH=%GitToolPath%;%PATH%

cls

.paket\paket.exe restore --touch-affected-refs
if errorlevel 1 (
  exit /b %errorlevel%
)

if "%VisualStudioVersion%"=="" (
  set VisualStudioVersion=15.0
)

packages\build\FAKE\tools\FAKE.exe build.fsx %*
