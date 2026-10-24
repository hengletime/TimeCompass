@echo off
rem Build TimeCompass screensaver (ASCII only, no parens inside if-blocks)
rem The HTML is embedded as a Win32 resource so the .scr is self-contained / portable.
cd /d %~dp0

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] .NET Framework 4 compiler not found.
  pause
  exit /b 1
)

rem Locate the HTML in the parent folder and copy it to an ASCII-named embed file
set HTMLSRC=
for %%f in ("..\*.html") do set HTMLSRC=%%~ff
if "%HTMLSRC%"=="" (
  echo [ERROR] Source HTML not found in parent folder.
  pause
  exit /b 1
)
copy /y "%HTMLSRC%" "_embed.html" >nul

echo Compiling Scr.cs with embedded HTML -^> TimeCompass.scr ...
"%CSC%" /nologo /codepage:65001 /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll /res:_embed.html,tcembed /out:"TimeCompass.scr" "Scr.cs"

rem v4.0 (D-17): propagate csc failure - otherwise callers mistake the old scr for a fresh build
if errorlevel 1 (
  echo [ERROR] csc compile FAILED - TimeCompass.scr was NOT updated.
  del "_embed.html" >nul 2>&1
  exit /b 1
)

del "_embed.html" >nul 2>&1

if exist "TimeCompass.scr" (
  echo.
  echo Build OK: %~dp0TimeCompass.scr
  echo The HTML is embedded - this .scr is portable to any PC with Edge.
  echo.
  echo Usage:
  echo   1. Copy TimeCompass.scr to any PC
  echo   2. Double-click for a fullscreen preview
  echo   3. Right-click - Install - copies to System32
  echo   4. Windows Settings -^> Personalization -^> Lock screen -^> Screen saver
  echo   5. Move mouse or press any key to exit
) else (
  echo Build FAILED. Please check Scr.cs
)
echo.
pause
