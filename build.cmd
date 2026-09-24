@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "OUT=%~dp0dist"
if not exist "%OUT%" mkdir "%OUT%"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%OUT%\ASCOS-DbMount.exe" /win32manifest:"%~dp0asInvoker.manifest" /win32icon:"%~dp0ASCOS-DbMount.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Data.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "%~dp0AssemblyInfo.cs" "%~dp0Program.cs" "%~dp0Common.cs" "%~dp0SqlOps.cs" "%~dp0ConfirmForm.cs" "%~dp0DbMountForm.cs"
if errorlevel 1 exit /b 1
copy /y "%~dp0README.md" "%OUT%\" >nul
copy /y "%~dp0USER_GUIDE.html" "%OUT%\" >nul
copy /y "%~dp0LICENSE" "%OUT%\" >nul
copy /y "%~dp0ASCOS-DbMount.png" "%OUT%\" >nul
echo Build complete: %OUT%
