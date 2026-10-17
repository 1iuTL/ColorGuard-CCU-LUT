@echo off
setlocal
if not exist dist mkdir dist
set CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /optimize+ /target:winexe ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  /out:dist\ColorGuard.exe src\ColorGuard.cs
echo.
echo Built: dist\ColorGuard.exe
endlocal
