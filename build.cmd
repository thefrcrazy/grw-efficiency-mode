@echo off
REM Builds dist\GRW-Efficiency-Setup.exe with the C# compiler built into Windows.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo C# compiler not found. .NET Framework 4.x is required.
  exit /b 1
)
cd /d "%~dp0"
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:dist\GRW-Efficiency-Setup.exe ^
  /win32icon:src\app.ico /win32manifest:src\app.manifest ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  src\GRWEfficiencyMode.cs
if errorlevel 1 exit /b 1
echo Built: dist\GRW-Efficiency-Setup.exe
