@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo  dotnet.exe was not found.
  echo  Install .NET 6 SDK from https://dotnet.microsoft.com/download/dotnet/6.0
  echo  or use Visual Studio Installer - workload ".NET desktop development".
  echo.
  pause
  exit /b 1
)

echo Restoring packages...
dotnet restore "STF DDS Converter.sln"
if errorlevel 1 goto :fail

echo Building Release...
dotnet build "STF DDS Converter.sln" -c Release --no-restore
if errorlevel 1 goto :fail

echo.
echo  Build OK:
echo  STF DDS Converter\bin\Release\net6.0-windows\STF DDS Converter.exe
echo.
pause
exit /b 0

:fail
echo.
echo  Build failed. Open BUILD.md for NU1101 / NuGet / SDK fixes.
echo.
pause
exit /b 1
